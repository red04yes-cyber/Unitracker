using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class CareerApplicationServiceTests
{
    private static readonly DateTimeOffset AttemptDate = new(2026, 2, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset RecordedDate = AttemptDate.AddDays(1);

    private static StudyData Data() => new()
    {
        DegreeTotalCfu = 180,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 },
            new() { Id = "fisica", Name = "Fisica", Cfu = 9 }
        }
    };

    [Fact]
    public void ProfileTotalIsUsedForCareerCfuDenominator()
    {
        var data = Data();
        var service = new CareerApplicationService(new CareerCalculatorService());
        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            numericGrade: 28, honors: false, isPassFail: false, AttemptDate, RecordedDate);

        var summary = service.GetSummary(data);

        Assert.Equal(6, summary.AcquiredCfu);
        Assert.Equal(180, summary.TotalCfu);
        Assert.Equal(174, summary.RemainingCfu);
        Assert.Equal(28m, summary.WeightedAverage);
    }

    [Fact]
    public void RejectedGradeIsPreservedButDoesNotCount()
    {
        var data = Data();
        var service = new CareerApplicationService(new CareerCalculatorService());

        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Rejected,
            numericGrade: 30, honors: false, isPassFail: false, AttemptDate, recordedAt: null);
        var rejectedSummary = service.GetSummary(data);
        Assert.Equal(0, rejectedSummary.AcquiredCfu);
        Assert.Null(rejectedSummary.WeightedAverage);
        Assert.Single(service.GetAttempts(data, "analisi"));

        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            numericGrade: 24, honors: false, isPassFail: false, AttemptDate.AddDays(10), RecordedDate.AddDays(10));

        Assert.Equal(2, service.GetAttempts(data, "analisi").Count);
        Assert.Equal(6, service.GetSummary(data).AcquiredCfu);
        Assert.Equal(24m, service.GetSummary(data).WeightedAverage);
    }

    [Fact]
    public void ArithmeticAverageUsesLatestOfficialNumericResultPerExam()
    {
        var data = Data();
        var service = new CareerApplicationService(new CareerCalculatorService());
        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            20, false, false, AttemptDate, RecordedDate);
        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            27, false, false, AttemptDate.AddDays(10), RecordedDate.AddDays(10));
        service.RecordResult(data, "fisica", ExamOutcome.Passed, GradeDecision.Accepted,
            30, true, false, AttemptDate, RecordedDate);

        Assert.Equal(28.5m, service.GetArithmeticAverage(data));
    }

    [Fact]
    public void WhatIfDoesNotMutateTheRealCareer()
    {
        var data = Data();
        var service = new CareerApplicationService(new CareerCalculatorService());
        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            24, false, false, AttemptDate, RecordedDate);

        var result = service.Simulate(data, new[]
        {
            new WhatIfCourseInput("analisi", "Analisi", 6, 30),
            new WhatIfCourseInput("fisica", "Fisica", 9, 27)
        });

        Assert.Equal(24m, service.GetSummary(data).WeightedAverage);
        Assert.True(result.Simulated.WeightedAverage > result.Current.WeightedAverage);
        Assert.Equal(24, data.CareerExams.Single().Attempts.Single().NumericGrade);
        Assert.Single(data.CareerExams);
    }

    [Fact]
    public void CfuChangeAfterHistoryIsRejected()
    {
        var data = Data();
        var service = new CareerApplicationService(new CareerCalculatorService());
        service.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            24, false, false, AttemptDate, RecordedDate);

        data.Courses[0].Cfu = 9;
        Assert.Throws<InvalidOperationException>(() => service.EnsureCareerExam(data, data.Courses[0]));
    }

    [Fact]
    public void ReadinessMarksMissingDataAndComputesTransparentPartialScore()
    {
        var data = Data();
        var career = new CareerApplicationService(new CareerCalculatorService());
        var syllabus = new SyllabusTrackerService();
        var topic = syllabus.Add(data, "analisi", "Limiti", estimatedMinutes: 120);
        syllabus.UpdateStatus(data, topic.Id, SyllabusTopicStatus.Completed);
        var readiness = new ExamReadinessService(career);

        var partial = readiness.Calculate(data, "analisi");
        Assert.False(partial.IsComplete);
        Assert.Equal(100m, partial.Score);
        Assert.Contains(partial.MissingData, x => x.Contains("Risultati", StringComparison.Ordinal));

        career.RecordResult(data, "analisi", ExamOutcome.Passed, GradeDecision.Accepted,
            28, false, false, AttemptDate, RecordedDate);
        var complete = readiness.Calculate(data, "analisi");

        Assert.True(complete.IsComplete);
        Assert.Equal(93.33m, complete.Score);
        Assert.Equal(60m, complete.Components.Single(x => x.Key == "syllabus").Weight * 100m);
    }
}
