using System.Collections.ObjectModel;
using System.Text.Json;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record ActivityTimelineSlot(
    int Index,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int RecordedSeconds,
    bool HasOverlap,
    IReadOnlyList<string> ActivityIds);

public sealed record ActivityTimeline(
    DateOnly Day,
    int SlotMinutes,
    int TotalSeconds,
    int RecordedSeconds,
    int UnregisteredSeconds,
    int OverlapSeconds,
    IReadOnlyList<ActivityTimelineSlot> Slots);

public sealed record ActivityHeatmapCell(
    DateOnly Day,
    int Hour,
    int RecordedSeconds,
    int ActivityCount);

public sealed record ActivityHeatmap(
    DateOnly From,
    DateOnly To,
    IReadOnlyList<ActivityHeatmapCell> Cells);

public sealed record ActivityOverlap(
    string FirstActivityId,
    string SecondActivityId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt);

public sealed record ActivitySummary(
    DateOnly From,
    DateOnly To,
    int RecordedSeconds,
    int ActivityCount,
    IReadOnlyDictionary<StudyActivityType, int> ByType,
    IReadOnlyDictionary<string, int> ByCourse)
{
    /// <summary>Seconds covered by two or more activities; included in RecordedSeconds only once.</summary>
    public int OverlapSeconds { get; init; }

    /// <summary>Unique seconds per syllabus topic; activities without a topic are omitted.</summary>
    public IReadOnlyDictionary<string, int> ByTopic { get; init; } = new Dictionary<string, int>();
}

/// <summary>
/// Owns the unified activity ledger. It records real intervals, keeps manual
/// entries first-class and links timer activities to their focus session so the
/// same time cannot be counted twice. It never invents activity for empty slots.
/// </summary>
public sealed class ActivityLedgerService
{
    private static readonly JsonSerializerOptions RevisionJsonOptions = new();

    /// <summary>
    /// Convenience entry point for the diary/quick-log screen. It deliberately
    /// delegates to <see cref="AddManual"/> so permissions, revisions, privacy
    /// and interval validation stay in one place.
    /// </summary>
    public StudyActivityData AddQuickLog(
        StudyData data,
        StudyActivityType type,
        string title,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string? courseId = null,
        string? syllabusTopicId = null,
        string? taskId = null,
        string? missionId = null,
        string? note = null,
        string? result = null,
        IEnumerable<string>? tags = null,
        bool isPrivate = false,
        int? focusRating = null,
        int? difficultyRating = null,
        int? qualityRating = null,
        int? energyRating = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (type is not (StudyActivityType.Study or StudyActivityType.Exercise or StudyActivityType.Review or
                         StudyActivityType.Reading or StudyActivityType.Simulation or StudyActivityType.Quiz))
            throw new ArgumentOutOfRangeException(nameof(type), "The selected type is not a quick-log study type.");
        if (endedAt <= startedAt)
            throw new ArgumentException("The quick log must end after it starts.", nameof(endedAt));

        var activity = new StudyActivityData
        {
            Type = type,
            Title = string.IsNullOrWhiteSpace(title) ? type.ToString() : title.Trim(),
            CourseId = courseId?.Trim() ?? string.Empty,
            SyllabusTopicId = syllabusTopicId?.Trim() ?? string.Empty,
            TaskId = taskId?.Trim() ?? string.Empty,
            MissionId = missionId?.Trim() ?? string.Empty,
            StartedAt = startedAt,
            EndedAt = endedAt,
            EffectiveSeconds = Math.Max(0, (int)Math.Floor((endedAt - startedAt).TotalSeconds)),
            TimeZoneId = TimeZoneInfo.Local.Id,
            Note = note?.Trim() ?? string.Empty,
            Result = result?.Trim() ?? string.Empty,
            Tags = new ObservableCollection<string>(tags ?? Array.Empty<string>()),
            IsPrivate = isPrivate,
            FocusRating = focusRating,
            DifficultyRating = difficultyRating,
            QualityRating = qualityRating,
            EnergyRating = energyRating
        };
        return AddManual(data, activity, now);
    }

