using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

/// <summary>
/// The kind of work represented in the activity ledger. A session is not a
/// separate kind of work: an activity linked to a session describes the same
/// real interval and must not be counted twice.
/// </summary>
public enum StudyActivityType
{
    Study,
    Review,
    Quiz,
    Lecture,
    Task,
    Mission,
    Simulation,
    Exercise,
    Reading,
    Other
}

public enum StudyActivityOrigin
{
    Manual,
    Timer,
    Calendar,
    Import
}

public enum StudyActivityStatus
{
    Planned,
    Running,
    Completed,
    Cancelled
}

public enum StudyMissionPeriod
{
    Daily,
    Weekly,
    Custom
}

public enum StudyMissionStatus
{
    Planned,
    InProgress,
    Completed,
    Cancelled
}

public enum ActivityRevisionAction
{
    Created,
    Updated,
    Deleted
}

/// <summary>
/// One recorded interval of study-related work. Manual entries are first-class:
/// the application cannot know what happened outside the app. The timezone and
/// offset are retained so local-day reports remain explainable across DST.
/// </summary>
public sealed class StudyActivityData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public StudyActivityType Type { get; set; } = StudyActivityType.Study;
    public string Title { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string MissionId { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;

    /// <summary>Link to the persisted focus session, when this interval came from the timer.</summary>
    public string SessionId { get; set; } = string.Empty;
    public string CalendarEventId { get; set; } = string.Empty;

    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? EndedAt { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;

    /// <summary>
    /// Actual time credited to the activity. It can be shorter than the interval
    /// (a manual correction); it is never inferred as "planned" time.
    /// </summary>
    public int EffectiveSeconds { get; set; }

    public StudyActivityOrigin Origin { get; set; } = StudyActivityOrigin.Manual;
    public StudyActivityStatus Status { get; set; } = StudyActivityStatus.Completed;
    public string Result { get; set; } = string.Empty;

    /// <summary>Optional subjective ratings, each on a 1–5 scale.</summary>
    public int? FocusRating { get; set; }
    public int? DifficultyRating { get; set; }
    public int? QualityRating { get; set; }
    public int? EnergyRating { get; set; }

    public string Note { get; set; } = string.Empty;
    public ObservableCollection<string> Tags { get; set; } = new();

    /// <summary>Private notes are excluded from exports unless explicitly requested.</summary>
    public bool IsPrivate { get; set; }

    public int Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Soft deletion keeps an auditable revision instead of silently erasing history.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsDeleted => DeletedAt is not null;
}

/// <summary>
/// A daily/weekly mission is a small, user-visible container of steps. It is
/// deliberately not a second task table: steps are checklist items, while
/// tasks remain actionable units that can be scheduled and completed.
/// </summary>
/// <summary>
/// Progress for one actionable task, derived from unique recorded activity
/// intervals rather than planned calendar blocks. Overlapping timer/manual
/// entries are counted once; an estimate of zero is reported as unconfigured.
/// </summary>
public sealed record TaskProgressSnapshot(
    string TaskId,
    int EstimatedMinutes,
    int RecordedSeconds,
    int RemainingSeconds,
    int ProgressPercent,
    bool IsOverEstimate)
{
    public bool HasEstimate => EstimatedMinutes > 0;
    public int RecordedMinutes => (int)Math.Ceiling(RecordedSeconds / 60d);
    public int RemainingMinutes => (int)Math.Ceiling(RemainingSeconds / 60d);
}

public sealed class StudyMissionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string CourseId { get; set; } = string.Empty;
    public string SyllabusTopicId { get; set; } = string.Empty;
    public StudyMissionPeriod Period { get; set; } = StudyMissionPeriod.Daily;
    public DateTimeOffset StartsAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? DueAt { get; set; }
    public int Difficulty { get; set; } = 3;
    public StudyMissionStatus Status { get; set; } = StudyMissionStatus.Planned;

    /// <summary>A voluntary reward, not a punishment or an invented score.</summary>
    public string Reward { get; set; } = string.Empty;
    public string Source { get; set; } = "manual";
    public ObservableCollection<MissionStepData> Steps { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? CompletedAt { get; set; }

    public int CompletedStepCount => Steps.Count(x => x.IsCompleted);
    public bool IsComplete => Steps.Count > 0 && Steps.All(x => x.IsCompleted);
}

public sealed class MissionStepData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public int Position { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Auditable before/after snapshots for an activity correction or deletion.
/// The snapshots stay in the local database and are not included in exports.
/// </summary>
public sealed class ActivityRevisionData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ActivityId { get; set; } = string.Empty;
    public ActivityRevisionAction Action { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string BeforeJson { get; set; } = string.Empty;
    public string AfterJson { get; set; } = string.Empty;
    public DateTimeOffset RevisedAt { get; set; } = DateTimeOffset.Now;
}
