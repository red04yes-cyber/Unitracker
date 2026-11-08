using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record PomodoroSnapshot(
    PomodoroPhase Phase,
    PomodoroTimerStatus Status,
    int ElapsedSeconds,
    int RemainingSeconds,
    bool IsExpired,
    int FocusesCompletedInSet,
    int CycleNumber,
    string SessionId);

public sealed record PomodoroTransition(
    PomodoroPhase Phase,
    PomodoroTimerStatus Status,
    DateTimeOffset At);

public sealed record PomodoroReconcileResult(
    IReadOnlyList<PomodoroTransition> Transitions,
    bool AwaitingNextFocus)
{
    public bool HasTransitions => Transitions.Count > 0;
}

/// <summary>
/// Persisted Pomodoro state machine. A timer is reconstructed from timestamps;
/// it never owns a DispatcherTimer. When the app is closed during a focus phase,
/// the focus ends at its stored deadline and the following break is created. A
/// new focus is deliberately not started automatically after a break: doing so
/// while the app is closed would credit study time nobody performed.
/// </summary>
public sealed class PomodoroService(FocusSessionService focus, ActivityLedgerService ledger)
{
    public PomodoroSettingsData UpdateSettings(
        StudyData data,
        int focusMinutes,
        int shortBreakMinutes,
        int longBreakMinutes,
        int sessionsBeforeLongBreak)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.PomodoroTimer is { Status: PomodoroTimerStatus.Running or PomodoroTimerStatus.Paused })
            throw new InvalidOperationException("Timer settings cannot change during an active phase.");
        if (focusMinutes is < 1 or > 1440 ||
            shortBreakMinutes is < 1 or > 240 ||
            longBreakMinutes is < 1 or > 240 ||
            sessionsBeforeLongBreak is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(focusMinutes), "Pomodoro settings are outside the supported range.");

        data.PomodoroSettings = new PomodoroSettingsData
        {
            FocusMinutes = focusMinutes,
            ShortBreakMinutes = shortBreakMinutes,
            LongBreakMinutes = longBreakMinutes,
            SessionsBeforeLongBreak = sessionsBeforeLongBreak
        };
        return data.PomodoroSettings;
    }

    public PomodoroTimerData Start(
        StudyData data,
        string? courseId,
        string topic,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var active = GetActive(data);
        if (active is not null)
            throw new InvalidOperationException("A Pomodoro phase is already running or paused.");
        CloseCompletedRun(data, at);

        var settings = data.PomodoroSettings ??= new PomodoroSettingsData();
        var session = focus.Start(data, courseId, topic, settings.FocusMinutes, at);
        session.PlannedMinutes = settings.FocusMinutes;
        var duration = DurationSeconds(settings, PomodoroPhase.Focus);
        var timer = new PomodoroTimerData
        {
            SessionId = session.Id,
            Phase = PomodoroPhase.Focus,
            Status = PomodoroTimerStatus.Running,
            PhaseWindowStartedAt = at,
            PhaseStartedAt = at,
            PhaseEndsAt = at.AddSeconds(duration),
            RemainingSeconds = duration,
            CycleNumber = 1,
            UpdatedAt = at
        };
        data.PomodoroTimer = timer;
        Sync(data, timer, at);
        return timer;
    }

    public PomodoroTimerData? GetActive(StudyData data) =>
        data.PomodoroTimer is { Status: PomodoroTimerStatus.Running or PomodoroTimerStatus.Paused } timer
            ? timer
            : null;

    public PomodoroSnapshot Snapshot(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var timer = data.PomodoroTimer
            ?? throw new InvalidOperationException("No Pomodoro timer has been started.");
        var at = now ?? DateTimeOffset.Now;
        var duration = DurationSeconds(data.PomodoroSettings, timer.Phase);
        var elapsed = timer.Status == PomodoroTimerStatus.Completed
            ? (timer.Phase == PomodoroPhase.Focus ? timer.ActiveSeconds : timer.ActiveSeconds)
            : ElapsedActive(timer, duration, at);
        var remaining = timer.Status switch
        {
            PomodoroTimerStatus.Paused => timer.RemainingSeconds,
            PomodoroTimerStatus.Completed => 0,
            _ => timer.PhaseEndsAt is { } ends ? Math.Max(0, (int)Math.Floor((ends - at).TotalSeconds)) : 0
        };
        return new PomodoroSnapshot(
            timer.Phase,
            timer.Status,
            Math.Max(0, elapsed),
            Math.Max(0, remaining),
            remaining == 0,
            timer.FocusesCompletedInSet,
            timer.CycleNumber,
            timer.SessionId);
    }

    public PomodoroTimerData Pause(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var timer = RequireTimer(data);
        if (timer.Status != PomodoroTimerStatus.Running)
            throw new InvalidOperationException("Only a running Pomodoro phase can be paused.");
        var at = now ?? DateTimeOffset.Now;
        if (timer.PhaseEndsAt is null || at >= timer.PhaseEndsAt)
            throw new InvalidOperationException("The phase has already elapsed; reconcile it before pausing.");

        timer.ActiveSeconds = ElapsedActive(timer, DurationSeconds(data.PomodoroSettings, timer.Phase), at);
        timer.RemainingSeconds = Math.Max(1, DurationSeconds(data.PomodoroSettings, timer.Phase) - timer.ActiveSeconds);
        timer.PhaseEndsAt = null;
        timer.Status = PomodoroTimerStatus.Paused;
        timer.UpdatedAt = at;
        if (timer.Phase == PomodoroPhase.Focus)
            focus.Pause(data, timer.SessionId, at);
        Sync(data, timer, at);
        return timer;
    }

    public PomodoroTimerData Resume(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var timer = RequireTimer(data);
        if (timer.Status != PomodoroTimerStatus.Paused)
            throw new InvalidOperationException("Only a paused Pomodoro phase can be resumed.");
        var at = now ?? DateTimeOffset.Now;
        if (timer.RemainingSeconds <= 0)
            throw new InvalidOperationException("A paused phase with no remaining time cannot be resumed.");
        timer.Status = PomodoroTimerStatus.Running;
        timer.PhaseStartedAt = at;
        timer.PhaseEndsAt = at.AddSeconds(timer.RemainingSeconds);
        timer.UpdatedAt = at;
        if (timer.Phase == PomodoroPhase.Focus)
            focus.Resume(data, timer.SessionId, at);
        Sync(data, timer, at);
        return timer;
    }

    /// <summary>Starts the next focus after a completed break, on the same linked session.</summary>
    public PomodoroTimerData StartNextFocus(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var timer = RequireTimer(data);
        if (timer.Status != PomodoroTimerStatus.Completed)
            throw new InvalidOperationException("The current Pomodoro phase is not complete.");
        if (timer.Phase == PomodoroPhase.Focus)
            throw new InvalidOperationException("The completed focus phase must be followed by a break.");
        var at = now ?? DateTimeOffset.Now;
        var session = FindSession(data, timer.SessionId);
        if (session.State != StudySessionState.Paused)
            throw new InvalidOperationException("The linked focus session is not paused and cannot start a new focus.");
        focus.Resume(data, session.Id, at);
        var duration = DurationSeconds(data.PomodoroSettings, PomodoroPhase.Focus);
        timer.Phase = PomodoroPhase.Focus;
        timer.Status = PomodoroTimerStatus.Running;
        timer.PhaseWindowStartedAt = at;
        timer.PhaseStartedAt = at;
        timer.PhaseEndsAt = at.AddSeconds(duration);
        timer.RemainingSeconds = duration;
        timer.ActiveSeconds = 0;
        timer.CurrentCycleId = string.Empty;
        timer.UpdatedAt = at;
        Sync(data, timer, at);
        return timer;
    }

    public PomodoroTimerData CompletePhase(StudyData data, DateTimeOffset? now = null) =>
        FinishPhase(data, skipped: false, now);

    public PomodoroTimerData SkipPhase(StudyData data, DateTimeOffset? now = null) =>
        FinishPhase(data, skipped: true, now);

    public PomodoroReconcileResult Reconcile(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var transitions = new List<PomodoroTransition>();
        var guard = 0;
        while (data.PomodoroTimer is { Status: PomodoroTimerStatus.Running, PhaseEndsAt: { } ends } timer &&
               ends <= at && guard++ < 8)
        {
            var phase = timer.Phase;
            if (phase == PomodoroPhase.Focus)
            {
                FinishFocusAt(data, timer, ends, skipped: false);
                transitions.Add(new PomodoroTransition(phase, timer.Status, ends));
                continue;
            }

            FinishBreakAt(data, timer, ends, skipped: false);
            transitions.Add(new PomodoroTransition(phase, timer.Status, ends));
            break;
        }
        return new PomodoroReconcileResult(
            transitions,
            data.PomodoroTimer is { Status: PomodoroTimerStatus.Completed, Phase: not PomodoroPhase.Focus });
    }

    public void Reset(StudyData data, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var timer = data.PomodoroTimer;
        if (timer is null) return;
        if (timer.Status == PomodoroTimerStatus.Running &&
            timer.PhaseEndsAt is { } deadline && deadline <= at)
            Reconcile(data, at);
        timer = data.PomodoroTimer;
        if (timer is null) return;
        if (timer.CurrentCycleId is { Length: > 0 } cycleId &&
            data.PomodoroCycles.FirstOrDefault(x => string.Equals(x.Id, cycleId, StringComparison.Ordinal)) is { } cycle &&
            !cycle.IsCompleted)
        {
            cycle.BreakEndedAt ??= at;
            if (cycle.BreakStartedAt is { } breakStart && cycle.BreakEndedAt > breakStart)
                cycle.BreakSeconds = (int)Math.Floor((cycle.BreakEndedAt.Value - breakStart).TotalSeconds);
            cycle.CompletedAt = null;
        }

        var session = data.StudySessions.FirstOrDefault(x =>
            string.Equals(x.Id, timer.SessionId, StringComparison.Ordinal));
        if (session is not null && session.State is StudySessionState.Running or StudySessionState.Paused)
            focus.Finish(data, session.Id, "Pomodoro interrotto", at);
        if (session is not null)
            ledger.SyncFocusSession(data, session, at);
        data.PomodoroTimer = null;
    }

    private PomodoroTimerData FinishPhase(StudyData data, bool skipped, DateTimeOffset? now)
    {
        ArgumentNullException.ThrowIfNull(data);
        var timer = RequireTimer(data);
        if (timer.Status != PomodoroTimerStatus.Running)
            throw new InvalidOperationException("Only a running Pomodoro phase can be completed.");
        var at = now ?? DateTimeOffset.Now;
        if (timer.PhaseEndsAt is { } deadline && deadline <= at)
        {
            // The phase already ended: reconcile it at the persisted deadline
            // instead of crediting time until the late click. The next break is
            // opened by Reconcile and no focus is started automatically.
            Reconcile(data, at);
            return timer;
        }
        if (timer.Phase == PomodoroPhase.Focus)
            FinishFocusAt(data, timer, at, skipped);
        else
            FinishBreakAt(data, timer, at, skipped);
        return timer;
    }

    private void FinishFocusAt(StudyData data, PomodoroTimerData timer, DateTimeOffset at, bool skipped)
    {
        var effectiveAt = timer.PhaseEndsAt is { } deadline && at > deadline ? deadline : at;
        var settings = data.PomodoroSettings;
        var active = ElapsedActive(timer, DurationSeconds(settings, PomodoroPhase.Focus), effectiveAt);
        var session = FindSession(data, timer.SessionId);
        if (session.State == StudySessionState.Running)
            focus.Pause(data, session.Id, effectiveAt);

        if (!skipped)
            timer.FocusesCompletedInSet++;
        var breakType = !skipped && timer.FocusesCompletedInSet >= settings.SessionsBeforeLongBreak
            ? PomodoroPhase.LongBreak
            : PomodoroPhase.ShortBreak;
        var cycle = new PomodoroCycleData
        {
            SessionId = timer.SessionId,
            CycleNumber = timer.CycleNumber,
            FocusStartedAt = timer.PhaseWindowStartedAt,
            FocusEndedAt = effectiveAt,
            FocusSeconds = active,
            BreakType = breakType,
            BreakStartedAt = effectiveAt,
            WasSkipped = skipped
        };
        data.PomodoroCycles.Add(cycle);
        timer.CurrentCycleId = cycle.Id;
        timer.CycleNumber++;
        timer.ActiveSeconds = 0;
        timer.Phase = breakType;
        timer.PhaseWindowStartedAt = effectiveAt;
        timer.PhaseStartedAt = effectiveAt;
        timer.RemainingSeconds = DurationSeconds(settings, breakType);
        timer.PhaseEndsAt = effectiveAt.AddSeconds(timer.RemainingSeconds);
        timer.Status = PomodoroTimerStatus.Running;
        timer.UpdatedAt = effectiveAt;
        Sync(data, timer, effectiveAt);
    }

    private void FinishBreakAt(StudyData data, PomodoroTimerData timer, DateTimeOffset at, bool skipped)
    {
        var effectiveAt = timer.PhaseEndsAt is { } deadline && at > deadline ? deadline : at;
        var active = ElapsedActive(timer, DurationSeconds(data.PomodoroSettings, timer.Phase), effectiveAt);
        if (timer.CurrentCycleId is { Length: > 0 } cycleId &&
            data.PomodoroCycles.FirstOrDefault(x => string.Equals(x.Id, cycleId, StringComparison.Ordinal)) is { } cycle)
        {
            cycle.BreakEndedAt = effectiveAt;
            cycle.BreakSeconds = active;
            cycle.IsCompleted = !skipped && !cycle.WasSkipped;
            cycle.CompletedAt = cycle.IsCompleted ? effectiveAt : null;
        }
        if (timer.Phase == PomodoroPhase.LongBreak)
            timer.FocusesCompletedInSet = 0;
        timer.ActiveSeconds = active;
        timer.RemainingSeconds = 0;
        timer.PhaseEndsAt = effectiveAt;
        timer.Status = PomodoroTimerStatus.Completed;
        timer.UpdatedAt = effectiveAt;
        Sync(data, timer, effectiveAt);
    }

    private void CloseCompletedRun(StudyData data, DateTimeOffset at)
    {
        var timer = data.PomodoroTimer;
        if (timer is null) return;
        if (timer.Status != PomodoroTimerStatus.Completed)
            throw new InvalidOperationException("An active Pomodoro phase must be reset before starting a new run.");
        var session = FindSession(data, timer.SessionId);
        if (session.State is StudySessionState.Running or StudySessionState.Paused)
        {
            focus.Finish(data, session.Id, "Pomodoro concluso", at);
            ledger.SyncFocusSession(data, session, at);
        }
        data.PomodoroTimer = null;
    }

    private void Sync(StudyData data, PomodoroTimerData timer, DateTimeOffset at)
    {
        var session = data.StudySessions.FirstOrDefault(x =>
            string.Equals(x.Id, timer.SessionId, StringComparison.Ordinal));
        if (session is not null)
            ledger.SyncFocusSession(data, session, at);
    }

    private static int ElapsedActive(PomodoroTimerData timer, int duration, DateTimeOffset at)
    {
        var elapsed = Math.Max(0, timer.ActiveSeconds);
        if (timer.Status == PomodoroTimerStatus.Running)
            elapsed += Math.Max(0, (int)Math.Floor((at - timer.PhaseStartedAt).TotalSeconds));
        return Math.Min(duration, elapsed);
    }

    private static int DurationSeconds(PomodoroSettingsData settings, PomodoroPhase phase) =>
        phase switch
        {
            PomodoroPhase.Focus => settings.FocusMinutes * 60,
            PomodoroPhase.ShortBreak => settings.ShortBreakMinutes * 60,
            PomodoroPhase.LongBreak => settings.LongBreakMinutes * 60,
            _ => throw new ArgumentOutOfRangeException(nameof(phase))
        };

    private static PomodoroTimerData RequireTimer(StudyData data) =>
        data.PomodoroTimer ?? throw new InvalidOperationException("No Pomodoro timer has been started.");

    private static StudySessionData FindSession(StudyData data, string sessionId) =>
        data.StudySessions.FirstOrDefault(x => string.Equals(x.Id, sessionId, StringComparison.Ordinal))
        ?? throw new InvalidOperationException("The Pomodoro session no longer exists.");
}
