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
    public void SyllabusSupportsModuleChapterTopicHierarchyAndStableOrdering()
    {
        var data = Data();
        var service = new SyllabusTrackerService();
        var module = service.Add(data, "analisi", "Fondamenti", kind: SyllabusTopicKind.Module);
        var chapter = service.Add(data, "analisi", "Limiti", parentTopicId: module.Id, kind: SyllabusTopicKind.Chapter);
        var first = service.Add(data, "analisi", "Limiti notevoli", parentTopicId: chapter.Id, estimatedMinutes: 45);
        var second = service.Add(data, "analisi", "Continuità", parentTopicId: chapter.Id, estimatedMinutes: 60);

        Assert.Equal(new[] { module.Id, chapter.Id, first.Id, second.Id },
            service.GetTopics(data, "analisi").Select(x => x.Id));
        Assert.Equal(new[] { first.Id, second.Id }, service.GetChildren(data, "analisi", chapter.Id).Select(x => x.Id));
        Assert.Throws<ArgumentException>(() => service.Add(data, "analisi", "Capitolo senza modulo",
            kind: SyllabusTopicKind.Chapter));
        Assert.Throws<ArgumentException>(() => service.Add(data, "analisi", "Modulo annidato",
            parentTopicId: chapter.Id, kind: SyllabusTopicKind.Module));
    }

    [Fact]
    public void CompletionAndPreparednessAreIndependentAndCoverageUsesLeafTopics()
    {
        var data = Data();
        var service = new SyllabusTrackerService();
        var module = service.Add(data, "analisi", "Fondamenti", kind: SyllabusTopicKind.Module);
        var chapter = service.Add(data, "analisi", "Limiti", parentTopicId: module.Id, kind: SyllabusTopicKind.Chapter);
        var topic = service.Add(data, "analisi", "Limiti notevoli", parentTopicId: chapter.Id, estimatedMinutes: 60);

        service.UpdateCompletionPercent(data, topic.Id, 70);
        service.UpdatePreparedness(data, topic.Id, TopicPreparednessLevel.Low);
        var coverage = service.GetCoverage(data, "analisi");

        Assert.Equal(1, coverage.TotalTopics);
        Assert.Equal(70m, coverage.AverageCompletionPercent);
        Assert.Equal(1m, coverage.AveragePreparedness);
        Assert.Equal(1, coverage.RatedTopics);
        Assert.Equal(SyllabusTopicStatus.NotStarted, topic.Status);
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
    public void TimeBudgetReportsShortageAndMissingEstimates()
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
}
