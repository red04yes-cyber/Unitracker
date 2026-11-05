using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class SprintAPersistenceTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
    };

    private static PomodoroService Service() =>
        new(new FocusSessionService(), new ActivityLedgerService());

    [Fact]
    public async Task PausedPomodoroStateSurvivesReloadAndResumesFromTimestamps()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = Data();
        var service = Service();
        service.UpdateSettings(data, 25, 5, 15, 4);
        service.Start(data, "analisi", "Ripresa", Start);
        service.Pause(data, Start.AddMinutes(10));
        await store.SaveAsync(data);

        var loaded = await store.LoadAsync();
        Assert.Equal(PomodoroTimerStatus.Paused, loaded.PomodoroTimer!.Status);
        Assert.Equal(900, loaded.PomodoroTimer.RemainingSeconds);
        Assert.Equal(25, loaded.PomodoroSettings.FocusMinutes);
        Assert.Equal(StudySessionState.Paused, Assert.Single(loaded.StudySessions).State);

        var restored = Service();
        restored.Resume(loaded, Start.AddMinutes(20));
        var result = restored.Reconcile(loaded, Start.AddMinutes(35));

        Assert.True(result.HasTransitions);
        Assert.Equal(PomodoroPhase.ShortBreak, loaded.PomodoroTimer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, loaded.PomodoroTimer.Status);
        Assert.Single(loaded.PomodoroCycles);
        await store.SaveAsync(loaded);
        var reloaded = await store.LoadAsync();
        Assert.Equal(PomodoroPhase.ShortBreak, reloaded.PomodoroTimer!.Phase);
        Assert.Single(reloaded.PomodoroCycles);
        Assert.Single(reloaded.StudyActivities);
        Assert.Equal(1500, reloaded.StudyActivities[0].EffectiveSeconds);
    }

    [Fact]
    public async Task PhaseThatExpiredWhileClosedCompletesTheBreakWithoutInventingAFocus()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = Data();
        var service = Service();
        service.Start(data, "analisi", "Chiusura", Start);
        await store.SaveAsync(data);

        var loaded = await store.LoadAsync();
        var restored = Service();
        var result = restored.Reconcile(loaded, Start.AddMinutes(30));

        Assert.Equal(2, result.Transitions.Count);
        Assert.Equal(PomodoroTimerStatus.Completed, loaded.PomodoroTimer!.Status);
        Assert.Equal(PomodoroPhase.ShortBreak, loaded.PomodoroTimer.Phase);
        Assert.Single(loaded.PomodoroCycles, x => x.IsCompleted);
        Assert.DoesNotContain(loaded.StudySessions, x => x.State == StudySessionState.Running);
        Assert.Single(loaded.StudySessions);
        Assert.Single(loaded.StudyActivities);
        Assert.Equal(1500, loaded.StudyActivities[0].EffectiveSeconds);
    }

    [Fact]
    public void ResetAfterACompletedCycleDoesNotEraseItsHistory()
    {
        var data = Data();
        var service = Service();
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Storico", Start);
        service.Reconcile(data, timer.PhaseEndsAt!.Value);
        service.Reconcile(data, timer.PhaseEndsAt!.Value);
        Assert.True(Assert.Single(data.PomodoroCycles).IsCompleted);

        service.Reset(data, Start.AddMinutes(31));
        Assert.True(data.PomodoroCycles[0].IsCompleted);
        Assert.NotNull(data.PomodoroCycles[0].CompletedAt);
        Assert.Null(data.PomodoroTimer);
        Assert.Equal(StudySessionState.Completed, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void ResetKeepsCompletedHistoryAndClosesTheTimer()
    {
        var data = Data();
        var service = Service();
        service.Start(data, "analisi", "Interruzione", Start);
        service.SkipPhase(data, Start.AddMinutes(5));
        Assert.True(Assert.Single(data.PomodoroCycles).WasSkipped);
        Assert.Equal(PomodoroPhase.ShortBreak, data.PomodoroTimer!.Phase);

        service.Reset(data, Start.AddMinutes(6));
        Assert.Null(data.PomodoroTimer);
        Assert.Equal(StudySessionState.Completed, Assert.Single(data.StudySessions).State);
        Assert.False(data.PomodoroCycles[0].IsCompleted);
        Assert.Single(data.StudyActivities);
        Assert.Equal(StudyActivityStatus.Completed, data.StudyActivities[0].Status);
    }
}
