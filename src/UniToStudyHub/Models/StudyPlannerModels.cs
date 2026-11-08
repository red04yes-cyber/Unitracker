using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

/// <summary>
/// A recurring local-time availability window. Windows never cross midnight;
/// use two windows when availability spans two local dates.
/// </summary>
public sealed record StudyAvailabilityWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>
/// Explicit constraints used to produce a study plan. Availability is never
/// inferred from an empty calendar: the user must provide windows.
/// </summary>
public sealed class StudyPlannerOptions
{
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public string TimeZoneId { get; init; } = TimeZoneInfo.Local.Id;
    public IReadOnlyList<StudyAvailabilityWindow> WeeklyAvailability { get; init; } =
        Array.Empty<StudyAvailabilityWindow>();
    public int? WeeklyStudyMinutes { get; init; }
    public int? MaxDailyStudyMinutes { get; init; }
    public int PreferredBlockMinutes { get; init; } = 50;
    public int MinimumBlockMinutes { get; init; } = 15;
    public int BreakMinutes { get; init; } = 10;
    public int MaxBlocksPerDay { get; init; } = 4;
    public bool ReplaceExistingGeneratedBlocks { get; init; }
}

/// <summary>
/// A proposed block. It is indicative until explicitly applied to the
/// calendar; it does not start a timer or complete its source task.
/// </summary>
public sealed record StudyPlanBlock(
    string TaskId,
    string Title,
    string CourseId,
    string CourseName,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int PlannedMinutes,
    int Priority,
    DateTimeOffset? DueAt);

public sealed record StudyPlanUnscheduledTask(
    string TaskId,
    string Title,
    int RemainingMinutes,
    string Reason);

public sealed record StudyPlanPreview(
    DateOnly From,
    DateOnly To,
    string TimeZoneId,
    DateTimeOffset GeneratedAt,
    int WeeklyBudgetMinutes,
    int DailyBudgetMinutes,
    StudyPlannerOptions Options,
    IReadOnlyList<StudyPlanBlock> Blocks,
    IReadOnlyList<StudyPlanUnscheduledTask> UnscheduledTasks,
    IReadOnlyList<string> Warnings)
{
    public int ScheduledMinutes => Blocks.Sum(x => x.PlannedMinutes);
    public int ScheduledBlockCount => Blocks.Count;
}

public sealed record StudyPlanApplyResult(
    int Added,
    int AlreadyPresent,
    int Conflicts,
    int Skipped,
    IReadOnlyList<string> Warnings)
{
    public int TotalProposed => Added + AlreadyPresent + Conflicts + Skipped;
}
