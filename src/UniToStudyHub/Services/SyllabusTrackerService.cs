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
    public SyllabusTopicData Add(StudyData data, string courseId, string title, string description = "",
        int estimatedMinutes = 0, string sourceUrl = "")
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Topic title is required.", nameof(title));
        if (estimatedMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes));
        var topic = new SyllabusTopicData
        {
            Id = Guid.NewGuid().ToString("N"),
            CourseId = courseId,
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            EstimatedMinutes = estimatedMinutes,
            SourceUrl = sourceUrl?.Trim() ?? string.Empty,
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
        topic.LastStudiedAt = status == SyllabusTopicStatus.Completed ? (studiedAt ?? DateTime.UtcNow) : topic.LastStudiedAt;
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
        var remaining = topics
            .Where(x => x.Status != SyllabusTopicStatus.Completed)
            .Sum(x => x.EstimatedMinutes);
        return new SyllabusCoverage(courseId, topics.Count, completed, inProgress, percent, remaining);
    }

    public IReadOnlyList<SyllabusTopicData> GetTopics(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        return data.SyllabusTopics
            .Where(x => string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static void EnsureCourse(StudyData data, string courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId) ||
            !data.Courses.Any(x => string.Equals(x.Id, courseId, StringComparison.Ordinal)))
            throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
    }
}