    public StudyActivityData AddManual(StudyData data, StudyActivityData activity, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(activity);
        var at = now ?? DateTimeOffset.Now;
        if (string.IsNullOrWhiteSpace(activity.Id)) activity.Id = Guid.NewGuid().ToString("N");
        if (data.StudyActivities.Any(x => string.Equals(x.Id, activity.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Activity '{activity.Id}' already exists.", nameof(activity));

        if (activity.StartedAt == default) activity.StartedAt = at;
        if (activity.EndedAt is null)
        {
            if (activity.EffectiveSeconds <= 0)
                throw new ArgumentException("A manual activity needs an end timestamp or an effective duration.", nameof(activity));
            activity.EndedAt = activity.StartedAt.AddSeconds(activity.EffectiveSeconds);
        }
        if (activity.EffectiveSeconds <= 0)
            activity.EffectiveSeconds = Math.Max(0, (int)Math.Floor((activity.EndedAt.Value - activity.StartedAt).TotalSeconds));
        activity.Origin = StudyActivityOrigin.Manual;
        activity.Status = StudyActivityStatus.Completed;
        activity.DeletedAt = null;
        activity.Revision = 1;
        activity.CreatedAt = activity.CreatedAt == default ? at : activity.CreatedAt;
        activity.UpdatedAt = at;
        NormalizeAndValidate(data, activity);

        data.StudyActivities.Add(activity);
        AddRevision(data, activity, ActivityRevisionAction.Created, "Inserimento manuale", null, at);
        return activity;
    }

    /// <summary>
    /// Creates or updates the ledger entry for a focus session. Calling this
    /// repeatedly, or after a restart, updates the same row (matched by
    /// SessionId) instead of creating duplicate hours.
    /// </summary>
    public StudyActivityData SyncFocusSession(
        StudyData data,
        StudySessionData session,
        DateTimeOffset? now = null,
        string? taskId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(session);
        if (!data.StudySessions.Any(x => string.Equals(x.Id, session.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Study session '{session.Id}' is not part of this data set.", nameof(session));

        var at = now ?? DateTimeOffset.Now;
        var existing = data.StudyActivities.FirstOrDefault(x =>
            string.Equals(x.SessionId, session.Id, StringComparison.Ordinal));
        if (existing?.IsDeleted == true)
            return existing;
        if (existing is not null && existing.Origin != StudyActivityOrigin.Timer)
            return existing;

        var effectiveTaskId = string.IsNullOrWhiteSpace(taskId)
            ? existing?.TaskId ?? string.Empty
            : taskId.Trim();
        var linkedTask = string.IsNullOrWhiteSpace(effectiveTaskId)
            ? null
            : data.StudyTasks.FirstOrDefault(x => string.Equals(x.Id, effectiveTaskId, StringComparison.Ordinal))
                ?? throw new ArgumentException($"Study task '{effectiveTaskId}' does not exist.", nameof(taskId));
        if (existing is null && linkedTask is not null &&
            linkedTask.Status is not (StudyTaskStatus.Todo or StudyTaskStatus.InProgress))
            throw new InvalidOperationException("A new focus session can only be linked to an open task.");
        if (linkedTask is not null &&
            !string.IsNullOrWhiteSpace(session.CourseId) &&
            !string.IsNullOrWhiteSpace(linkedTask.CourseId) &&
            !string.Equals(session.CourseId, linkedTask.CourseId, StringComparison.Ordinal))
            throw new ArgumentException("The focus session and linked task belong to different courses.", nameof(taskId));
        if (linkedTask is not null && string.IsNullOrWhiteSpace(session.CourseId) &&
            !string.IsNullOrWhiteSpace(linkedTask.CourseId))
        {
            session.CourseId = linkedTask.CourseId;
            session.UpdatedAt = at;
        }

        var activityCourseId = string.IsNullOrWhiteSpace(session.CourseId)
            ? linkedTask?.CourseId ?? string.Empty
            : session.CourseId;
        var previousTaskId = existing?.TaskId ?? string.Empty;
        var taskLinkChanged = existing is not null &&
            !string.Equals(previousTaskId, effectiveTaskId, StringComparison.Ordinal);
        var beforeTaskLink = taskLinkChanged
            ? JsonSerializer.Serialize(existing, RevisionJsonOptions)
            : null;
        var effective = FocusSessionService.ElapsedSeconds(session, at);
        var isNew = existing is null;
        if (isNew)
        {
            existing = new StudyActivityData
            {
                Id = Guid.NewGuid().ToString("N"),
                Type = StudyActivityType.Study,
                Title = string.IsNullOrWhiteSpace(session.Topic) ? "Sessione di studio" : session.Topic,
                CourseId = activityCourseId,
                TaskId = effectiveTaskId,
                SessionId = session.Id,
                StartedAt = session.StartedAt,
                TimeZoneId = TimeZoneInfo.Local.Id,
                Origin = StudyActivityOrigin.Timer,
                CreatedAt = session.CreatedAt,
                Revision = 1
            };
        }

        existing!.Title = string.IsNullOrWhiteSpace(session.Topic) ? existing.Title : session.Topic.Trim();
        existing.CourseId = activityCourseId;
        existing.TaskId = effectiveTaskId;
        existing.EffectiveSeconds = effective;
        existing.EndedAt = session.State is StudySessionState.Completed or StudySessionState.Cancelled
            ? session.EndedAt ?? at
            : null;
        existing.Status = session.State switch
        {
            StudySessionState.Completed => StudyActivityStatus.Completed,
            StudySessionState.Cancelled => StudyActivityStatus.Cancelled,
            _ => StudyActivityStatus.Running
        };
        existing.UpdatedAt = at;
        NormalizeAndValidate(data, existing);
        if (isNew)
        {
            data.StudyActivities.Add(existing);
            AddRevision(data, existing, ActivityRevisionAction.Created, "Sessione focus", null, at);
        }
        else if (taskLinkChanged)
        {
            existing.Revision++;
            AddRevision(data, existing, ActivityRevisionAction.Updated, "Collegamento al task", beforeTaskLink, at);
        }
        return existing;
    }

    /// <summary>
    /// Calculates task progress from unique real activity intervals. Planned
    /// calendar blocks are deliberately excluded; overlapping manual and timer
    /// records are unioned so the same minute is never credited twice.
    /// </summary>
    public TaskProgressSnapshot GetTaskProgress(
        StudyData data,
        string taskId,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("Task id is required.", nameof(taskId));
        var task = data.StudyTasks.FirstOrDefault(x => string.Equals(x.Id, taskId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Study task '{taskId}' does not exist.", nameof(taskId));
        var recordedSeconds = UnionSeconds(GetRecordedIntervals(data, now ?? DateTimeOffset.Now)
            .Where(x => string.Equals(x.Activity.TaskId, task.Id, StringComparison.Ordinal))
            .Select(x => (x.Start, x.End)));
        var estimatedSeconds = Math.Max(0, task.EstimatedMinutes) * 60;
        var remainingSeconds = Math.Max(0, estimatedSeconds - recordedSeconds);
        var progressPercent = estimatedSeconds == 0
            ? 0
            : Math.Clamp((int)Math.Round(recordedSeconds * 100d / estimatedSeconds), 0, 100);
        return new TaskProgressSnapshot(
            task.Id,
            Math.Max(0, task.EstimatedMinutes),
            recordedSeconds,
            remainingSeconds,
            progressPercent,
            estimatedSeconds > 0 && recordedSeconds > estimatedSeconds);
    }

    public IReadOnlyList<StudyActivityData> SyncFocusSessions(StudyData data, DateTimeOffset? now = null) =>
        data.StudySessions.Select(x => SyncFocusSession(data, x, now)).ToList();

    public StudyActivityData Update(
        StudyData data,
        StudyActivityData updated,
        string reason,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(updated);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A correction reason is required.", nameof(reason));
        var existing = data.StudyActivities.FirstOrDefault(x =>
            string.Equals(x.Id, updated.Id, StringComparison.Ordinal) && !x.IsDeleted)
            ?? throw new ArgumentException($"Activity '{updated.Id}' does not exist or was deleted.", nameof(updated));
        if (updated.Id != existing.Id) throw new ArgumentException("The activity id cannot be changed.", nameof(updated));
        if (updated.EndedAt is null)
            throw new ArgumentException("A corrected activity needs an end timestamp.", nameof(updated));
        if (updated.EffectiveSeconds <= 0)
            updated.EffectiveSeconds = Math.Max(0, (int)Math.Floor((updated.EndedAt.Value - updated.StartedAt).TotalSeconds));
        updated.DeletedAt = null;
        updated.CreatedAt = existing.CreatedAt;
        updated.Revision = existing.Revision + 1;
        updated.UpdatedAt = now ?? DateTimeOffset.Now;
        NormalizeAndValidate(data, updated);

        if (!string.IsNullOrWhiteSpace(updated.SessionId) &&
            data.StudyActivities.Any(x => !string.Equals(x.Id, existing.Id, StringComparison.Ordinal) &&
                                          !string.IsNullOrWhiteSpace(x.SessionId) &&
                                          string.Equals(x.SessionId, updated.SessionId, StringComparison.Ordinal)))
            throw new ArgumentException("Another activity is already linked to this focus session.", nameof(updated));

        var before = JsonSerializer.Serialize(existing, RevisionJsonOptions);
        CopyActivity(existing, updated);
        AddRevision(data, existing, ActivityRevisionAction.Updated, reason.Trim(), before, existing.UpdatedAt);
        return existing;
    }

    public StudyActivityData SoftDelete(
        StudyData data,
        string activityId,
        string reason,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A deletion reason is required.", nameof(reason));
        var activity = data.StudyActivities.FirstOrDefault(x =>
            string.Equals(x.Id, activityId, StringComparison.Ordinal) && !x.IsDeleted)
            ?? throw new ArgumentException($"Activity '{activityId}' does not exist or was deleted.", nameof(activityId));
        var at = now ?? DateTimeOffset.Now;
        var before = JsonSerializer.Serialize(activity, RevisionJsonOptions);
        activity.DeletedAt = at;
        activity.UpdatedAt = at;
        activity.Revision++;
        AddRevision(data, activity, ActivityRevisionAction.Deleted, reason.Trim(), before, at);
        return activity;
    }

    public IReadOnlyList<StudyActivityData> GetActive(StudyData data) =>
        data.StudyActivities
            .Where(x => !x.IsDeleted && x.Status == StudyActivityStatus.Running)
            .OrderBy(x => x.StartedAt)
            .ToList();

    public IReadOnlyList<StudyActivityData> GetDiary(
        StudyData data,
        DateOnly from,
        DateOnly to,
        bool includePrivate = true,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");
        var zone = ResolveTimeZone(timeZoneId);
        var start = StartOfDay(from, zone);
        var end = StartOfDay(to.AddDays(1), zone);
        return data.StudyActivities
            .Where(x => !x.IsDeleted && x.StartedAt < end && (x.EndedAt ?? DateTimeOffset.MaxValue) > start)
            .Where(x => includePrivate || !x.IsPrivate)
            .OrderBy(x => x.StartedAt)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToList();
    }

    public IReadOnlyList<StudyActivityData> GetForDay(
        StudyData data,
        DateOnly day,
        string? timeZoneId = null,
        DateTimeOffset? now = null)
    {
        var zone = ResolveTimeZone(timeZoneId);
        var dayStart = StartOfDay(day, zone);
        var dayEnd = StartOfDay(day.AddDays(1), zone);
        return GetRecordedIntervals(data, now ?? DateTimeOffset.Now)
            .Where(x => x.End > dayStart && x.Start < dayEnd)
            .Select(x => x.Activity)
            .DistinctBy(x => x.Id)
            .OrderBy(x => x.StartedAt)
            .ToList();
    }

    public IReadOnlyList<ActivityOverlap> FindOverlaps(
        StudyData data,
        DateOnly? day = null,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var zone = ResolveTimeZone(timeZoneId);
        var at = now ?? DateTimeOffset.Now;
        var dayStart = day is { } value ? StartOfDay(value, zone) : DateTimeOffset.MinValue;
        var dayEnd = day is { } lastDay ? StartOfDay(lastDay.AddDays(1), zone) : DateTimeOffset.MaxValue;
        var intervals = GetRecordedIntervals(data, at)
            .Where(x => x.End > dayStart && x.Start < dayEnd)
            .Select(x =>
            {
                var start = x.Start < dayStart ? dayStart : x.Start;
                var end = x.End > dayEnd ? dayEnd : x.End;
                return (Start: start, End: end, x.Activity);
            })
            .Where(x => x.End > x.Start)
            .OrderBy(x => x.Start)
            .ToList();
        var overlaps = new List<ActivityOverlap>();
        for (var i = 0; i < intervals.Count; i++)
        {
            for (var j = i + 1; j < intervals.Count; j++)
            {
                var first = intervals[i];
                var second = intervals[j];
                if (second.Start >= first.End) break;
                var start = first.Start > second.Start ? first.Start : second.Start;
                var end = first.End < second.End ? first.End : second.End;
                if (end > start)
                    overlaps.Add(new ActivityOverlap(first.Activity.Id, second.Activity.Id, start, end));
            }
        }
        return overlaps;
    }

    public ActivityTimeline BuildTimeline(
        StudyData data,
        DateOnly day,
        int slotMinutes = 30,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (slotMinutes is not (15 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(slotMinutes), "The timeline supports 15, 30 or 60 minute slots.");
        var zone = ResolveTimeZone(timeZoneId);
        var at = now ?? DateTimeOffset.Now;
        var dayStart = StartOfDay(day, zone);
        var dayEnd = StartOfDay(day.AddDays(1), zone);
        var totalSeconds = Math.Max(0, (int)Math.Floor((dayEnd - dayStart).TotalSeconds));
        var slotSeconds = slotMinutes * 60;
        var slotCount = Math.Max(1, (int)Math.Ceiling(totalSeconds / (double)slotSeconds));
        var intervals = GetRecordedIntervals(data, at).ToList();
        var slots = new List<ActivityTimelineSlot>(slotCount);
        var recorded = 0;

        for (var index = 0; index < slotCount; index++)
        {
            var start = dayStart.AddSeconds(index * slotSeconds);
            var end = start.AddSeconds(slotSeconds);
            if (end > dayEnd) end = dayEnd;
            if (end <= start) break;
            var inSlot = intervals
                .Where(x => x.End > start && x.Start < end)
                .Select(x => (
                    Start: x.Start < start ? start : x.Start,
                    End: x.End > end ? end : x.End,
                    x.Activity.Id))
                .ToList();
            var uniqueSeconds = UnionSeconds(inSlot.Select(x => (x.Start, x.End)));
            var overlaps = OverlapSeconds(inSlot.Select(x => (x.Start, x.End)));
            recorded += uniqueSeconds;
            slots.Add(new ActivityTimelineSlot(
                index,
                start,
                end,
                uniqueSeconds,
                overlaps > 0,
                inSlot.Select(x => x.Id).Distinct(StringComparer.Ordinal).ToArray()));
        }

        return new ActivityTimeline(
            day,
            slotMinutes,
            totalSeconds,
            recorded,
            Math.Max(0, totalSeconds - recorded),
            OverlapSeconds(intervals
                .Where(x => x.End > dayStart && x.Start < dayEnd)
                .Select(x =>
                {
                    var start = x.Start < dayStart ? dayStart : x.Start;
                    var end = x.End > dayEnd ? dayEnd : x.End;
                    return (start, end);
                })),
            slots);
    }

    public ActivityHeatmap BuildHeatmap(
        StudyData data,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");
        if (to.DayNumber - from.DayNumber > 366)
            throw new ArgumentOutOfRangeException(nameof(to), "The heatmap range is limited to 367 days.");
        var zone = ResolveTimeZone(timeZoneId);
        var cells = new List<ActivityHeatmapCell>();
        var intervals = GetRecordedIntervals(data, now ?? DateTimeOffset.Now).ToList();
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var dayStart = StartOfDay(day, zone);
            var dayEnd = StartOfDay(day.AddDays(1), zone);
            for (var hour = 0; hour < 24; hour++)
            {
                var start = dayStart.AddHours(hour);
                var end = start.AddHours(1);
                if (end > dayEnd) end = dayEnd;
                if (end <= start) continue;
                var inHour = intervals
                    .Where(x => x.End > start && x.Start < end)
                    .Select(x => (
                        Start: x.Start < start ? start : x.Start,
                        End: x.End > end ? end : x.End,
                        x.Activity.Id))
                    .ToList();
                cells.Add(new ActivityHeatmapCell(
                    day,
                    hour,
                    UnionSeconds(inHour.Select(x => (x.Start, x.End))),
                    inHour.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count()));
            }
        }
        return new ActivityHeatmap(from, to, cells);
    }

    public ActivitySummary Summarize(
        StudyData data,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");
        var zone = ResolveTimeZone(timeZoneId);
        var rangeStart = StartOfDay(from, zone);
        var rangeEnd = StartOfDay(to.AddDays(1), zone);
        var intervals = GetRecordedIntervals(data, now ?? DateTimeOffset.Now)
            .Where(x => x.End > rangeStart && x.Start < rangeEnd)
            .Select(x =>
            {
                var start = x.Start < rangeStart ? rangeStart : x.Start;
                var end = x.End > rangeEnd ? rangeEnd : x.End;
                return (Start: start, End: end, x.Activity);
            })
            .Where(x => x.End > x.Start)
            .ToList();
        var items = intervals.Select(x => x.Activity).DistinctBy(x => x.Id).ToList();
        var byType = intervals
            .GroupBy(x => x.Activity.Type)
            .ToDictionary(x => x.Key, x => UnionSeconds(x.Select(y => (y.Start, y.End))));
        var byCourse = intervals
            .Where(x => !string.IsNullOrWhiteSpace(x.Activity.CourseId))
            .GroupBy(x => x.Activity.CourseId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => UnionSeconds(x.Select(y => (y.Start, y.End))),
                StringComparer.Ordinal);
        var byTopic = intervals
            .Where(x => !string.IsNullOrWhiteSpace(x.Activity.SyllabusTopicId))
            .GroupBy(x => x.Activity.SyllabusTopicId, StringComparer.Ordinal)
            .ToDictionary(
                x => x.Key,
                x => UnionSeconds(x.Select(y => (y.Start, y.End))),
                StringComparer.Ordinal);
        return new ActivitySummary(
            from,
            to,
            UnionSeconds(intervals.Select(x => (x.Start, x.End))),
            items.Count,
            byType,
            byCourse)
        {
            OverlapSeconds = OverlapSeconds(intervals.Select(x => (x.Start, x.End))),
            ByTopic = byTopic
        };
    }

    private static void NormalizeAndValidate(StudyData data, StudyActivityData activity)
    {
        if (string.IsNullOrWhiteSpace(activity.Id))
            throw new ArgumentException("Activity id is required.", nameof(activity));
        if (!Enum.IsDefined(activity.Type) || !Enum.IsDefined(activity.Origin) || !Enum.IsDefined(activity.Status))
            throw new ArgumentException("Activity enums contain an invalid value.", nameof(activity));
        if (activity.StartedAt == default)
            throw new ArgumentException("Activity start is required.", nameof(activity));
        if (activity.Origin == StudyActivityOrigin.Timer && string.IsNullOrWhiteSpace(activity.SessionId))
            throw new ArgumentException("Timer activities must reference their persisted focus session.", nameof(activity));
        if (activity.EndedAt is { } end && end <= activity.StartedAt)
            throw new ArgumentException("Activity end must be after its start.", nameof(activity));
        if (activity.EndedAt is null && activity.Status is StudyActivityStatus.Completed)
            throw new ArgumentException("A completed activity needs an end timestamp.", nameof(activity));
        activity.Title = string.IsNullOrWhiteSpace(activity.Title)
            ? activity.Type.ToString()
            : activity.Title.Trim();
        if (activity.Title.Length > 400) throw new ArgumentException("Activity title is too long.", nameof(activity));
        activity.Note = activity.Note?.Trim() ?? string.Empty;
        activity.Result = activity.Result?.Trim() ?? string.Empty;
        if (activity.Note.Length > 12000 || activity.Result.Length > 2000)
            throw new ArgumentException("Activity text is too long.", nameof(activity));
        activity.TimeZoneId = string.IsNullOrWhiteSpace(activity.TimeZoneId)
            ? TimeZoneInfo.Local.Id
            : activity.TimeZoneId.Trim();
        _ = ResolveTimeZone(activity.TimeZoneId);
        if (activity.EffectiveSeconds < 0 || activity.EffectiveSeconds > 86400)
            throw new ArgumentOutOfRangeException(nameof(activity), "Effective seconds are outside the supported range.");
        if (activity.EndedAt is { } ended)
        {
            var interval = (int)Math.Floor((ended - activity.StartedAt).TotalSeconds);
            if (activity.EffectiveSeconds > interval)
                throw new ArgumentException("Effective seconds cannot exceed the recorded interval.", nameof(activity));
        }
        ValidateRating(activity.FocusRating, nameof(activity.FocusRating));
        ValidateRating(activity.DifficultyRating, nameof(activity.DifficultyRating));
        ValidateRating(activity.QualityRating, nameof(activity.QualityRating));
        ValidateRating(activity.EnergyRating, nameof(activity.EnergyRating));

        activity.Tags ??= new ObservableCollection<string>();
        var tags = activity.Tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (tags.Count > 20 || tags.Any(x => x.Length > 60))
            throw new ArgumentException("Activity tags are invalid.", nameof(activity));
        activity.Tags = new ObservableCollection<string>(tags);

        if (!string.IsNullOrWhiteSpace(activity.CourseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, activity.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity course does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.SyllabusTopicId) &&
            !data.SyllabusTopics.Any(x => string.Equals(x.Id, activity.SyllabusTopicId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity syllabus topic does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.TaskId) &&
            !data.StudyTasks.Any(x => string.Equals(x.Id, activity.TaskId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity task does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.MissionId) &&
            !data.StudyMissions.Any(x => string.Equals(x.Id, activity.MissionId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity mission does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.ExamId) &&
            !data.Exams.Any(x => string.Equals(x.Id, activity.ExamId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity exam does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.SessionId) &&
            !data.StudySessions.Any(x => string.Equals(x.Id, activity.SessionId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity session does not exist.", nameof(activity));
        if (!string.IsNullOrWhiteSpace(activity.CalendarEventId) &&
            !data.CalendarEvents.Any(x => string.Equals(x.Id, activity.CalendarEventId, StringComparison.Ordinal)))
            throw new ArgumentException("The activity calendar event does not exist.", nameof(activity));
    }

    private static void AddRevision(
        StudyData data,
        StudyActivityData activity,
        ActivityRevisionAction action,
        string reason,
        string? beforeJson,
        DateTimeOffset at)
    {
        data.ActivityRevisions.Add(new ActivityRevisionData
        {
            Id = Guid.NewGuid().ToString("N"),
            ActivityId = activity.Id,
            Action = action,
            Reason = reason,
            BeforeJson = beforeJson ?? string.Empty,
            AfterJson = JsonSerializer.Serialize(activity, RevisionJsonOptions),
            RevisedAt = at
        });
    }

    private static void CopyActivity(StudyActivityData target, StudyActivityData source)
    {
        target.Type = source.Type;
        target.Title = source.Title;
        target.CourseId = source.CourseId;
        target.SyllabusTopicId = source.SyllabusTopicId;
        target.TaskId = source.TaskId;
        target.MissionId = source.MissionId;
        target.ExamId = source.ExamId;
        target.SessionId = source.SessionId;
        target.CalendarEventId = source.CalendarEventId;
        target.StartedAt = source.StartedAt;
        target.EndedAt = source.EndedAt;
        target.TimeZoneId = source.TimeZoneId;
        target.EffectiveSeconds = source.EffectiveSeconds;
        target.Origin = source.Origin;
        target.Status = source.Status;
        target.Result = source.Result;
        target.FocusRating = source.FocusRating;
        target.DifficultyRating = source.DifficultyRating;
        target.QualityRating = source.QualityRating;
        target.EnergyRating = source.EnergyRating;
        target.Note = source.Note;
        target.Tags = source.Tags;
        target.IsPrivate = source.IsPrivate;
        target.Revision = source.Revision;
        target.UpdatedAt = source.UpdatedAt;
    }

    private static void ValidateRating(int? rating, string name)
    {
        if (rating is < 1 or > 5)
            throw new ArgumentOutOfRangeException(name, "Ratings must be between 1 and 5.");
    }

    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End, StudyActivityData Activity)> GetRecordedIntervals(
        StudyData data,
        DateTimeOffset now)
    {
        var sessions = data.StudySessions.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var intervals = new List<(DateTimeOffset Start, DateTimeOffset End, StudyActivityData Activity)>();
        foreach (var activity in data.StudyActivities.Where(x =>
                     !x.IsDeleted &&
                     (x.Status is StudyActivityStatus.Running or StudyActivityStatus.Completed ||
                      x.Status == StudyActivityStatus.Cancelled &&
                      x.Origin == StudyActivityOrigin.Timer && x.EffectiveSeconds > 0)))
        {
            StudySessionData? session = null;
            if (!string.IsNullOrWhiteSpace(activity.SessionId))
                sessions.TryGetValue(activity.SessionId, out session);
            foreach (var interval in GetFocusIntervals(activity, session, now))
                intervals.Add((interval.Start, interval.End, activity));
        }
        return intervals.OrderBy(x => x.Start).ToList();
    }

    /// <summary>
    /// A timer activity uses the session's focus segments, so pauses remain gaps.
    /// Legacy sessions without segments can only expose one inferred block at the
    /// start; their exact pause positions cannot be reconstructed from aggregates.
    /// </summary>
    private static IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> GetFocusIntervals(
        StudyActivityData activity,
        StudySessionData? session,
        DateTimeOffset now)
    {
        if (session is not null && session.Segments.Count > 0)
        {
            return session.Segments
                .Where(x => x.Type == StudySessionSegmentType.Focus)
                .Select(x => (x.StartedAt, End: x.EndedAt ?? now))
                .Where(x => x.End > x.StartedAt)
                .OrderBy(x => x.StartedAt)
                .Select(x => (x.StartedAt, x.End))
                .ToList();
        }

        var end = activity.EndedAt ?? now;
        if (activity.EffectiveSeconds > 0)
            end = activity.StartedAt.AddSeconds(activity.EffectiveSeconds) < end
                ? activity.StartedAt.AddSeconds(activity.EffectiveSeconds)
                : end;
        return end > activity.StartedAt
            ? new[] { (activity.StartedAt, end) }
            : Array.Empty<(DateTimeOffset, DateTimeOffset)>();
    }

    private static int UnionSeconds(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> source)
    {
        var intervals = source.OrderBy(x => x.Start).ToList();
        var total = 0;
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        foreach (var interval in intervals)
        {
            if (start is null)
            {
                start = interval.Start;
                end = interval.End;
                continue;
            }
            if (interval.Start <= end)
            {
                if (interval.End > end) end = interval.End;
                continue;
            }
            total += Math.Max(0, (int)Math.Floor((end!.Value - start.Value).TotalSeconds));
            start = interval.Start;
            end = interval.End;
        }
        if (start is not null)
            total += Math.Max(0, (int)Math.Floor((end!.Value - start.Value).TotalSeconds));
        return total;
    }

    private static int OverlapSeconds(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> source)
    {
        var events = new List<(DateTimeOffset At, int Delta)>();
        foreach (var interval in source)
        {
            if (interval.End <= interval.Start) continue;
            events.Add((interval.Start, 1));
            events.Add((interval.End, -1));
        }
        events.Sort((a, b) => a.At == b.At ? a.Delta.CompareTo(b.Delta) : a.At.CompareTo(b.At));
        var active = 0;
        var total = 0;
        DateTimeOffset? previous = null;
        foreach (var current in events)
        {
            if (previous is not null && active > 1)
                total += Math.Max(0, (int)Math.Floor((current.At - previous.Value).TotalSeconds));
            active += current.Delta;
            previous = current.At;
        }
        return total;
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue);
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
    }

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, zone).DateTime);

    private static bool InRange(DateOnly value, DateOnly from, DateOnly to) => value >= from && value <= to;

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Invalid time zone '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
    }
}

/// <summary>
/// Mission rules: a mission contains checklist steps, never hidden tasks. A
/// mission is only complete when every step is complete; cancellation is not a
/// punishment and does not invent an XP/score value.
/// </summary>
public sealed class StudyMissionService
{
    public StudyMissionData Add(StudyData data, StudyMissionData mission, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(mission);
        var at = now ?? DateTimeOffset.Now;
        if (string.IsNullOrWhiteSpace(mission.Id)) mission.Id = Guid.NewGuid().ToString("N");
        if (data.StudyMissions.Any(x => string.Equals(x.Id, mission.Id, StringComparison.Ordinal)))
            throw new ArgumentException($"Mission '{mission.Id}' already exists.", nameof(mission));
        if (string.IsNullOrWhiteSpace(mission.Title))
            throw new ArgumentException("Mission title is required.", nameof(mission));
        if (!Enum.IsDefined(mission.Period) || !Enum.IsDefined(mission.Status))
            throw new ArgumentException("Mission enums contain an invalid value.", nameof(mission));
        if (mission.Difficulty is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(mission), "Mission difficulty must be between 1 and 5.");
        if (mission.StartsAt == default) mission.StartsAt = at;
        if (mission.DueAt is { } due && due < mission.StartsAt)
            throw new ArgumentException("Mission due date precedes its start.", nameof(mission));
        if (!string.IsNullOrWhiteSpace(mission.CourseId) &&
            !data.Courses.Any(x => string.Equals(x.Id, mission.CourseId, StringComparison.Ordinal)))
            throw new ArgumentException("The mission course does not exist.", nameof(mission));
        if (!string.IsNullOrWhiteSpace(mission.SyllabusTopicId) &&
            !data.SyllabusTopics.Any(x => string.Equals(x.Id, mission.SyllabusTopicId, StringComparison.Ordinal)))
            throw new ArgumentException("The mission syllabus topic does not exist.", nameof(mission));

        mission.Title = mission.Title.Trim();
        mission.Description = mission.Description?.Trim() ?? string.Empty;
        mission.Reward = mission.Reward?.Trim() ?? string.Empty;
        mission.Source = string.IsNullOrWhiteSpace(mission.Source) ? "manual" : mission.Source.Trim();
        mission.Status = StudyMissionStatus.Planned;
        mission.CompletedAt = null;
        mission.CreatedAt = mission.CreatedAt == default ? at : mission.CreatedAt;
        mission.UpdatedAt = at;
        mission.Steps ??= new ObservableCollection<MissionStepData>();
        NormalizeSteps(mission, at);
        data.StudyMissions.Add(mission);
        return mission;
    }

    public MissionStepData AddStep(
        StudyData data,
        string missionId,
        string title,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var mission = Find(data, missionId);
        if (mission.Status == StudyMissionStatus.Cancelled)
            throw new InvalidOperationException("A cancelled mission cannot receive new steps.");
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Mission step title is required.", nameof(title));
        var at = now ?? DateTimeOffset.Now;
        var step = new MissionStepData
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title.Trim(),
            Position = mission.Steps.Count,
            IsCompleted = false
        };
        mission.Steps.Add(step);
        mission.Status = mission.Status == StudyMissionStatus.Completed
            ? StudyMissionStatus.InProgress
            : mission.Status;
        mission.CompletedAt = null;
        mission.UpdatedAt = at;
        return step;
    }

    public void SetStepCompleted(
        StudyData data,
        string missionId,
        string stepId,
        bool completed,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var mission = Find(data, missionId);
        if (mission.Status == StudyMissionStatus.Cancelled)
            throw new InvalidOperationException("A cancelled mission cannot be changed.");
        var step = mission.Steps.FirstOrDefault(x => string.Equals(x.Id, stepId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"Mission step '{stepId}' does not exist.", nameof(stepId));
        var at = now ?? DateTimeOffset.Now;
        step.IsCompleted = completed;
        step.CompletedAt = completed ? at : null;
        mission.Status = mission.Steps.Count > 0 && mission.Steps.All(x => x.IsCompleted)
            ? StudyMissionStatus.Completed
            : mission.Steps.Any(x => x.IsCompleted)
                ? StudyMissionStatus.InProgress
                : StudyMissionStatus.Planned;
        mission.CompletedAt = mission.Status == StudyMissionStatus.Completed ? at : null;
        mission.UpdatedAt = at;
    }

    public void Complete(StudyData data, string missionId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var mission = Find(data, missionId);
        if (mission.Status == StudyMissionStatus.Cancelled)
            throw new InvalidOperationException("A cancelled mission cannot be completed.");
        if (mission.Steps.Count == 0)
            throw new InvalidOperationException("A mission needs at least one step before it can be completed.");
        var at = now ?? DateTimeOffset.Now;
        foreach (var step in mission.Steps)
        {
            step.IsCompleted = true;
            step.CompletedAt ??= at;
        }
        mission.Status = StudyMissionStatus.Completed;
        mission.CompletedAt = at;
        mission.UpdatedAt = at;
    }

    public void Cancel(StudyData data, string missionId, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var mission = Find(data, missionId);
        if (mission.Status == StudyMissionStatus.Completed)
            throw new InvalidOperationException("A completed mission cannot be cancelled.");
        mission.Status = StudyMissionStatus.Cancelled;
        mission.CompletedAt = null;
        mission.UpdatedAt = now ?? DateTimeOffset.Now;
    }

    public IReadOnlyList<StudyMissionData> GetForPeriod(
        StudyData data,
        StudyMissionPeriod period,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from) throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");
        var zone = TimeZoneInfo.Local;
        return data.StudyMissions
            .Where(x => x.Period == period)
            .Where(x =>
            {
                var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(x.StartsAt, zone).DateTime);
                return local >= from && local <= to;
            })
            .OrderBy(x => x.DueAt)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static StudyMissionData Find(StudyData data, string missionId) =>
        data.StudyMissions.FirstOrDefault(x => string.Equals(x.Id, missionId, StringComparison.Ordinal))
        ?? throw new ArgumentException($"Mission '{missionId}' does not exist.", nameof(missionId));

    private static void NormalizeSteps(StudyMissionData mission, DateTimeOffset at)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var steps = mission.Steps.ToList();
        mission.Steps.Clear();
        for (var index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (string.IsNullOrWhiteSpace(step.Id)) step.Id = Guid.NewGuid().ToString("N");
            if (!seen.Add(step.Id))
                throw new ArgumentException($"Duplicate mission step id '{step.Id}'.", nameof(mission));
            if (string.IsNullOrWhiteSpace(step.Title))
                throw new ArgumentException("A mission step needs a title.", nameof(mission));
            if (step.IsCompleted && step.CompletedAt is null) step.CompletedAt = at;
            step.Position = index;
            mission.Steps.Add(step);
        }
        if (mission.Status == StudyMissionStatus.Completed && mission.Steps.Count > 0 &&
            mission.Steps.All(x => x.IsCompleted))
            mission.CompletedAt ??= at;
    }
}
