using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ExamPlannerTests
{
    private static readonly DateTime ReferenceDate = new(2026, 11, 15);

    private static Exam MakeExam(
        string id,
        int cfu = 6,
        int hours = 10,
        bool completed = false) => new()
    {
        Id = id,
        CourseId = id,
        CourseName = id,
        Name = id,
        Cfu = cfu,
        EstimatedStudyHours = hours,
        Completed = completed,
        PlannedSession = "Piano precedente",
        PlannedDate = new DateTime(2026, 12, 1)
    };

    private static StudyData Data(params Exam[] exams) => new()
    {
        Exams = new ObservableCollection<Exam>(exams),
        Courses = new ObservableCollection<Course>(),
        CoursePrerequisites = new ObservableCollection<CoursePrerequisiteData>()
    };

    [Fact]
    public void PlanNeverExceedsCfuCapacityAndReturnsSessionLoads()
    {
        var exams = new[] { MakeExam("a", 12), MakeExam("b", 12), MakeExam("c", 12) };

        var result = ExamPlanner.CreatePlan(exams, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 6, 30),
            MaxCfuPerSession = 18,
            MaxWeeklyStudyHours = 168
        }, ReferenceDate);

        Assert.Equal(2, result.Assignments.Count);
        Assert.Single(result.UnscheduledExams);
        Assert.All(result.SessionLoads, load => Assert.InRange(load.AssignedCfu, 0, load.MaxCfu));
        Assert.Equal(24, result.ScheduledCfu);
        Assert.False(result.IsFeasible);
    }

    [Fact]
    public void PlanMovesHeavyWorkToALaterSessionWhenWeeklyBudgetRequiresIt()
    {
        var result = ExamPlanner.CreatePlan(new[] { MakeExam("large", 6, hours: 100) },
            new ExamPlanningOptions
            {
                TargetDate = new DateTime(2027, 6, 30),
                MaxCfuPerSession = 24,
                MaxWeeklyStudyHours = 5
            }, ReferenceDate);

        var assignment = Assert.Single(result.Assignments);
        Assert.Equal(new DateTime(2027, 6, 20), assignment.SessionDate);
        Assert.All(result.SessionLoads, load =>
            Assert.True(load.AverageWeeklyStudyHours <= load.MaxWeeklyStudyHours));
    }

    [Fact]
    public void PastTargetIsReportedInsteadOfBeingSilentlyMovedForward()
    {
        var result = ExamPlanner.CreatePlan(new[] { MakeExam("a") },
            new ExamPlanningOptions { TargetDate = new DateTime(2026, 11, 14) }, ReferenceDate);

        Assert.Empty(result.Assignments);
        Assert.Single(result.UnscheduledExams);
        Assert.Equal(new DateTime(2026, 11, 14), result.TargetDate);
        Assert.Contains(result.Warnings, warning => warning.Contains("già trascorso", StringComparison.Ordinal));
    }

    [Fact]
    public void ExcludedPeriodsRemoveMatchingSessionsFromThePlan()
    {
        var result = ExamPlanner.CreatePlan(new[] { MakeExam("a") },
            new ExamPlanningOptions
            {
                TargetDate = new DateTime(2027, 6, 30),
                ExcludedPeriods = new[]
                {
                    new ExamPlanningBlackout(new DateOnly(2027, 1, 20), new DateOnly(2027, 1, 20), "sessione invernale")
                }
            }, ReferenceDate);

        Assert.Equal(new DateTime(2027, 6, 20), Assert.Single(result.Assignments).SessionDate);
        Assert.DoesNotContain(result.SessionLoads, load => load.SessionDate == new DateTime(2027, 1, 20));
    }

    [Fact]
    public void MandatoryPrerequisiteIsScheduledInAnEarlierSession()
    {
        var data = Data(MakeExam("prerequisite"), MakeExam("dependent"));
        data.Courses.Add(new Course { Id = "prerequisite", Name = "Propedeutico", Cfu = 6 });
        data.Courses.Add(new Course { Id = "dependent", Name = "Dipendente", Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "dependent",
            PrerequisiteCourseId = "prerequisite",
            IsMandatory = true
        });

        var result = ExamPlanner.CreatePlan(data, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 6, 30),
            MaxCfuPerSession = 24,
            MaxWeeklyStudyHours = 168
        }, ReferenceDate);

        var prerequisite = Assert.Single(result.Assignments, x => x.CourseId == "prerequisite");
        var dependent = Assert.Single(result.Assignments, x => x.CourseId == "dependent");
        Assert.True(prerequisite.SessionDate < dependent.SessionDate);
    }

    [Fact]
    public void CompletedPrerequisiteDoesNotDelayTheDependentExam()
    {
        var data = Data(MakeExam("dependent"));
        data.Courses.Add(new Course
        {
            Id = "prerequisite",
            Name = "Propedeutico",
            Cfu = 6,
            Completed = true,
            Status = CourseStatus.Passed
        });
        data.Courses.Add(new Course { Id = "dependent", Name = "Dipendente", Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "dependent",
            PrerequisiteCourseId = "prerequisite",
            IsMandatory = true
        });

        var result = ExamPlanner.CreatePlan(data, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 1, 31),
            MaxWeeklyStudyHours = 168
        }, ReferenceDate);

        Assert.Equal(new DateTime(2027, 1, 20), Assert.Single(result.Assignments).SessionDate);
    }

    [Fact]
    public void DependentExamWorkloadUsesOnlyTheTimeAfterItsPrerequisiteSession()
    {
        var data = Data(
            MakeExam("prerequisite", cfu: 6, hours: 10),
            MakeExam("dependent", cfu: 6, hours: 300));
        data.Courses.Add(new Course { Id = "prerequisite", Name = "Propedeutico", Cfu = 6 });
        data.Courses.Add(new Course { Id = "dependent", Name = "Dipendente", Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "dependent",
            PrerequisiteCourseId = "prerequisite"
        });

        var result = ExamPlanner.CreatePlan(data, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 6, 30),
            MaxWeeklyStudyHours = 10
        }, ReferenceDate);

        Assert.Single(result.Assignments);
        Assert.Equal("prerequisite", result.Assignments[0].CourseId);
        Assert.Contains("ore settimanali", Assert.Single(result.UnscheduledExams).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingUncompletedPrerequisiteProducesAnExplicitReason()
    {
        var data = Data(MakeExam("dependent"));
        data.Courses.Add(new Course { Id = "dependent", Name = "Dipendente", Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "dependent",
            PrerequisiteCourseId = "missing",
            IsMandatory = true
        });

        var result = ExamPlanner.CreatePlan(data, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 6, 30)
        }, ReferenceDate);

        Assert.Empty(result.Assignments);
        Assert.Contains("missing", Assert.Single(result.UnscheduledExams).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void PrerequisiteCyclesAreRejected()
    {
        var data = Data(MakeExam("a"), MakeExam("b"));
        data.CoursePrerequisites.Add(new CoursePrerequisiteData { CourseId = "a", PrerequisiteCourseId = "b" });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData { CourseId = "b", PrerequisiteCourseId = "a" });

        Assert.Throws<ArgumentException>(() => ExamPlanner.CreatePlan(data, referenceDate: ReferenceDate));
    }

    [Fact]
    public void CreatePlanDoesNotMutateExistingExamAssignments()
    {
        var exam = MakeExam("a");
        var originalSession = exam.PlannedSession;
        var originalDate = exam.PlannedDate;

        _ = ExamPlanner.CreatePlan(new[] { exam }, referenceDate: ReferenceDate);

        Assert.Equal(originalSession, exam.PlannedSession);
        Assert.Equal(originalDate, exam.PlannedDate);
    }

    [Fact]
    public void ApplyPlanPreservesCompletedExamsAndClearsUnscheduledDrafts()
    {
        var completed = MakeExam("completed", completed: true);
        var tooLarge = MakeExam("too-large", cfu: 30);
        var exams = new List<Exam> { completed, tooLarge };
        var result = ExamPlanner.CreatePlan(exams, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 1, 31),
            MaxCfuPerSession = 24
        }, ReferenceDate);

        ExamPlanner.ApplyPlan(exams, result);

        Assert.Equal("Piano precedente", completed.PlannedSession);
        Assert.Equal(new DateTime(2026, 12, 1), completed.PlannedDate);
        Assert.Equal("Da pianificare", tooLarge.PlannedSession);
        Assert.Null(tooLarge.PlannedDate);
    }

    [Fact]
    public void AlternativesExposeConservativeBalancedAndIntensiveCapacity()
    {
        var data = Data(MakeExam("large", cfu: 20, hours: 30));

        var alternatives = ExamPlanner.CreateAlternatives(data, ReferenceDate, new DateTime(2027, 6, 30));

        Assert.Empty(alternatives.Conservative.Assignments);
        Assert.Single(alternatives.Balanced.Assignments);
        Assert.Single(alternatives.Intensive.Assignments);
        Assert.All(alternatives.Conservative.SessionLoads, load => Assert.Equal(18, load.MaxCfu));
        Assert.All(alternatives.Balanced.SessionLoads, load => Assert.Equal(24, load.MaxCfu));
        Assert.All(alternatives.Intensive.SessionLoads, load => Assert.Equal(30, load.MaxCfu));
    }

    [Fact]
    public void InvalidBudgetsAndBlackoutRangesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ExamPlanner.CreatePlan(
            new[] { MakeExam("a") },
            new ExamPlanningOptions { MaxCfuPerSession = 0 },
            ReferenceDate));

        Assert.Throws<ArgumentOutOfRangeException>(() => ExamPlanner.CreatePlan(
            new[] { MakeExam("a") },
            new ExamPlanningOptions { MaxWeeklyStudyHours = 169 },
            ReferenceDate));

        Assert.Throws<ArgumentException>(() => ExamPlanner.CreatePlan(
            new[] { MakeExam("a") },
            new ExamPlanningOptions
            {
                ExcludedPeriods = new[]
                {
                    new ExamPlanningBlackout(new DateOnly(2027, 2, 1), new DateOnly(2027, 1, 1))
                }
            },
            ReferenceDate));
    }

    [Fact]
    public void ExcessiveExamCollectionsAreRejectedBeforePlanning()
    {
        var exams = Enumerable.Range(0, 501).Select(index => MakeExam($"exam-{index:D3}")).ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            ExamPlanner.CreatePlan(exams, referenceDate: ReferenceDate));
    }

    [Fact]
    public void InfiniteExamEnumerableIsStoppedAtTheFirstItemOverTheLimit()
    {
        var enumerated = 0;

        IEnumerable<Exam> Endless()
        {
            while (true)
            {
                enumerated++;
                yield return MakeExam($"stream-{enumerated:D4}");
            }
        }

        Assert.Throws<InvalidOperationException>(() =>
            ExamPlanner.CreatePlan(Endless(), referenceDate: ReferenceDate));
        Assert.Equal(501, enumerated);
    }

    [Fact]
    public void StudyDataRejectsOversizedCourseCollectionsBeforeBuildingPrerequisiteIndexes()
    {
        var data = Data(MakeExam("a"));
        for (var index = 0; index < 2_001; index++)
            data.Courses.Add(new Course { Id = $"course-{index:D4}", Name = "Corso", Cfu = 6 });

        Assert.Throws<InvalidOperationException>(() =>
            ExamPlanner.CreatePlan(data, referenceDate: ReferenceDate));
    }

    [Fact]
    public void DuplicateCourseAssignmentsAreRejectedRatherThanDoubleCounted()
    {
        var first = MakeExam("first");
        var second = MakeExam("second");
        second.CourseId = first.CourseId;

        Assert.Throws<InvalidDataException>(() =>
            ExamPlanner.CreatePlan(new[] { first, second }, referenceDate: ReferenceDate));
    }

    [Fact]
    public void InvalidExamValuesAreReportedWithoutCreatingAnAssignment()
    {
        var exam = MakeExam("invalid", cfu: 0, hours: -1);

        var result = ExamPlanner.CreatePlan(new[] { exam }, referenceDate: ReferenceDate);

        Assert.Empty(result.Assignments);
        Assert.Contains("CFU", Assert.Single(result.UnscheduledExams).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyWpfEntryPointAppliesTheHardCapacityAndReturnsItsResult()
    {
        var exams = new List<Exam> { MakeExam("first", 6), MakeExam("second", 6) };

        var result = ExamPlanner.AssignIndicativeSessions(
            exams, ReferenceDate, maxCfuPerSession: 6, targetDate: new DateTime(2027, 1, 31));

        Assert.Single(result.Assignments);
        Assert.Equal(6, result.ScheduledCfu);
        Assert.Equal(1, exams.Count(x => x.PlannedDate is not null));
        Assert.Equal("Da pianificare", exams.Single(x => x.PlannedDate is null).PlannedSession);
    }

    [Fact]
    public void StudyDataAssignmentOverloadAppliesPrerequisiteAwareDates()
    {
        var data = Data(MakeExam("prerequisite"), MakeExam("dependent"));
        data.Courses.Add(new Course { Id = "prerequisite", Name = "Propedeutico", Cfu = 6 });
        data.Courses.Add(new Course { Id = "dependent", Name = "Dipendente", Cfu = 6 });
        data.CoursePrerequisites.Add(new CoursePrerequisiteData
        {
            CourseId = "dependent",
            PrerequisiteCourseId = "prerequisite"
        });

        var result = ExamPlanner.AssignIndicativeSessions(data, new ExamPlanningOptions
        {
            TargetDate = new DateTime(2027, 6, 30),
            MaxWeeklyStudyHours = 168
        }, ReferenceDate);

        Assert.Equal(2, result.Assignments.Count);
        Assert.True(data.Exams.Single(x => x.CourseId == "prerequisite").PlannedDate <
                    data.Exams.Single(x => x.CourseId == "dependent").PlannedDate);
    }

    [Fact]
    public void ApplyPlanRejectsAssignmentsForUnknownExams()
    {
        var result = new ExamPlanningResult(
            ReferenceDate,
            new DateTime(2027, 1, 31),
            new[]
            {
                new ExamSessionAssignment("unknown", "unknown", "Unknown", "Invernale 2027",
                    new DateTime(2027, 1, 20), 6, 10)
            },
            Array.Empty<ExamSessionLoad>(),
            Array.Empty<ExamPlanningIssue>(),
            Array.Empty<string>());

        Assert.Throws<InvalidOperationException>(() => ExamPlanner.ApplyPlan(new[] { MakeExam("known") }, result));
    }
}
