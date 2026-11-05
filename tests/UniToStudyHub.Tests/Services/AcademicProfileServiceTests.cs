using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AcademicProfileServiceTests
{
    private static StudyData Data() => new()
    {
        GraduationTarget = new DateTime(2027, 10, 1),
        DegreeTotalCfu = 180,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "course-1", Name = "Analisi", Code = "MAT1", Cfu = 6 },
            new() { Id = "course-2", Name = "Fisica", Code = "FIS1", Cfu = 9 }
        }
    };

    [Fact]
    public void UpdateNormalizesAndValidatesProfile()
    {
        var data = Data();
        var service = new AcademicProfileService();

        service.Update(data, new AcademicProfileUpdate(
            "  Informatica  ", " L-31 ", 2024, 2, 180,
            new DateTime(2028, 7, 1), "  Studiare la mattina  ", 18, 180));

        Assert.Equal("Informatica", data.DegreeProgramName);
        Assert.Equal("L-31", data.DegreeProgramCode);
        Assert.Equal(2024, data.EnrollmentYear);
        Assert.Equal(2, data.CurrentAcademicYear);
        Assert.Equal(180, data.DegreeTotalCfu);
        Assert.Equal("Studiare la mattina", data.StudyPreferences);
        Assert.Equal(18, data.WeeklyStudyHours);
        Assert.Equal(180, data.MaxDailyStudyMinutes);
    }

    [Fact]
    public void UpdateRejectsInvalidCapacityAndCfu()
    {
        var service = new AcademicProfileService();
        var data = Data();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update(data, new AcademicProfileUpdate(
            "Informatica", "", 2024, 2, 180, DateTime.Today, "", 169, 180)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update(data, new AcademicProfileUpdate(
            "Informatica", "", 2024, 2, 10, DateTime.Today, "", 20, 240)));
    }

    [Theory]
    [InlineData(CourseStatus.NotStarted, false)]
    [InlineData(CourseStatus.InProgress, false)]
    [InlineData(CourseStatus.Prepared, false)]
    [InlineData(CourseStatus.ExamScheduled, false)]
    [InlineData(CourseStatus.Failed, false)]
    [InlineData(CourseStatus.Passed, true)]
    public void ArchiveAndRestorePreserveAcademicStatus(CourseStatus status, bool completed)
    {
        var data = Data();
        var service = new AcademicProfileService();
        service.SetStatus(data, "course-1", status);

        service.Archive(data, "course-1");
        Assert.True(data.Courses[0].Archived);
        Assert.Equal(completed, data.Courses[0].Completed);
        Assert.Equal(status, data.Courses[0].Status);

        service.Restore(data, "course-1");
        Assert.False(data.Courses[0].Archived);
        Assert.Equal(status, data.Courses[0].Status);
        Assert.Equal(completed, data.Courses[0].Completed);
    }

    [Fact]
    public void RejectedCourseDeletionDoesNotPartiallyRemoveGradesOrExams()
    {
        var data = Data();
        data.Grades.Add(new Grade { Id = "grade-1", CourseId = "course-1", CourseName = "Analisi", Cfu = 6, Value = 27 });
        data.Exams.Add(new Exam { Id = "exam-1", CourseId = "course-1", CourseName = "Analisi", Name = "Scritto" });
        data.SyllabusTopics.Add(new SyllabusTopicData { Id = "topic-1", CourseId = "course-1", Title = "Limiti" });
        var service = new AcademicProfileService();

        Assert.Throws<InvalidOperationException>(() => service.RemoveCourse(data, "course-1"));

        Assert.Contains(data.Courses, x => x.Id == "course-1");
        Assert.Contains(data.Grades, x => x.Id == "grade-1");
        Assert.Contains(data.Exams, x => x.Id == "exam-1");
        Assert.Contains(data.SyllabusTopics, x => x.Id == "topic-1");
    }

    [Fact]
    public void CourseWithCareerHistoryRequiresExplicitDeletionConfirmation()
    {
        var data = Data();
        data.CareerExams.Add(new CareerExamData
        {
            Id = "career-1",
            CourseId = "course-1",
            Name = "Analisi",
            Cfu = 6
        });
        var service = new AcademicProfileService();

        Assert.Throws<InvalidOperationException>(() => service.RemoveCourse(data, "course-1"));

        service.RemoveCourse(data, "course-1", removeCareerHistory: true);
        Assert.DoesNotContain(data.Courses, x => x.Id == "course-1");
        Assert.DoesNotContain(data.CareerExams, x => x.CourseId == "course-1");
    }

    [Fact]
    public void CourseCodeMustRemainUnique()
    {
        var data = Data();
        var service = new AcademicProfileService();

        Assert.Throws<ArgumentException>(() => service.AddCourse(data, new Course
        {
            Name = "Altro",
            Code = "MAT1",
            Cfu = 6
        }));
    }
}
