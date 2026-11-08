using UniToStudyHub.Domain.Entities;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;
using Xunit;

namespace UniToStudyHub.Tests.Domain;

public sealed class CareerExamTests
{
    [Fact]
    public void OfficialAttempt_IgnoresLaterRejectedGrade_AndPreservesAttemptHistory()
    {
        var exam = new CareerExam("course-1", "Analisi", 6);
        var recordedAt = new DateTimeOffset(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);
        var accepted = new ExamAttempt(exam.Id, recordedAt.AddDays(-2), "attempt-1");
        accepted.Complete(ExamOutcome.Passed, GradeDecision.Accepted, GradeValue.Numeric(24), recordedAt);
        var rejected = new ExamAttempt(exam.Id, recordedAt.AddDays(2), "attempt-2");
        rejected.Complete(ExamOutcome.Passed, GradeDecision.Rejected, GradeValue.Numeric(30), null);

        exam.AddAttempt(accepted);
        exam.AddAttempt(rejected);

        Assert.Equal(2, exam.Attempts.Count);
        Assert.Equal("attempt-2", exam.GetLatestAttempt()!.Id);
        Assert.Equal("attempt-1", exam.GetOfficialAttempt()!.Id);
        Assert.Equal(24, exam.GetOfficialAttempt()!.Grade!.NumericValue);
        Assert.True(exam.AreCfuAcquired());
    }

    [Fact]
    public void AddAttempt_RejectsAttemptOwnedByAnotherExam()
    {
        var first = new CareerExam("course-1", "Analisi", 6);
        var second = new CareerExam("course-2", "Fisica", 9);
        var attempt = new ExamAttempt(second.Id, DateTimeOffset.UtcNow);

        Assert.Throws<ArgumentException>(() => first.AddAttempt(attempt));
    }

    [Fact]
    public void EligibilityAttempt_AcquiresCfuWithoutEnteringAverage()
    {
        var exam = new CareerExam("course-1", "Lingua", 3, countsTowardAverage: true);
        var attempt = new ExamAttempt(exam.Id, DateTimeOffset.UtcNow);
        attempt.Complete(ExamOutcome.EligibilityPassed, GradeDecision.NotApplicable, GradeValue.Eligibility(), DateTimeOffset.UtcNow);
        exam.AddAttempt(attempt);

        Assert.True(exam.AreCfuAcquired());
        Assert.True(exam.GetOfficialAttempt()!.Grade!.IsPassFail);
    }

    [Fact]
    public void GradeValue_RejectsInvalidHonors()
    {
        Assert.Throws<ArgumentException>(() => GradeValue.Numeric(29, honors: true));
        Assert.Throws<ArgumentOutOfRangeException>(() => GradeValue.Numeric(17));
    }
}