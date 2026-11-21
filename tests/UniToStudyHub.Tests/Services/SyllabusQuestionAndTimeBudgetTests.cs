using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class SyllabusQuestionAndTimeBudgetTests
{
    private static StudyData Data() => new()
    {
        DegreeTotalCfu = 180,
        WeeklyStudyHours = 10,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6, EstimatedStudyHours = 20 }
        }
    };

    [Fact]
    public void SyllabusCoverageUsesAnExplicitTopicDenominator()
    {
        var data = Data();
        var service = new SyllabusTrackerService();
        var first = service.Add(data, "analisi", "Limiti", estimatedMinutes: 60);
        service.Add(data, "analisi", "Derivate", estimatedMinutes: 90);
        service.UpdateStatus(data, first.Id, SyllabusTopicStatus.Completed, new DateTime(2026, 3, 1));

        var coverage = service.GetCoverage(data, "analisi");

        Assert.Equal(2, coverage.TotalTopics);
        Assert.Equal(1, coverage.CompletedTopics);
        Assert.Equal(50m, coverage.CompletedPercent);
        Assert.Equal(90, coverage.EstimatedRemainingMinutes);
        Assert.NotNull(first.LastStudiedAt);
    }

    [Fact]
    public void ClosedQuestionEvaluatesDeterministicallyAndOpenQuestionNeedsReview()
    {
        var data = Data();
        var service = new QuestionBankService();
        var closed = new QuestionBankItemData
        {
            Id = "q1",
            CourseId = "analisi",
            Text = "Qual è la derivata di x²?",
            Type = QuestionType.Closed,
            Options = new ObservableCollection<string> { "2x", "x", "x²" },
            CorrectAnswer = "2x",
            Source = "Manuale"
        };
        service.Add(data, closed);

        Assert.Equal(true, service.Evaluate(closed, " 2X ").IsCorrect);
        Assert.Equal(false, service.Evaluate(closed, "x").IsCorrect);
        Assert.Null(service.Evaluate(new QuestionBankItemData
        {
            CourseId = "analisi",
            Text = "Spiega il teorema",
            Type = QuestionType.Open
        }, "testo").IsCorrect);

        var open = service.Evaluate(new QuestionBankItemData
        {
            CourseId = "analisi",
            Text = "Spiega il teorema",
            Type = QuestionType.Open
        }, "testo");
        Assert.True(open.RequiresManualReview);
        Assert.Null(open.IsCorrect);
    }

    [Fact]
    public void ClosedQuestionWithoutASelectedCorrectOptionIsRejected()
    {
        var data = Data();
        var service = new QuestionBankService();
        var question = new QuestionBankItemData
        {
            CourseId = "analisi",
            Text = "Domanda",
            Type = QuestionType.Closed,
            Options = new ObservableCollection<string> { "A", "B" },
            CorrectAnswer = "C"
        };

        Assert.Throws<ArgumentException>(() => service.Add(data, question));
    }

    [Fact]
    public void TimeBudgetAllocatesWhenCapacityIsSufficient()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "e1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            Cfu = 6,
            EstimatedStudyHours = 20,
            PlannedDate = new DateTime(2026, 1, 15)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(600, plan.WeeklyMinutes);
        Assert.Equal(1200, plan.AvailableMinutes);
        Assert.Equal(1200, plan.RequiredMinutes);
        Assert.Equal(1200, plan.Allocations.Single().AllocatedMinutes);
        Assert.False(plan.IsOverCapacity);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void TimeBudgetReportsShortageAndMissingExams()
    {
        var data = Data();
        data.WeeklyStudyHours = 5;
        data.Exams.Add(new Exam
        {
            Id = "e1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            Cfu = 6,
            EstimatedStudyHours = 20,
            PlannedDate = new DateTime(2026, 1, 15)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.True(plan.IsOverCapacity);
        Assert.Equal(600, plan.Allocations.Single().AllocatedMinutes);
        Assert.True(plan.Allocations.Single().IsAtRisk);
        Assert.Contains(plan.Warnings, x => x.Contains("supera", StringComparison.OrdinalIgnoreCase));

        var empty = new StudyData();
        var emptyPlan = new TimeBudgetService().Calculate(empty, new DateTime(2026, 1, 1));
        Assert.Empty(emptyPlan.Allocations);
        Assert.NotEmpty(emptyPlan.Warnings);
    }

    [Fact]
    public void TimeBudgetPrioritizesEarlierDeadlinesBeforeLaterExams()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "early",
            CourseId = "analisi",
            Name = "Esame vicino",
            EstimatedStudyHours = 15,
            PlannedDate = new DateTime(2026, 1, 8)
        });
        data.Exams.Add(new Exam
        {
            Id = "late",
            CourseId = "fisica",
            Name = "Esame lontano",
            EstimatedStudyHours = 90,
            PlannedDate = new DateTime(2026, 3, 12)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        var early = plan.Allocations.Single(x => x.CourseName == "Esame vicino");
        var late = plan.Allocations.Single(x => x.CourseName == "Esame lontano");
        Assert.Equal(6000, plan.AvailableMinutes);
        Assert.Equal(6300, plan.RequiredMinutes);
        Assert.Equal(600, early.AllocatedMinutes);
        Assert.True(early.IsAtRisk);
        Assert.Equal(5400, late.AllocatedMinutes);
        Assert.False(late.IsAtRisk);
        Assert.True(plan.IsOverCapacity);
    }

    [Fact]
    public void TimeBudgetProratesCapacityForPartialWeeks()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "soon",
            CourseId = "analisi",
            Name = "Esame tra due giorni",
            EstimatedStudyHours = 5,
            PlannedDate = new DateTime(2026, 1, 3)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(171, plan.AvailableMinutes);
        Assert.Equal(171, plan.Allocations.Single().AllocatedMinutes);
        Assert.True(plan.Allocations.Single().IsAtRisk);
        Assert.Equal(1, plan.Allocations.Single().WeeksAvailable);
    }

    [Fact]
    public void TimeBudgetHonorsTheDailyLimitWhenItIsMoreRestrictive()
    {
        var data = Data();
        data.WeeklyStudyHours = 168;
        data.MaxDailyStudyMinutes = 120;
        data.Exams.Add(new Exam
        {
            Id = "daily-limit",
            CourseId = "analisi",
            Name = "Esame",
            EstimatedStudyHours = 30,
            PlannedDate = new DateTime(2026, 1, 8)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(10080, plan.WeeklyMinutes);
        Assert.Equal(840, plan.AvailableMinutes);
        Assert.Equal(840, plan.Allocations.Single().AllocatedMinutes);
        Assert.True(plan.Allocations.Single().IsAtRisk);
    }

    [Fact]
    public void TimeBudgetSplitsSameDeadlineShortageProportionally()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-a",
            CourseId = "analisi",
            Name = "Primo esame",
            EstimatedStudyHours = 10,
            PlannedDate = new DateTime(2026, 1, 8)
        });
        data.Exams.Add(new Exam
        {
            Id = "exam-b",
            CourseId = "fisica",
            Name = "Secondo esame",
            EstimatedStudyHours = 10,
            PlannedDate = new DateTime(2026, 1, 8)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(300, plan.Allocations.Single(x => x.CourseName == "Primo esame").AllocatedMinutes);
        Assert.Equal(300, plan.Allocations.Single(x => x.CourseName == "Secondo esame").AllocatedMinutes);
        Assert.Equal(plan.AvailableMinutes, plan.Allocations.Sum(x => x.AllocatedMinutes));
    }

    [Fact]
    public void TimeBudgetFlagsAnUnachievableEarlyDeadlineEvenWhenTotalCapacityIsEnough()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "early",
            CourseId = "analisi",
            Name = "Esame vicino",
            EstimatedStudyHours = 15,
            PlannedDate = new DateTime(2026, 1, 8)
        });
        data.Exams.Add(new Exam
        {
            Id = "late",
            CourseId = "fisica",
            Name = "Esame successivo",
            EstimatedStudyHours = 1,
            PlannedDate = new DateTime(2026, 1, 15)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(1200, plan.AvailableMinutes);
        Assert.Equal(960, plan.RequiredMinutes);
        Assert.True(plan.Allocations.Single(x => x.CourseName == "Esame vicino").IsAtRisk);
        Assert.False(plan.Allocations.Single(x => x.CourseName == "Esame successivo").IsAtRisk);
        Assert.True(plan.IsOverCapacity);
        Assert.Contains(plan.Warnings, x => x.Contains("scadenze", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TimeBudgetClampsInvalidCapacityValuesAndReportsThem()
    {
        var data = Data();
        data.WeeklyStudyHours = int.MaxValue;
        data.MaxDailyStudyMinutes = int.MaxValue;
        data.Exams.Add(new Exam
        {
            Id = "bounded",
            CourseId = "analisi",
            Name = "Esame",
            EstimatedStudyHours = 20,
            PlannedDate = new DateTime(2026, 1, 8)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        Assert.Equal(10080, plan.WeeklyMinutes);
        Assert.Equal(10080, plan.AvailableMinutes);
        Assert.Contains(plan.Warnings, x => x.Contains("0–168", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, x => x.Contains("0–1440", StringComparison.Ordinal));
    }

    [Fact]
    public void TimeBudgetDoesNotPresentMissingEstimatesAsCovered()
    {
        var data = Data();
        data.Courses[0].EstimatedStudyHours = 0;
        data.Exams.Add(new Exam
        {
            Id = "missing-estimate",
            CourseId = "analisi",
            Name = "Esame senza stima",
            PlannedDate = new DateTime(2026, 1, 8)
        });

        var plan = new TimeBudgetService().Calculate(data, new DateTime(2026, 1, 1));

        var allocation = Assert.Single(plan.Allocations);
        Assert.False(allocation.HasEstimate);
        Assert.Contains("non disponibile", allocation.Explanation, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(plan.Warnings, x => x.Contains("Nessuna stima", StringComparison.Ordinal));
    }
}
