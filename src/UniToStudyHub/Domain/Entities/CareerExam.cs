using UniToStudyHub.Domain.Enums;

namespace UniToStudyHub.Domain.Entities;

/// <summary>
/// An exam in the student's career. It owns every attempt made for the same
/// teaching unit, so repeated attempts share one CFU contribution.
/// </summary>
public sealed class CareerExam
{
    private readonly List<ExamAttempt> _attempts = new();

    public string Id { get; }

    /// <summary>Legacy course identifier retained for the existing JSON/SQLite data.</summary>
    public string CourseId { get; }

    /// <summary>The teaching unit this exam belongs to. It defaults to CourseId for legacy data.</summary>
    public string TeachingUnitId { get; }

    public string Name { get; }
    public int Cfu { get; }
    public bool CountsTowardAverage { get; }
    public IReadOnlyCollection<ExamAttempt> Attempts => _attempts.AsReadOnly();

    public CareerExam(
        string courseId,
        string name,
        int cfu,
        bool countsTowardAverage = true,
        string? id = null,
        string? teachingUnitId = null)
    {
        if (string.IsNullOrWhiteSpace(courseId)) throw new ArgumentException("Course id is required.", nameof(courseId));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Exam name is required.", nameof(name));
        if (cfu <= 0) throw new ArgumentOutOfRangeException(nameof(cfu), "CFU must be positive.");
        CourseId = courseId;
        TeachingUnitId = string.IsNullOrWhiteSpace(teachingUnitId) ? courseId : teachingUnitId;
        Name = name.Trim();
        Cfu = cfu;
        CountsTowardAverage = countsTowardAverage;
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
    }

    /// <summary>
    /// Creates an exam directly from a persisted teaching unit. The course id
    /// and the teaching unit id coincide for data projected from legacy courses.
    /// </summary>
    public CareerExam(TeachingUnit teachingUnit, string? id = null)
        : this(
            teachingUnit?.Id ?? throw new ArgumentNullException(nameof(teachingUnit)),
            teachingUnit.Name,
            teachingUnit.Cfu.Value,
            teachingUnit.CountsTowardAverage,
            id,
            teachingUnit.Id)
    {
    }

    public void AddAttempt(ExamAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        if (!string.Equals(attempt.ExamId, Id, StringComparison.Ordinal))
            throw new ArgumentException("The attempt belongs to a different exam.", nameof(attempt));
        if (_attempts.Any(a => string.Equals(a.Id, attempt.Id, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Attempt id '{attempt.Id}' already exists.");
        _attempts.Add(attempt);
    }

    /// <summary>The most recent attempt, regardless of whether its result is official.</summary>
    public ExamAttempt? GetLatestAttempt() => _attempts
        .OrderByDescending(a => a.AttemptedAt)
        .ThenByDescending(a => a.RecordedAt)
        .ThenByDescending(a => a.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    /// <summary>
    /// The latest recorded official result. This is not necessarily the latest
    /// attempt: a later rejected or still-open attempt must not replace it.
    /// </summary>
    public ExamAttempt? GetOfficialAttempt() => _attempts
        .Where(a => a.IsOfficialResult)
        .OrderByDescending(a => a.RecordedAt)
        .ThenByDescending(a => a.AttemptedAt)
        .ThenByDescending(a => a.Id, StringComparer.Ordinal)
        .FirstOrDefault();

    public bool HasOfficialResult() => GetOfficialAttempt() is not null;

    public bool AreCfuAcquired() => HasOfficialResult();
}
