using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Domain.ValueObjects;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class CareerDomainTests
{
    private static readonly DateTimeOffset BaseDate =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private static ExamAttempt Passed(string examId, DateTimeOffset attemptedAt, int grade,
        DateTimeOffset recordedAt, bool honors = false)
    {
        var attempt = new ExamAttempt(examId, attemptedAt);
        attempt.Complete(
            ExamOutcome.Passed,
            GradeDecision.Accepted,
            GradeValue.Numeric(grade, honors),
            recordedAt);
        return attempt;
    }

    [Fact]
    public void OfficialAttemptUsesRecordingTimeInsteadOfInsertionOrder()
    {
        var exam = new CareerExam("course-1", "Analisi", 6);
        var newest = Passed(exam.Id, BaseDate.AddDays(20), 28, BaseDate.AddDays(25));
        var oldest = Passed(exam.Id, BaseDate, 20, BaseDate.AddDays(2));

        exam.AddAttempt(newest);
        exam.AddAttempt(oldest);

        Assert.Equal(newest.Id, exam.GetLatestAttempt()!.Id);
        Assert.Equal(newest.Id, exam.GetOfficialAttempt()!.Id);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });
        Assert.Equal(6, summary.AcquiredCfu);
        Assert.Equal(28m, summary.WeightedAverage);
    }

    [Fact]
    public void RejectedGradeDoesNotCountTowardAverageOrCfu()
    {
        var exam = new CareerExam("course-1", "Fisica", 9);
        var rejected = new ExamAttempt(exam.Id, BaseDate);
        rejected.Complete(
            ExamOutcome.Passed,
            GradeDecision.Rejected,
            GradeValue.Numeric(30),
            recordedAt: null);
        exam.AddAttempt(rejected);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });

        Assert.Null(summary.WeightedAverage);
        Assert.Null(summary.BaseDegreeGrade);
        Assert.Equal(0, summary.AcquiredCfu);
        Assert.Equal(0, summary.PassedExams);
        Assert.Equal(1, summary.RemainingExams);
    }

    [Fact]
    public void EligibilityAcquiresCfuButDoesNotEnterNumericAverage()
    {
        var exam = new CareerExam("course-1", "Idoneità informatica", 3);
        var attempt = new ExamAttempt(exam.Id, BaseDate);
        attempt.Complete(
            ExamOutcome.EligibilityPassed,
            GradeDecision.NotApplicable,
            GradeValue.Eligibility(),
            BaseDate.AddDays(1));
        exam.AddAttempt(attempt);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });

        Assert.Equal(3, summary.AcquiredCfu);
        Assert.Null(summary.WeightedAverage);
        Assert.Null(summary.BaseDegreeGrade);
        Assert.Equal(1, summary.PassedExams);
    }

    [Fact]
    public void RepeatedOfficialAttemptsAcquireCfuOnlyOnce()
    {
        var exam = new CareerExam("course-1", "Statistica", 6);
        var first = Passed(exam.Id, BaseDate, 22, BaseDate.AddDays(2));
        var second = Passed(exam.Id, BaseDate.AddDays(10), 27, BaseDate.AddDays(12));
        exam.AddAttempt(first);
        exam.AddAttempt(second);

        var summary = new CareerCalculatorService().Calculate(new[] { exam });

        Assert.Equal(6, summary.AcquiredCfu);
        Assert.Equal(27m, summary.WeightedAverage);
        Assert.Equal(1, summary.PassedExams);
    }

    [Fact]
    public void WhatIfCanReplaceAnExamAndAddAnotherWithoutMutatingActualCareer()
    {
        var first = new CareerExam("course-1", "Matematica", 6);
        first.AddAttempt(Passed(first.Id, BaseDate, 20, BaseDate.AddDays(1)));

        var second = new CareerExam("course-2", "Fisica", 12);
        second.AddAttempt(Passed(second.Id, BaseDate, 25, BaseDate.AddDays(1)));

        var service = new CareerCalculatorService();
        var result = service.Simulate(
            new[] { first, second },
            new[]
            {
                new WhatIfExam(first.Id, first.Name, 6, GradeValue.Numeric(30)),
                new WhatIfExam(null, "Chimica", 6, GradeValue.Numeric(28))
            });

        Assert.Equal(18, result.Current.AcquiredCfu);
        Assert.Equal(24, result.Simulated.AcquiredCfu);
        Assert.Equal(27m, result.Simulated.WeightedAverage);
        Assert.NotNull(result.AverageDelta);
        Assert.True(result.AverageDelta > 0m);
        Assert.Equal(20, first.GetOfficialAttempt()!.Grade!.NumericValue);
        Assert.Single(first.Attempts);
        Assert.Equal(2, new[] { first, second }.Length);
    }

    [Fact]
    public void EmptyCareerReturnsNullAverageAndBaseGrade()
    {
        var result = new CareerCalculatorService().Calculate(Array.Empty<CareerExam>());

        Assert.Null(result.WeightedAverage);
        Assert.Null(result.BaseDegreeGrade);
        Assert.Equal(0, result.AcquiredCfu);
        Assert.Equal(0, result.TotalCfu);
    }

    [Fact]
    public void AttemptCannotBeCompletedTwiceOrRecordedBeforeItWasTaken()
    {
        var attempt = new ExamAttempt("exam-1", BaseDate);
        Assert.Throws<ArgumentException>(() =>
            attempt.Complete(ExamOutcome.Passed, GradeDecision.Accepted, GradeValue.Numeric(24), BaseDate.AddDays(-1)));

        attempt.Complete(ExamOutcome.Passed, GradeDecision.Accepted, GradeValue.Numeric(24), BaseDate.AddDays(1));
        Assert.Throws<InvalidOperationException>(() =>
            attempt.Complete(ExamOutcome.Passed, GradeDecision.Accepted, GradeValue.Numeric(25), BaseDate.AddDays(2)));
    }

    [Fact]
    public void AggregateRejectsAttemptWithDifferentExamIdAndDuplicateAttemptId()
    {
        var exam = new CareerExam("course-1", "Analisi", 6);
        var attempt = new ExamAttempt(exam.Id, BaseDate, "attempt-1");
        exam.AddAttempt(attempt);

        Assert.Throws<ArgumentException>(() =>
            exam.AddAttempt(new ExamAttempt("another-exam", BaseDate)));
        Assert.Throws<InvalidOperationException>(() =>
            exam.AddAttempt(new ExamAttempt(exam.Id, BaseDate, "attempt-1")));
    }
}
