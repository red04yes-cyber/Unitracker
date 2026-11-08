using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Domain.ValueObjects;

namespace UniToStudyHub.Domain.Entities;

/// <summary>
/// A single attempt at an exam. The attempt state, the exam outcome and the
/// decision about the grade are deliberately three separate concepts.
/// Previous attempts are never removed or overwritten when a new one is added.
/// </summary>
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

    /// <summary>
    /// True only when this attempt produced an official, recorded result that
    /// contributes CFU. A passed exam whose grade was rejected is not official.
    /// </summary>
    public bool IsOfficialResult =>
        Status == AttemptStatus.Completed &&
        RecordedAt is not null &&
        Outcome is ExamOutcome.Passed or ExamOutcome.EligibilityPassed &&
        GradeDecision is GradeDecision.Accepted or GradeDecision.NotApplicable;

    public ExamAttempt(string examId, DateTimeOffset attemptedAt, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(examId))
            throw new ArgumentException("Exam id is required.", nameof(examId));
        if (id is not null && string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Attempt id cannot be whitespace.", nameof(id));
        if (attemptedAt == default)
            throw new ArgumentException("An attempt must have a real date.", nameof(attemptedAt));

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

    /// <summary>
    /// Closes the attempt with a final outcome. A numeric grade is only valid
    /// for a passed exam; an eligibility result acquires CFU without entering
    /// the numeric average. Rejected grades and failed attempts are not
    /// verbalized, therefore they must not carry a recording timestamp.
    /// </summary>
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

        var requiresRecording = decision == GradeDecision.Accepted || outcome == ExamOutcome.EligibilityPassed;
        if (requiresRecording && recordedAt is null)
            throw new ArgumentException("An accepted result must have a recording timestamp.", nameof(recordedAt));
        if (!requiresRecording && recordedAt is not null)
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
