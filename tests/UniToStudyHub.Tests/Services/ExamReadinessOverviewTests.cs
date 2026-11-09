using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ExamReadinessOverviewTests
{
    private static readonly DateTimeOffset AttemptedAt =
        new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedAt = AttemptedAt.AddDays(1);

    private static ExamReadinessService CreateService() =>
        new(new CareerApplicationService(new CareerCalculatorService()));

    [Fact]
    public void OverviewSortsCoursesAndSeparatesPartialMissingAndCompleteEvidence()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "analisi", Name = "Analisi", Cfu = 6 },
                new() { Id = "fisica", Name = "Fisica", Cfu = 9 },
                new() { Id = "programmazione", Name = "Programmazione", Cfu = 12 },
                new() { Id = "storia", Name = "Storia", Cfu = 6 },
                new() { Id = "archiviata", Name = "Materia archiviata", Cfu = 6, Archived = true },
                new() { Id = "stato-archiviato", Name = "Materia con stato archiviato", Cfu = 6, Status = CourseStatus.Archived }
            }
        };
        var syllabus = new SyllabusTrackerService();
        var analysisTopic = syllabus.Add(data, "analisi", "Limiti", estimatedMinutes: 120);
        syllabus.UpdateStatus(data, analysisTopic.Id, SyllabusTopicStatus.Completed, new DateTime(2026, 10, 10));
        var programmingTopic = syllabus.Add(data, "programmazione", "Strutture dati", estimatedMinutes: 180);
        syllabus.UpdateStatus(data, programmingTopic.Id, SyllabusTopicStatus.Completed, new DateTime(2026, 10, 11));

        var career = new CareerApplicationService(new CareerCalculatorService());
        career.RecordResult(data, "fisica", ExamOutcome.Passed, GradeDecision.Accepted,
            numericGrade: 18, honors: false, isPassFail: false,
            attemptedAt: AttemptedAt, recordedAt: RecordedAt);
        career.RecordResult(data, "programmazione", ExamOutcome.Passed, GradeDecision.Accepted,
            numericGrade: 24, honors: false, isPassFail: false,
            attemptedAt: AttemptedAt, recordedAt: RecordedAt);

        var beforeCourseCount = data.Courses.Count;
        var beforeTopicCount = data.SyllabusTopics.Count;
        var beforeAttemptCount = data.CareerExams.Sum(x => x.Attempts.Count);

        var overview = CreateService().CalculateOverview(data);

        Assert.Equal(4, overview.TotalCourses);
        Assert.Equal(3, overview.CoursesWithScore);
        Assert.Equal(1, overview.CoursesWithoutScore);
        Assert.Equal(1, overview.FullyAssessedCourses);
        Assert.Equal(0, overview.EstimatedRemainingSyllabusMinutes);
        Assert.Equal(60m, overview.AverageAvailableScore);
        Assert.Equal(57.78m, overview.CfuWeightedAvailableScore);

        Assert.Equal(
            new[] { "fisica", "programmazione", "analisi", "storia" },
            overview.Courses.Select(x => x.CourseId));
        Assert.Equal(0m, overview.Courses[0].Score);
        Assert.Equal(80m, overview.Courses[1].Score);
        Assert.Equal(100m, overview.Courses[2].Score);
        Assert.Null(overview.Courses[3].Score);
        Assert.False(overview.Courses[0].IsComplete);
        Assert.True(overview.Courses[1].IsComplete);
        Assert.Contains(overview.Courses[3].MissingData,
            item => item.Contains("Syllabus", StringComparison.Ordinal));

        // Calculating an overview is read-only: it must not create or alter study data.
        Assert.Equal(beforeCourseCount, data.Courses.Count);
        Assert.Equal(beforeTopicCount, data.SyllabusTopics.Count);
        Assert.Equal(beforeAttemptCount, data.CareerExams.Sum(x => x.Attempts.Count));
        Assert.DoesNotContain(overview.Courses, x => x.CourseId == "archiviata");
        Assert.DoesNotContain(overview.Courses, x => x.CourseId == "stato-archiviato");
    }

    [Fact]
    public void OverviewWithNoActiveCoursesHasNoInventedAggregateScores()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "archived", Name = "Archiviata", Cfu = 6, Archived = true }
            }
        };

        var overview = CreateService().CalculateOverview(data);

        Assert.Equal(0, overview.TotalCourses);
        Assert.Equal(0, overview.CoursesWithScore);
        Assert.Equal(0, overview.CoursesWithoutScore);
        Assert.Equal(0, overview.FullyAssessedCourses);
        Assert.Null(overview.AverageAvailableScore);
        Assert.Null(overview.CfuWeightedAvailableScore);
        Assert.Empty(overview.Courses);
    }

    [Fact]
    public void CfuWeightedAverageIsUnavailableWhenScoredCoursesHaveNoPositiveCfu()
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "laboratorio", Name = "Laboratorio", Cfu = 0 }
            }
        };
        var syllabus = new SyllabusTrackerService();
        var topic = syllabus.Add(data, "laboratorio", "Attività", estimatedMinutes: 30);
        syllabus.UpdateStatus(data, topic.Id, SyllabusTopicStatus.Completed);

        var overview = CreateService().CalculateOverview(data);

        Assert.Equal(100m, overview.AverageAvailableScore);
        Assert.Null(overview.CfuWeightedAvailableScore);
        Assert.Equal(1, overview.CoursesWithScore);
    }
}
