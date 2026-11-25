using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record SyllabusCoverage(
    string CourseId,
    int TotalTopics,
    int CompletedTopics,
    int InProgressTopics,
    decimal CompletedPercent,
    int EstimatedRemainingMinutes);

/// <summary>
/// Owns syllabus-topic rules. Coverage is count-based and explicitly reports
/// the denominator; it is not presented as an official exam prediction.
/// </summary>
public sealed class SyllabusTrackerService
{
    public const int MaximumTitleLength = 300;
    public const int MaximumDescriptionLength = 4_000;
    public const int MaximumSourceUrlLength = 2_048;
    public const int MaximumEstimatedMinutes = 525_600;

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "wstoken", "password", "pass", "secret", "api_key", "apikey",
        "access_token", "authorization", "signature", "sig"
    };

    public SyllabusTopicData Add(StudyData data, string courseId, string title, string description = "",
        int estimatedMinutes = 0, string sourceUrl = "")
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);

        var normalizedTitle = NormalizeText(title, MaximumTitleLength, allowLineBreaks: false, nameof(title));
        if (string.IsNullOrWhiteSpace(normalizedTitle))
            throw new ArgumentException("Topic title is required.", nameof(title));

        var normalizedDescription = NormalizeText(description, MaximumDescriptionLength, allowLineBreaks: true, nameof(description));
        var normalizedSourceUrl = NormalizeSourceUrl(sourceUrl);

        if (estimatedMinutes is < 0 or > MaximumEstimatedMinutes)
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes),
                $"Estimated minutes must be between 0 and {MaximumEstimatedMinutes}.");

        var topic = new SyllabusTopicData
        {
            Id = Guid.NewGuid().ToString("N"),
            CourseId = courseId,
            Title = normalizedTitle,
            Description = normalizedDescription,
            EstimatedMinutes = estimatedMinutes,
            SourceUrl = normalizedSourceUrl,
            UpdatedAt = DateTime.UtcNow
        };
        data.SyllabusTopics.Add(topic);
        return topic;
    }

    public void UpdateStatus(StudyData data, string topicId, SyllabusTopicStatus status, DateTime? studiedAt = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var topic = data.SyllabusTopics.FirstOrDefault(x => string.Equals(x.Id, topicId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Syllabus topic '{topicId}' does not exist.", nameof(topicId));
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));

        topic.Status = status;
        if (status == SyllabusTopicStatus.Completed)
            topic.LastStudiedAt = NormalizeTimestamp(studiedAt ?? DateTime.UtcNow);
        topic.UpdatedAt = DateTime.UtcNow;
    }

    public SyllabusCoverage GetCoverage(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        var topics = data.SyllabusTopics.Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal)).ToList();
        var completed = topics.Count(x => x.Status == SyllabusTopicStatus.Completed);
        var inProgress = topics.Count(x => x.Status == SyllabusTopicStatus.InProgress);
        var percent = topics.Count == 0 ? 0m : Math.Round(completed * 100m / topics.Count, 2, MidpointRounding.AwayFromZero);

        // Legacy/imported data may contain negative estimates or enough large
        // values to overflow Int32. Ignore invalid negatives and saturate the
        // public integer summary instead of wrapping into a negative duration.
        long remaining = 0;
        foreach (var topic in topics.Where(x => x.Status != SyllabusTopicStatus.Completed))
        {
            if (topic.EstimatedMinutes <= 0)
                continue;

            remaining = Math.Min((long)int.MaxValue, remaining + topic.EstimatedMinutes);
        }

        return new SyllabusCoverage(courseId, topics.Count, completed, inProgress, percent, (int)remaining);
    }

    public IReadOnlyList<SyllabusTopicData> GetTopics(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        return data.SyllabusTopics
            .Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string NormalizeText(string? value, int maximumLength, bool allowLineBreaks, string parameterName)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = new string(value
            .Where(character =>
                !char.IsControl(character) ||
                (allowLineBreaks && (character is '\\r' or '\\n' or '\\t')))
            .ToArray())
            .Trim();

        if (normalized.Length > maximumLength)
            throw new ArgumentOutOfRangeException(parameterName, $"Text exceeds {maximumLength} characters.");

        return normalized;
    }

    private static string NormalizeSourceUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        if (value.Length > MaximumSourceUrlLength || value.Any(char.IsControl))
            throw new ArgumentOutOfRangeException(nameof(value), "Source URL is too long or contains control characters.");

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Source URL must be an absolute HTTP(S) URL without credentials or fragments.", nameof(value));

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var rawKey = pair.Split('=', 2)[0].Replace('+', ' ');
            string key;
            try
            {
                key = Uri.UnescapeDataString(rawKey);
            }
            catch (UriFormatException)
            {
                throw new ArgumentException("Source URL contains an invalid query parameter.", nameof(value));
            }

            if (SensitiveQueryKeys.Contains(key))
                throw new ArgumentException("Source URL must not contain authentication or secret parameters.", nameof(value));
        }

        var normalized = uri.AbsoluteUri;
        if (normalized.Length > MaximumSourceUrlLength)
            throw new ArgumentOutOfRangeException(nameof(value), "Normalized source URL is too long.");
        return normalized;
    }

    private static DateTime NormalizeTimestamp(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static void EnsureCourse(StudyData data, string courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId) ||
            !data.Courses.Any(x => string.Equals(x.Id, courseId, StringComparison.Ordinal)))
            throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
    }
}
