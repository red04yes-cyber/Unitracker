using System.Collections.ObjectModel;

namespace UniToStudyHub.Models;

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

public enum PomodoroTimerStatus
{
    Running,
    Paused,
    Completed
}

/// <summary>
/// User-visible Pomodoro configuration. Durations are stored in minutes; the
/// timer itself stores seconds and absolute timestamps.
/// </summary>
public sealed class PomodoroSettingsData
{
    public int FocusMinutes { get; set; } = 25;
    public int ShortBreakMinutes { get; set; } = 5;
    public int LongBreakMinutes { get; set; } = 15;
    public int SessionsBeforeLongBreak { get; set; } = 4;
}

/// <summary>
/// The single active Pomodoro timer. It is persisted, so a restart can rebuild
/// the phase from timestamps instead of trusting an in-memory DispatcherTimer.
/// </summary>
public sealed class PomodoroTimerData
{
    public string Id { get; set; } = "current";
    public string SessionId { get; set; } = string.Empty;
    public PomodoroPhase Phase { get; set; } = PomodoroPhase.Focus;
    public PomodoroTimerStatus Status { get; set; } = PomodoroTimerStatus.Running;

    /// <summary>Wall-clock start of the current phase, used for cycle history.</summary>
    public DateTimeOffset PhaseWindowStartedAt { get; set; }

    /// <summary>Start of the current active interval; changes after a pause/resume.</summary>
    public DateTimeOffset PhaseStartedAt { get; set; }

    /// <summary>Absolute end while running. Null while paused or completed.</summary>
    public DateTimeOffset? PhaseEndsAt { get; set; }

    /// <summary>Remaining seconds captured when the timer is paused.</summary>
    public int RemainingSeconds { get; set; }

    /// <summary>Active seconds accumulated in this phase before the current interval.</summary>
    public int ActiveSeconds { get; set; }

    public int FocusesCompletedInSet { get; set; }
    public int CycleNumber { get; set; } = 1;
    public string CurrentCycleId { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// History of a focus phase and its following break. The row is created when a
/// focus phase ends and completed when the break ends; <see cref="IsCompleted"/>
/// distinguishes a complete cycle from a skipped or interrupted one.
/// </summary>
public sealed class PomodoroCycleData
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SessionId { get; set; } = string.Empty;
    public int CycleNumber { get; set; }
    public DateTimeOffset FocusStartedAt { get; set; }
    public DateTimeOffset FocusEndedAt { get; set; }
    public int FocusSeconds { get; set; }
    public PomodoroPhase BreakType { get; set; } = PomodoroPhase.ShortBreak;
    public DateTimeOffset? BreakStartedAt { get; set; }
    public DateTimeOffset? BreakEndedAt { get; set; }
    public int BreakSeconds { get; set; }
    public bool WasSkipped { get; set; }
    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}
