using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class PomodoroClockRollbackTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 26, 9, 0, 0, TimeSpan.Zero);

    private static (StudyData Data, PomodoroService Service, PomodoroTimerData Timer, StudySessionData Session)
        CreateRunning(DateTimeOffset? startedAt = null)
    {
        var data = new StudyData();
        var service = new PomodoroService(new FocusSessionService(), new ActivityLedgerService());
        var timer = service.Start(data, null, "Derivate", startedAt ?? Start);
        return (data, service, timer, Assert.Single(data.StudySessions));
    }

    private static (StudyData Data, PomodoroService Service, PomodoroTimerData Timer,
        StudySessionData Session, PomodoroCycleData Cycle) CreateRunningBreak()
    {
        var (data, service, timer, session) = CreateRunning();
        service.CompletePhase(data, Start.AddMinutes(25));
        return (data, service, timer, session, Assert.Single(data.PomodoroCycles));
    }

    [Fact]
    public void StartRejectsOutOfRangePersistedSettingsWithoutCreatingSession()
    {
        var data = new StudyData();
        data.PomodoroSettings.FocusMinutes = int.MaxValue;
        var service = new PomodoroService(new FocusSessionService(), new ActivityLedgerService());

        Assert.Throws<InvalidDataException>(() =>
            service.Start(data, null, "Derivate", Start));

        Assert.Empty(data.StudySessions);
        Assert.Null(data.PomodoroTimer);
        Assert.Empty(data.StudyActivities);
    }

    [Fact]
    public void CompletingFocusBeforeItsStartLeavesTheSessionRunning()
    {
        var (data, service, timer, session) = CreateRunning();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.CompletePhase(data, Start.AddSeconds(-1)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(StudySessionState.Running, session.State);
        Assert.Equal(Start, session.LastResumedAt);
        Assert.Empty(data.PomodoroCycles);
    }

    [Fact]
    public void PauseBeforeFocusStartLeavesTimerAndSessionUnchanged()
    {
        var (data, service, timer, session) = CreateRunning();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Pause(data, Start.AddSeconds(-1)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start, timer.PhaseStartedAt);
        Assert.Equal(Start.AddMinutes(25), timer.PhaseEndsAt);
        Assert.Equal(Start, timer.UpdatedAt);
        Assert.Equal(StudySessionState.Running, session.State);
        Assert.Equal(Start, session.LastResumedAt);
        Assert.Null(session.PausedAt);
    }

    [Fact]
    public void ResumeBeforeFocusPauseLeavesTimerAndSessionPaused()
    {
        var (data, service, timer, session) = CreateRunning();
        var pausedAt = Start.AddMinutes(5);
        service.Pause(data, pausedAt);
        var remaining = timer.RemainingSeconds;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Resume(data, pausedAt.AddMinutes(-1)));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(Start, timer.PhaseStartedAt);
        Assert.Equal(pausedAt, timer.UpdatedAt);
        Assert.Equal(remaining, timer.RemainingSeconds);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(pausedAt, session.PausedAt);
        Assert.Null(session.LastResumedAt);
    }

    [Fact]
    public void PauseBeforeBreakStartIsRejectedWithoutChangingTheTimer()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();
        var deadline = timer.PhaseEndsAt;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Pause(data, Start.AddMinutes(24)));

        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddMinutes(25), timer.PhaseStartedAt);
        Assert.Equal(deadline, timer.PhaseEndsAt);
        Assert.Equal(Start.AddMinutes(25), timer.UpdatedAt);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Null(cycle.BreakEndedAt);
    }

    [Fact]
    public void ResumeBeforeBreakPauseIsRejectedWithoutChangingTheTimer()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();
        var pausedAt = Start.AddMinutes(27);
        service.Pause(data, pausedAt);
        var remaining = timer.RemainingSeconds;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Resume(data, pausedAt.AddMinutes(-1)));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(Start.AddMinutes(25), timer.PhaseStartedAt);
        Assert.Equal(pausedAt, timer.UpdatedAt);
        Assert.Equal(remaining, timer.RemainingSeconds);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Null(cycle.BreakEndedAt);
    }

    [Fact]
    public void CompletingBreakBeforeItsStartDoesNotCorruptCycleHistory()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.CompletePhase(data, Start.AddMinutes(24)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(Start.AddMinutes(30), timer.PhaseEndsAt);
        Assert.Null(cycle.BreakEndedAt);
        Assert.Equal(0, cycle.BreakSeconds);
        Assert.False(cycle.IsCompleted);
        Assert.Null(cycle.CompletedAt);
        Assert.Equal(StudySessionState.Paused, session.State);
    }

    [Fact]
    public void SkippingBreakBeforeItsStartDoesNotCorruptCycleHistory()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.SkipPhase(data, Start.AddMinutes(24)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Null(cycle.BreakEndedAt);
        Assert.False(cycle.IsCompleted);
        Assert.Equal(StudySessionState.Paused, session.State);
    }

    [Fact]
    public void ResetBeforeBreakStartLeavesCycleAndTimerUntouched()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Reset(data, Start.AddMinutes(24)));

        Assert.Same(timer, data.PomodoroTimer);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Null(cycle.BreakEndedAt);
        Assert.Equal(0, cycle.BreakSeconds);
        Assert.False(cycle.IsCompleted);
        Assert.Equal(StudySessionState.Paused, session.State);
    }

    [Fact]
    public void StartingNextFocusBeforeCompletedBreakTimestampIsRejected()
    {
        var (data, service, timer, session, cycle) = CreateRunningBreak();
        service.CompletePhase(data, Start.AddMinutes(30));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.StartNextFocus(data, Start.AddMinutes(29)));

        Assert.Equal(PomodoroTimerStatus.Completed, timer.Status);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(Start.AddMinutes(30), timer.UpdatedAt);
        Assert.True(cycle.IsCompleted);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Null(session.LastResumedAt);
    }

    [Fact]
    public void ResetBeforeRunningPhaseStartDoesNotCloseFocusSession()
    {
        var (data, service, timer, session) = CreateRunning();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Reset(data, Start.AddSeconds(-1)));

        Assert.Same(timer, data.PomodoroTimer);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(StudySessionState.Running, session.State);
        Assert.Equal(Start, session.LastResumedAt);
        Assert.Null(session.EndedAt);
    }

    [Fact]
    public void SnapshotClampsExtremePastClockJumpWithoutOverflow()
    {
        var (data, service, _, _) = CreateRunning();

        var snapshot = service.Snapshot(data, Start.AddYears(-100));

        Assert.Equal(0, snapshot.ElapsedSeconds);
        Assert.Equal(25 * 60, snapshot.RemainingSeconds);
        Assert.False(snapshot.IsExpired);
    }

    [Fact]
    public void SnapshotClampsExtremeFutureClockJumpToThePhaseDuration()
    {
        var (data, service, _, _) = CreateRunning();

        var snapshot = service.Snapshot(data, Start.AddYears(100));

        Assert.Equal(25 * 60, snapshot.ElapsedSeconds);
        Assert.Equal(0, snapshot.RemainingSeconds);
        Assert.True(snapshot.IsExpired);
    }

    [Fact]
    public void StartNearDateTimeOffsetMaximumDoesNotCreateAnOrphanSession()
    {
        var data = new StudyData();
        var service = new PomodoroService(new FocusSessionService(), new ActivityLedgerService());
        var nearMaximum = DateTimeOffset.MaxValue.AddMinutes(-1);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Start(data, null, "Derivate", nearMaximum));

        Assert.Empty(data.StudySessions);
        Assert.Null(data.PomodoroTimer);
        Assert.Empty(data.StudyActivities);
    }

    [Fact]
    public void ResumeNearDateTimeOffsetMaximumDoesNotUnpauseTheFocusSession()
    {
        var nearMaximum = DateTimeOffset.MaxValue.AddHours(-1);
        var (data, service, timer, session) = CreateRunning(nearMaximum);
        var pausedAt = nearMaximum.AddMinutes(5);
        service.Pause(data, pausedAt);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.Resume(data, DateTimeOffset.MaxValue.AddMinutes(-1)));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(pausedAt, session.PausedAt);
    }

    [Fact]
    public void BreakDeadlineOverflowDoesNotPauseFocusOrCreateCycle()
    {
        var data = new StudyData();
        var service = new PomodoroService(new FocusSessionService(), new ActivityLedgerService());
        service.UpdateSettings(data, 25, 5, 240, 1);
        var startedAt = DateTimeOffset.MaxValue.AddMinutes(-30);
        var timer = service.Start(data, null, "Derivate", startedAt);
        var session = Assert.Single(data.StudySessions);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.CompletePhase(data, startedAt.AddMinutes(25)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(StudySessionState.Running, session.State);
        Assert.Equal(startedAt, session.LastResumedAt);
        Assert.Empty(data.PomodoroCycles);
    }
}
