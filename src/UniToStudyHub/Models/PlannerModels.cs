using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

public enum StudyPlanStatus
{
    Draft = 0,
    Active = 1,
    InsufficientTime = 2,
    Completed = 3,
    Archived = 4
}

public enum PlannedSessionStatus
{
    None = 0,
    Scheduled = 1,
    Completed = 2,
    Missed = 3,
    NeedsAttention = 4,
    Cancelled = 5
}

/// <summary>
/// A user-configured weekly availability window. Times are local wall-clock
/// times; the planner converts them with the configured time zone and never
/// assumes that a day is exactly 24 hours long.
/// </summary>
public sealed class PlannerAvailabilityWindowData
{
    public DayOfWeek DayOfWeek { get; set; } = DayOfWeek.Monday;
    public TimeOnly StartsAt { get; set; } = new(18, 0);
    public TimeOnly EndsAt { get; set; } = new(20, 0);
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Planner preferences are explicit. An empty availability list means that the
/// user has not configured any free time: the planner reports this limitation
/// instead of inventing a study schedule.
/// </summary>
public sealed class PlannerSettingsData
{
    public int PreferredSessionMinutes { get; set; } = 50;
    public int MinimumSessionMinutes { get; set; } = 25;
    public int BreakBetweenSessionsMinutes { get; set; } = 10;
    public int MaxSessionsPerDay { get; set; } = 3;
    public int ReviewReservePercent { get; set; } = 15;
    public int ReviewReserveDays { get; set; } = 2;
    public bool IncludeWeekends { get; set; } = true;
    public bool AutoRescheduleMissed { get; set; } = true;
    public ObservableCollection<PlannerAvailabilityWindowData> Availability { get; set; } = new();
}

/// <summary>
/// A generated study plan. Sessions are persisted as CalendarEvents in the same
/// SQLite source of truth, linked by <see cref="Id"/>; the plan keeps the
/// assumptions, deficit and warnings needed to explain a failed allocation.
/// </summary>
public sealed class StudyPlanData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string CourseId { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;
    public string ExamTitle { get; set; } = string.Empty;
    public DateTime ExamDate { get; set; }
    public int RequiredMinutes { get; set; }
    public int ReviewReserveMinutes { get; set; }
    public int ScheduledMinutes { get; set; }
    public StudyPlanStatus Status { get; set; } = StudyPlanStatus.Draft;
    public string Explanation { get; set; } = string.Empty;
    public ObservableCollection<string> Warnings { get; set; } = new();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// Append-only history of a moved planner session. A move is reversible while
/// its previous slot is still free; the history is retained even afterwards.
/// </summary>
public sealed class PlannerRescheduleData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string PlanId { get; set; } = string.Empty;
    public string CalendarEventId { get; set; } = string.Empty;
    public DateTimeOffset PreviousStartsAt { get; set; }
    public DateTimeOffset PreviousEndsAt { get; set; }
    public DateTimeOffset NewStartsAt { get; set; }
    public DateTimeOffset NewEndsAt { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset MovedAt { get; set; } = DateTimeOffset.Now;
    public bool IsReversed { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
}

/// <summary>
/// Input for a deterministic plan. Topics are optional: when omitted, all
/// non-archived topics of the course are considered. A missing estimate is a
/// warning, never a silently invented duration.
/// </summary>
public sealed class PlannerRequest
{
    public string CourseId { get; set; } = string.Empty;
    public string ExamId { get; set; } = string.Empty;
    public DateTime? ExamDate { get; set; }
    public IReadOnlyCollection<string> TopicIds { get; set; } = Array.Empty<string>();
    public bool ReplaceExistingPlan { get; set; }
}

public sealed record PlannerResult(
    StudyPlanData Plan,
    IReadOnlyList<CalendarEvent> Sessions,
    int DeficitMinutes)
{
    public bool IsFeasible => Plan.Status is StudyPlanStatus.Active or StudyPlanStatus.Completed;
}

public sealed record PlannerRescheduleProposal(
    CalendarEvent Session,
    DateTimeOffset PreviousStartsAt,
    DateTimeOffset PreviousEndsAt,
    DateTimeOffset? NewStartsAt,
    DateTimeOffset? NewEndsAt,
    string Reason);

public sealed record PlannerDashboard(
    DateOnly Day,
    IReadOnlyList<CalendarEvent> TodayEvents,
    IReadOnlyList<CalendarEvent> NextEvents,
    IReadOnlyList<CalendarEvent> NextStudySessions,
    Exam? NextExam,
    IReadOnlyList<CoursePlanProgress> CourseProgress,
    int WeeklyRecordedMinutes,
    IReadOnlyList<string> Warnings);

public sealed record CoursePlanProgress(
    string CourseId,
    string CourseName,
    int CompletedTopics,
    int TotalTopics,
    int EstimatedRemainingMinutes,
    int ScheduledPlannerMinutes,
    int CompletedPlannerMinutes)
{
    public decimal CompletionPercent => TotalTopics == 0
        ? 0
        : Math.Round((decimal)CompletedTopics / TotalTopics * 100, 1);
}
