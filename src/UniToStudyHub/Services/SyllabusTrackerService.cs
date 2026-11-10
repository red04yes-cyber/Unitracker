using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record SyllabusCoverage(
    string CourseId,
    int TotalTopics,
    int CompletedTopics,
    int InProgressTopics,
    decimal CompletedPercent,
    int EstimatedRemainingMinutes)
{
    public decimal AverageCompletionPercent { get; init; }
    public decimal AveragePreparedness { get; init; }
    public int RatedTopics { get; init; }
}

/// <summary>
/// Owns syllabus hierarchy and progress rules. Completion and preparedness
/// are separate measures: completing a task does not imply exam mastery.
/// </summary>
public sealed class SyllabusTrackerService
{
    public SyllabusTopicData Add(StudyData data, string courseId, string title, string description = "",
        int estimatedMinutes = 0, string sourceUrl = "", string parentTopicId = "",
        SyllabusTopicKind kind = SyllabusTopicKind.Topic)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Topic title is required.", nameof(title));
        if (estimatedMinutes < 0)
            throw new ArgumentOutOfRangeException(nameof(estimatedMinutes));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        ValidateParent(data, courseId, kind, parentTopicId);

        var siblings = data.SyllabusTopics.Where(x =>
            x.CourseId == courseId && string.Equals(x.ParentTopicId, parentTopicId ?? string.Empty, StringComparison.Ordinal));
        var topic = new SyllabusTopicData
        {
            Id = Guid.NewGuid().ToString("N"),
            CourseId = courseId,
            ParentTopicId = parentTopicId?.Trim() ?? string.Empty,
            Kind = kind,
            Position = siblings.Select(x => x.Position).DefaultIfEmpty(-1).Max() + 1,
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
        var topic = FindTopic(data, topicId);
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        topic.Status = status;
        if (status == SyllabusTopicStatus.Completed)
            topic.CompletionPercent = 100;
        topic.LastStudiedAt = status == SyllabusTopicStatus.Completed ? (studiedAt ?? DateTime.UtcNow) : topic.LastStudiedAt;
        topic.UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCompletionPercent(StudyData data, string topicId, int percent)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        var topic = FindTopic(data, topicId);
        topic.CompletionPercent = percent;
        topic.UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePreparedness(StudyData data, string topicId, TopicPreparednessLevel level)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        var topic = FindTopic(data, topicId);
        topic.Preparedness = level;
        topic.UpdatedAt = DateTime.UtcNow;
    }

    public SyllabusCoverage GetCoverage(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        var all = data.SyllabusTopics.Where(x => x.CourseId == courseId).ToList();
        var parentIds = new HashSet<string>(all.Where(x => !string.IsNullOrWhiteSpace(x.ParentTopicId))
            .Select(x => x.ParentTopicId), StringComparer.Ordinal);
        var topics = all.Where(x => !parentIds.Contains(x.Id)).ToList();
        var completed = topics.Count(x => x.Status == SyllabusTopicStatus.Completed);
        var inProgress = topics.Count(x => x.Status == SyllabusTopicStatus.InProgress);
        var percent = topics.Count == 0 ? 0m : Math.Round(completed * 100m / topics.Count, 2, MidpointRounding.AwayFromZero);
        var rated = topics.Where(x => x.Preparedness != TopicPreparednessLevel.Unrated).ToList();
        return new SyllabusCoverage(courseId, topics.Count, completed, inProgress, percent,
            topics.Where(x => x.Status != SyllabusTopicStatus.Completed).Sum(x => x.EstimatedMinutes))
        {
            AverageCompletionPercent = topics.Count == 0 ? 0m :
                Math.Round(topics.Average(x => (decimal)(x.Status == SyllabusTopicStatus.Completed ? 100 : x.CompletionPercent)), 2, MidpointRounding.AwayFromZero),
            AveragePreparedness = rated.Count == 0 ? 0m :
                Math.Round(rated.Average(x => (decimal)(int)x.Preparedness), 2, MidpointRounding.AwayFromZero),
            RatedTopics = rated.Count
        };
    }

    /// <summary>Returns a stable pre-order tree flattened for list/tree controls.</summary>
    public IReadOnlyList<SyllabusTopicData> GetTopics(StudyData data, string courseId)
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        var all = data.SyllabusTopics.Where(x => x.CourseId == courseId).ToList();
        var children = all.GroupBy(x => x.ParentTopicId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(t => t.Position)
                .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase).ToList(), StringComparer.Ordinal);
        var result = new List<SyllabusTopicData>();
        void Append(string parentId)
        {
            if (!children.TryGetValue(parentId, out var items)) return;
            foreach (var item in items)
            {
                result.Add(item);
                Append(item.Id);
            }
        }
        Append(string.Empty);
        return result;
    }

    public IReadOnlyList<SyllabusTopicData> GetChildren(StudyData data, string courseId, string parentTopicId = "")
    {
        ArgumentNullException.ThrowIfNull(data);
        EnsureCourse(data, courseId);
        if (!string.IsNullOrWhiteSpace(parentTopicId))
        {
            var parent = FindTopic(data, parentTopicId);
            if (parent.CourseId != courseId) throw new ArgumentException("Parent belongs to another course.", nameof(parentTopicId));
        }
        return data.SyllabusTopics.Where(x => x.CourseId == courseId &&
                string.Equals(x.ParentTopicId, parentTopicId ?? string.Empty, StringComparison.Ordinal))
            .OrderBy(x => x.Position).ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static SyllabusTopicData FindTopic(StudyData data, string topicId) =>
        data.SyllabusTopics.FirstOrDefault(x => string.Equals(x.Id, topicId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Syllabus topic '{topicId}' does not exist.", nameof(topicId));

    private static void ValidateParent(StudyData data, string courseId, SyllabusTopicKind kind, string? parentId)
    {
        var hasParent = !string.IsNullOrWhiteSpace(parentId);
        if (kind == SyllabusTopicKind.Module && hasParent)
            throw new ArgumentException("A module cannot have a parent.", nameof(parentId));
        if (kind == SyllabusTopicKind.Chapter && !hasParent)
            throw new ArgumentException("A chapter must belong to a module.", nameof(parentId));
        if (!hasParent) return; // Supports legacy flat topics.
        var parent = FindTopic(data, parentId!);
        if (parent.CourseId != courseId)
            throw new ArgumentException("A syllabus parent must belong to the same course.", nameof(parentId));
        var valid = kind switch
        {
            SyllabusTopicKind.Chapter => parent.Kind == SyllabusTopicKind.Module,
            SyllabusTopicKind.Topic => parent.Kind == SyllabusTopicKind.Chapter,
            _ => false
        };
        if (!valid) throw new ArgumentException("Invalid syllabus hierarchy: modules contain chapters and chapters contain topics.", nameof(parentId));
    }

    private static void EnsureCourse(StudyData data, string courseId)
    {
        if (string.IsNullOrWhiteSpace(courseId) ||
            !data.Courses.Any(x => string.Equals(x.Id, courseId, StringComparison.Ordinal)))
            throw new ArgumentException($"Course '{courseId}' does not exist.", nameof(courseId));
    }
}
