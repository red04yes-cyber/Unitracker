using UniToStudyHub.Domain.Enums;

namespace UniToStudyHub.Domain.Entities;

public sealed class CareerExam
{
    private readonly List<ExamAttempt> _attempts = new();
    public string Id { get; }
    public string CourseId { get; }
    public string Name { get; }
    public int Cfu { get; }
    public bool CountsTowardAverage { get; }
    public IReadOnlyCollection<ExamAttempt> Attempts => _attempts.AsReadOnly();

    public CareerExam(string courseId, string name, int cfu, bool countsTowardAverage = true, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(courseId)) throw new ArgumentException("Course id is required.", nameof(courseId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Exam name is required.", nameof(name));
        if (cfu <= 0) throw new ArgumentOutOfRangeException(nameof(cfu), "CFU must be positive.");
        CourseId = courseId;
        Name = name.Trim();
        Cfu = cfu;
        CountsTowardAverage = countsTowardAverage;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    public void AddAttempt(ExamAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (!string.Equals(attempt.ExamId, Id, StringComparison.Ordinal))
            throw new ArgumentException("The attempt belongs to a different exam.", nameof(attempt));
        if (_attempts.Any(a => a.Id == attempt.Id))
            throw new InvalidOperationException($"Attempt id '{attempt.Id}' already exists.");
        _attempts.Add(attempt);
    }

    public ExamAttempt? GetLatestAttempt() => _attempts
        .OrderByDescending(a => a.AttemptedAt)
        .ThenByDescending(a => a.RecordedAt)
        .ThenByDescending(a => a.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    public ExamAttempt? GetOfficialAttempt() => _attempts
        .Where(IsOfficial)
        .OrderByDescending(a => a.RecordedAt)
        .ThenByDescending(a => a.AttemptedAt)
        .ThenByDescending(a => a.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    public bool AreCfuAcquired() => GetOfficialAttempt() is not null;

    private static bool IsOfficial(ExamAttempt attempt) =>
        attempt.Status == AttemptStatus.Completed &&
        attempt.RecordedAt is not null &&
        attempt.GradeDecision is GradeDecision.Accepted or GradeDecision.NotApplicable &&
        attempt.Outcome is ExamOutcome.Passed or ExamOutcome.EligibilityPassed;
}