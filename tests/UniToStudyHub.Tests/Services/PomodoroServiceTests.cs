using System.Collections.ObjectModel;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class PomodoroServiceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 11, 18, 9, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analysis", Name = "Analisi", Cfu = 9 }
        }
    };

    private static PomodoroService Service() =>
        new(new FocusSessionService(), new ActivityLedgerService());

    [Fact]
    public void StartCreatesOneFocusSessionAndLinksAnOpenTask()
    {
        var data = Data();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analysis",
            Title = "Esercizi sui limiti",
            EstimatedMinutes = 60,
            Status = StudyTaskStatus.Todo
        });

        var timer = Service().Start(data, "analysis", "Limiti", Start, "task-1");

        var session = Assert.Single(data.StudySessions);
        var activity = Assert.Single(data.StudyActivities);
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddMinutes(25), timer.PhaseEndsAt);
        Assert.Equal(1500, timer.RemainingSeconds);
        Assert.Equal(session.Id, timer.SessionId);
        Assert.Equal("task-1", activity.TaskId);
        Assert.Equal(session.Id, activity.SessionId);
    }

    [Fact]
    public void PauseCapturesElapsedTimeAndLeavesTheSessionPaused()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);

        service.Pause(data, Start.AddMinutes(10));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(600, timer.ActiveSeconds);
        Assert.Equal(900, timer.RemainingSeconds);
        Assert.Equal(StudySessionState.Paused, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void ResumeContinuesTheSamePhaseFromItsPersistedRemainingTime()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);
        service.Pause(data, Start.AddMinutes(10));

        service.Resume(data, Start.AddHours(1));
        var snapshot = service.Snapshot(data, Start.AddHours(1).AddMinutes(5));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddHours(1).AddMinutes(15), timer.PhaseEndsAt);
        Assert.Equal(900, snapshot.ElapsedSeconds);
        Assert.Equal(600, snapshot.RemainingSeconds);
        Assert.Equal(StudySessionState.Running, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void PauseRejectsAClockRollbackWithoutChangingTimerOrSession()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Pause(data, Start.AddMinutes(-1)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddMinutes(25), timer.PhaseEndsAt);
        Assert.Equal(StudySessionState.Running, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void ResumeRejectsAClockRollbackWithoutChangingTimerOrSession()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);
        service.Pause(data, Start.AddMinutes(10));

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Resume(data, Start.AddMinutes(9)));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(900, timer.RemainingSeconds);
        Assert.Equal(StudySessionState.Paused, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void FailedFocusPauseDoesNotPartiallyMutateThePomodoroTimer()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);
        var originalDeadline = timer.PhaseEndsAt;
        Assert.Single(data.StudySessions).State = StudySessionState.Paused;

        Assert.Throws<InvalidOperationException>(() => service.Pause(data, Start.AddMinutes(5)));

        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(originalDeadline, timer.PhaseEndsAt);
        Assert.Equal(0, timer.ActiveSeconds);
        Assert.Equal(1500, timer.RemainingSeconds);
    }

    [Fact]
    public void FailedFocusResumeDoesNotPartiallyMutateThePomodoroTimer()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);
        service.Pause(data, Start.AddMinutes(10));
        var originalPhaseStart = timer.PhaseStartedAt;
        var originalRemaining = timer.RemainingSeconds;
        Assert.Single(data.StudySessions).State = StudySessionState.Completed;

        Assert.Throws<InvalidOperationException>(() => service.Resume(data, Start.AddMinutes(20)));

        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Null(timer.PhaseEndsAt);
        Assert.Equal(originalPhaseStart, timer.PhaseStartedAt);
        Assert.Equal(originalRemaining, timer.RemainingSeconds);
    }

    [Fact]
    public void SnapshotDoesNotExtendThePhaseWhenTheSystemClockMovesBackwards()
    {
        var data = Data();
        var service = Service();
        service.Start(data, "analysis", "Limiti", Start);

        var snapshot = service.Snapshot(data, Start.AddMinutes(-1));

        Assert.Equal(0, snapshot.ElapsedSeconds);
        Assert.Equal(1500, snapshot.RemainingSeconds);
        Assert.False(snapshot.IsExpired);
    }

    [Fact]
    public void SnapshotClampsLargeForwardClockJumpsWithoutIntegerOverflow()
    {
        var data = Data();
        var service = Service();
        service.Start(data, "analysis", "Limiti", Start);

        var snapshot = service.Snapshot(data, DateTimeOffset.MaxValue);

        Assert.Equal(1500, snapshot.ElapsedSeconds);
        Assert.Equal(0, snapshot.RemainingSeconds);
        Assert.True(snapshot.IsExpired);
    }

    [Fact]
    public void ReconcileEndsAnOverdueFocusAtItsDeadlineNotAtTheLateWakeUpTime()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);

        var result = service.Reconcile(data, Start.AddMinutes(27));

        var cycle = Assert.Single(data.PomodoroCycles);
        var session = Assert.Single(data.StudySessions);
        Assert.Single(result.Transitions);
        Assert.Equal(Start.AddMinutes(25), cycle.FocusEndedAt);
        Assert.Equal(1500, cycle.FocusSeconds);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddMinutes(30), timer.PhaseEndsAt);
    }

    [Fact]
    public void ReconcileCompletesAnExpiredBreakButNeverStartsAnotherFocusAutomatically()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);

        var result = service.Reconcile(data, Start.AddMinutes(35));

        Assert.Equal(2, result.Transitions.Count);
        Assert.True(result.AwaitingNextFocus);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Completed, timer.Status);
        Assert.Equal(StudySessionState.Paused, Assert.Single(data.StudySessions).State);
        Assert.Single(data.StudySessions);
        Assert.True(Assert.Single(data.PomodoroCycles).IsCompleted);
    }

    [Fact]
    public void StartNextFocusResumesTheExistingSessionOnlyAfterTheBreakCompletes()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);
        var sessionId = timer.SessionId;
        service.Reconcile(data, Start.AddMinutes(35));

        service.StartNextFocus(data, Start.AddMinutes(40));

        Assert.Equal(sessionId, timer.SessionId);
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(Start.AddMinutes(65), timer.PhaseEndsAt);
        Assert.Equal(StudySessionState.Running, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void SkippingFocusDoesNotCountItTowardTheLongBreakThreshold()
    {
        var data = Data();
        var service = Service();
        var timer = service.Start(data, "analysis", "Limiti", Start);

        service.SkipPhase(data, Start.AddMinutes(10));

        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.True(cycle.WasSkipped);
        Assert.Equal(600, cycle.FocusSeconds);
        Assert.Equal(0, timer.FocusesCompletedInSet);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
    }

    [Fact]
    public void CompletedFocusSelectsTheConfiguredLongBreakThreshold()
    {
        var data = Data();
        var service = Service();
        service.UpdateSettings(data, 25, 5, 15, 1);
        var timer = service.Start(data, "analysis", "Limiti", Start);

        service.CompletePhase(data, Start.AddMinutes(24));

        Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);
        Assert.Equal(1, timer.FocusesCompletedInSet);
        Assert.Equal(PomodoroPhase.LongBreak, Assert.Single(data.PomodoroCycles).BreakType);
    }

    [Fact]
    public void ResetClosesTheFocusSessionAndKeepsItsActivityHistory()
    {
        var data = Data();
        var service = Service();
        service.Start(data, "analysis", "Limiti", Start);

        service.Reset(data, Start.AddMinutes(10));

        Assert.Null(data.PomodoroTimer);
        Assert.Equal(StudySessionState.Completed, Assert.Single(data.StudySessions).State);
        Assert.Equal("Pomodoro interrotto", data.StudySessions[0].Outcome);
        Assert.Equal(600, data.StudySessions[0].AccumulatedFocusSeconds);
        Assert.Equal(StudyActivityStatus.Completed, Assert.Single(data.StudyActivities).Status);
    }

    [Fact]
    public void SettingsCannotChangeDuringAnActivePhaseAndInvalidRangesAreRejected()
    {
        var data = Data();
        var service = Service();
        service.Start(data, "analysis", "Limiti", Start);

        Assert.Throws<InvalidOperationException>(() => service.UpdateSettings(data, 30, 5, 15, 4));
        service.Reset(data, Start.AddMinutes(1));

        Assert.Throws<ArgumentOutOfRangeException>(() => service.UpdateSettings(data, 0, 5, 15, 4));
        Assert.Equal(25, data.PomodoroSettings.FocusMinutes);
    }

    [Fact]
    public async Task PausedPomodoroRoundTripsThroughSqliteAndResumesAfterReload()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = Data();
        var service = Service();
        service.Start(data, "analysis", "Limiti", Start);
        service.Pause(data, Start.AddMinutes(10));

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();
        var restoredService = Service();

        restoredService.Resume(loaded, Start.AddHours(1));
        var snapshot = restoredService.Snapshot(loaded, Start.AddHours(1).AddMinutes(5));

        Assert.Equal(PomodoroTimerStatus.Running, loaded.PomodoroTimer!.Status);
        Assert.Equal(Start.AddHours(1).AddMinutes(15), loaded.PomodoroTimer.PhaseEndsAt);
        Assert.Equal(900, snapshot.ElapsedSeconds);
        Assert.Equal(600, snapshot.RemainingSeconds);
        Assert.Single(loaded.StudySessions);
        Assert.Single(loaded.StudyActivities);
    }
}
