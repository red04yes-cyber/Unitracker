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
        if (string.IsNullOrWhiteSpace(examId))
            throw new ArgumentException("Exam id is required.", nameof(examId));
        if (string.IsNullOrWhiteSpace(id) && id is not null)
            throw new ArgumentException("Attempt id cannot be whitespace.", nameof(id));

        ExamId = examId;
        Id = id ?? Guid.NewGuid().ToString("N");
        AttemptedAt = attemptedAt;
        Status = AttemptStatus.Scheduled;
        Outcome = ExamOutcome.Pending;
        GradeDecision = GradeDecision.Pending;
    }

    public void Book()
    {
        if (Status != AttemptStatus.Scheduled)
            throw new InvalidOperationException("Only a scheduled attempt can be booked.");
        Status = AttemptStatus.Booked;
    }

    public void Start()
    {
        if (Status is not (AttemptStatus.Scheduled or AttemptStatus.Booked))
            throw new InvalidOperationException("Only a scheduled or booked attempt can be started.");
        Status = AttemptStatus.InProgress;
    }

    public void Complete(
        ExamOutcome outcome,
        GradeDecision decision,
        GradeValue? grade,
        DateTimeOffset? recordedAt = null)
    {
        EnsureOpen();

        if (outcome == ExamOutcome.Pending)
            throw new ArgumentException("A completed attempt needs a final outcome.", nameof(outcome));

        switch (outcome)
        {
            case ExamOutcome.Passed:
                if (grade is null || grade.IsPassFail)
                    throw new ArgumentException("A passed graded exam requires a numeric grade.", nameof(grade));
                if (decision is not (GradeDecision.Accepted or GradeDecision.Rejected))
                    throw new ArgumentException("A passed numeric grade must be accepted or rejected.", nameof(decision));
                break;

            case ExamOutcome.EligibilityPassed:
                if (grade is null || !grade.IsPassFail)
                    throw new ArgumentException("An eligibility exam requires an eligibility result.", nameof(grade));
                if (decision != GradeDecision.NotApplicable)
                    throw new ArgumentException("Eligibility results do not have an accept/reject grade decision.", nameof(decision));
                break;

            case ExamOutcome.Failed:
                if (grade is not null)
                    throw new ArgumentException("A failed attempt cannot carry an accepted grade.", nameof(grade));
                if (decision != GradeDecision.NotApplicable)
                    throw new ArgumentException("A failed attempt has no grade decision.", nameof(decision));
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        var isAccepted = decision is GradeDecision.Accepted or GradeDecision.NotApplicable;
        if (isAccepted && recordedAt is null)
            throw new ArgumentException("An accepted result must have a recording timestamp.", nameof(recordedAt));
        if (!isAccepted && recordedAt is not null)
            throw new ArgumentException("A rejected or failed result cannot have an official recording timestamp.", nameof(recordedAt));
        if (recordedAt is not null && recordedAt.Value < AttemptedAt)
            throw new ArgumentException("A result cannot be recorded before the attempt took place.", nameof(recordedAt));

        Status = AttemptStatus.Completed;
        Outcome = outcome;
        GradeDecision = decision;
        Grade = grade;
        RecordedAt = recordedAt;
    }

    public void Withdraw()
    {
        EnsureOpen();
        Status = AttemptStatus.Withdrawn;
        Outcome = ExamOutcome.Pending;
        GradeDecision = GradeDecision.Pending;
        Grade = null;
        RecordedAt = null;
    }

    private void EnsureOpen()
    {
        if (Status is AttemptStatus.Completed or AttemptStatus.Withdrawn)
            throw new InvalidOperationException("This attempt is already closed.");
    }
}
