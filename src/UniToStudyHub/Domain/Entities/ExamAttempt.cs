using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Entities;

public sealed class ExamAttempt
{
    public string Id { get; }
    public string ExamId { get; }
    public DateTimeOffset AttemptedAt { get; }
    public AttemptStatus Status { get; private set; }
    public ExamOutcome Outcome { get; private set; }
    public GradeDecision GradeDecision { get; private set; }
    public GradeValue? Grade { get; private set; }
    public DateTimeOffset? RecordedAt { get; private set; }

    public ExamAttempt(string examId, DateTimeOffset attemptedAt, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(examId)) throw new ArgumentException("Exam id is required.", nameof(examId));
        ExamId = examId;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        AttemptedAt = attemptedAt;
        Status = AttemptStatus.Scheduled;
        Outcome = ExamOutcome.Pending;
        GradeDecision = GradeDecision.Pending;
    }

    public void Book() { EnsureNotCompleted(); Status = AttemptStatus.Booked; }
    public void Start() { EnsureNotCompleted(); Status = AttemptStatus.InProgress; }

    public void Complete(ExamOutcome outcome, GradeDecision decision, GradeValue? grade, DateTimeOffset? recordedAt = null)
    {
        EnsureNotCompleted();
        if (outcome == ExamOutcome.Pending) throw new ArgumentException("A completed attempt needs a final outcome.", nameof(outcome));
        if (outcome == ExamOutcome.Passed && (grade is null || grade.IsPassFail)) throw new ArgumentException("A passed graded exam requires a numeric grade.", nameof(grade));
        if (outcome == ExamOutcome.EligibilityPassed && (grade is null || !grade.IsPassFail)) throw new ArgumentException("An eligibility exam requires an eligibility result.", nameof(grade));
        if (outcome == ExamOutcome.Failed && grade is not null) throw new ArgumentException("A failed attempt cannot carry an accepted grade.", nameof(grade));
        if (outcome == ExamOutcome.Passed && decision is not (GradeDecision.Accepted or GradeDecision.Rejected)) throw new ArgumentException("A passed numeric grade must be accepted or rejected.", nameof(decision));
        if (outcome == ExamOutcome.EligibilityPassed && decision != GradeDecision.NotApplicable) throw new ArgumentException("Eligibility results do not have an accept/reject grade decision.", nameof(decision));
        if (outcome == ExamOutcome.Failed && decision != GradeDecision.NotApplicable) throw new ArgumentException("A failed attempt has no grade decision.", nameof(decision));
        if (decision == GradeDecision.Accepted && recordedAt is null) throw new ArgumentException("An accepted grade must have a recording timestamp.", nameof(recordedAt));
        if (decision == GradeDecision.Rejected && recordedAt is not null) throw new ArgumentException("A rejected grade cannot be recorded as official.", nameof(recordedAt));

        Status = AttemptStatus.Completed;
        Outcome = outcome;
        GradeDecision = decision;
        Grade = grade;
        RecordedAt = recordedAt;
    }

    public void Withdraw()
    {
        if (Status == AttemptStatus.Completed) throw new InvalidOperationException("A completed attempt cannot be withdrawn.");
        Status = AttemptStatus.Withdrawn;
        Outcome = ExamOutcome.Pending;
        GradeDecision = GradeDecision.Pending;
    }

    private void EnsureNotCompleted()
    {
        if (Status == AttemptStatus.Completed || Status == AttemptStatus.Withdrawn)
            throw new InvalidOperationException("This attempt is already closed.");
    }
}