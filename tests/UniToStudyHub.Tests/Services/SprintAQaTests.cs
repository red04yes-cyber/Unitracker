using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class SprintAQaTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Day = new(2026, 11, 4);
    private static readonly StudyExportRequest DefaultExport = new(Day, Day, "UTC");

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
    };

    private static PomodoroService Pomodoro(out ActivityLedgerService ledger)
    {
        ledger = new ActivityLedgerService();
        return new PomodoroService(new FocusSessionService(), ledger);
    }

    private static string Csv(StudyExportBundle bundle, string name) =>
        Encoding.UTF8.GetString(bundle.Files.Single(x => x.FileName == name).Content);

    private static string Sheet(StudyExportFile file, int index)
    {
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry($"xl/worksheets/sheet{index}.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static string Entry(StudyExportFile file, string path)
    {
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry(path)!.Open());
        return reader.ReadToEnd();
    }

    [Fact]
    public void PauseResumeKeepFocusAndPauseSegmentsSeparate()
    {
        var data = Data();
        var service = Pomodoro(out var ledger);
        service.UpdateSettings(data, 25, 5, 15, 4);
        service.Start(data, "analisi", "Derivate", Start);

        service.Pause(data, Start.AddMinutes(10));
        service.Resume(data, Start.AddMinutes(15));
        service.CompletePhase(data, Start.AddMinutes(20));

        var session = Assert.Single(data.StudySessions);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(900, session.AccumulatedFocusSeconds);
        Assert.Equal(300, session.AccumulatedPauseSeconds);

        var segments = session.Segments.OrderBy(x => x.StartedAt).ToList();
        Assert.Equal(4, segments.Count);
        Assert.Equal(StudySessionSegmentType.Focus, segments[0].Type);
        Assert.Equal(Start, segments[0].StartedAt);
        Assert.Equal(Start.AddMinutes(10), segments[0].EndedAt);
        Assert.Equal(StudySessionSegmentType.Pause, segments[1].Type);
        Assert.Equal(Start.AddMinutes(10), segments[1].StartedAt);
        Assert.Equal(Start.AddMinutes(15), segments[1].EndedAt);
        Assert.Equal(StudySessionSegmentType.Focus, segments[2].Type);
        Assert.Equal(Start.AddMinutes(15), segments[2].StartedAt);
        Assert.Equal(Start.AddMinutes(20), segments[2].EndedAt);
        Assert.Equal(StudySessionSegmentType.Pause, segments[3].Type);
        Assert.Null(segments[3].EndedAt);

        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.Equal(900, cycle.FocusSeconds);
        Assert.Equal(PomodoroPhase.ShortBreak, cycle.BreakType);

        var activity = Assert.Single(data.StudyActivities);
        Assert.Equal(StudyActivityOrigin.Timer, activity.Origin);
        Assert.Equal(900, activity.EffectiveSeconds);
        Assert.Equal(900, ledger.Summarize(data, Day, Day, Start.AddMinutes(20), "UTC").RecordedSeconds);
    }

    [Fact]
    public async Task ReconcileAfterRestartClosesFocusAndBreakAtTheirOwnDeadlines()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = Data();
        var service = Pomodoro(out _);
        service.UpdateSettings(data, 25, 5, 15, 4);
        service.Start(data, "analisi", "Ripasso", Start);
        await store.SaveAsync(data);

        var loaded = await store.LoadAsync();
        var restored = Pomodoro(out _);
        var result = restored.Reconcile(loaded, Start.AddMinutes(40));

        Assert.Equal(2, result.Transitions.Count);
        Assert.True(result.AwaitingNextFocus);
        Assert.Equal(PomodoroTimerStatus.Completed, loaded.PomodoroTimer!.Status);
        Assert.Equal(PomodoroPhase.ShortBreak, loaded.PomodoroTimer.Phase);
        var cycle = Assert.Single(loaded.PomodoroCycles);
        Assert.Equal(1500, cycle.FocusSeconds);
        Assert.Equal(300, cycle.BreakSeconds);
        Assert.True(cycle.IsCompleted);
        Assert.Equal(Start.AddMinutes(25), cycle.FocusEndedAt);
        Assert.Equal(Start.AddMinutes(30), cycle.BreakEndedAt);
        var activity = Assert.Single(loaded.StudyActivities);
        Assert.Equal(1500, activity.EffectiveSeconds);
        // The session pauses during the break, so the ledger row stays open and
        // its intervals come from the session focus segment, not from EndedAt.
        Assert.Null(activity.EndedAt);
        Assert.Equal(1500, new ActivityLedgerService()
            .Summarize(loaded, Day, Day, Start.AddMinutes(40), "UTC").RecordedSeconds);

        await store.SaveAsync(loaded);
        var reloaded = await store.LoadAsync();
        Assert.Single(reloaded.PomodoroCycles);
        Assert.Equal(1500, Assert.Single(reloaded.StudyActivities).EffectiveSeconds);
        Assert.Equal(StudySessionState.Paused, Assert.Single(reloaded.StudySessions).State);
    }

    [Fact]
    public void StartNextFocusKeepsOneLedgerRowAndContinuesSegments()
    {
        var data = Data();
        var service = Pomodoro(out _);
        service.UpdateSettings(data, 25, 5, 15, 4);
        service.Start(data, "analisi", "Derivate", Start);
        service.CompletePhase(data, Start.AddMinutes(24));
        service.CompletePhase(data, Start.AddMinutes(29));
        service.StartNextFocus(data, Start.AddMinutes(30));
        service.Pause(data, Start.AddMinutes(35));

        var activity = Assert.Single(data.StudyActivities);
        Assert.Equal(1440 + 300, activity.EffectiveSeconds);
        var session = Assert.Single(data.StudySessions);
        Assert.Equal(2, session.Segments.Count(x => x.Type == StudySessionSegmentType.Focus));
        Assert.Single(data.PomodoroCycles);
        Assert.Equal(2, data.PomodoroTimer!.CycleNumber);
    }

    [Fact]
    public void CsvPreservesAccentsQuotesCommasAndNewlines()
    {
        var data = Data();
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "attivita-accenti",
            Type = StudyActivityType.Study,
            Title = "Analisi: àèìòù, \"virgolette\"\ne linea",
            CourseId = "analisi",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            TimeZoneId = "UTC",
            EffectiveSeconds = 1800,
            Note = "Riga, con accento è"
        });

        var csv = Csv(new StudyDataExportService().Export(data, DefaultExport, StudyExportFormat.Csv, Start.AddHours(1)),
            "activities.csv");

        Assert.Contains("àèìòù", csv, StringComparison.Ordinal);
        Assert.Contains("\"\"virgolette\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("Riga, con accento è", csv, StringComparison.Ordinal);
        Assert.Equal(1, csv.Count(character => character == '\uFEFF'));
    }

    [Fact]
    public void XlsxKeepsSeparateSheetsAndIsoColumnsForExplicitOffsets()
    {
        var data = Data();
        var offset = TimeSpan.FromHours(2);
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "attivita-roma",
            Type = StudyActivityType.Study,
            Title = "Lezione",
            CourseId = "analisi",
            StartedAt = new DateTimeOffset(2026, 11, 4, 10, 0, 0, offset),
            EndedAt = new DateTimeOffset(2026, 11, 4, 10, 30, 0, offset),
            TimeZoneId = "Europe/Rome",
            EffectiveSeconds = 1800
        });

        var file = new StudyDataExportService()
            .Export(data, DefaultExport, StudyExportFormat.Xlsx, Start.AddHours(1))
            .Files.Single();
        var workbook = Entry(file, "xl/workbook.xml");
        Assert.Contains("name=\"Attività\"", workbook, StringComparison.Ordinal);
        Assert.Contains("name=\"Sessioni\"", workbook, StringComparison.Ordinal);
        Assert.Contains("name=\"Segmenti\"", workbook, StringComparison.Ordinal);
        Assert.Contains("name=\"Riepilogo\"", workbook, StringComparison.Ordinal);
        Assert.Contains("name=\"Pomodoro\"", workbook, StringComparison.Ordinal);

        var sheet = Sheet(file, 1);
        Assert.Contains("Inizio ISO", sheet, StringComparison.Ordinal);
        Assert.Contains("2026-11-04T10:00:00.0000000+02:00", sheet, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonExportIsVersionedAndPrivateNotesStayOutByDefault()
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "sessione-privata",
            CourseId = "analisi",
            Topic = "Ripasso",
            PlannedMinutes = 30,
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            State = StudySessionState.Completed,
            Notes = "nota riservata della sessione",
            NotesArePrivate = true
        });
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "attivita-privata",
            Type = StudyActivityType.Study,
            Title = "Ripasso",
            CourseId = "analisi",
            SessionId = "sessione-privata",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            TimeZoneId = "UTC",
            EffectiveSeconds = 1800,
            Note = "appunto privato",
            IsPrivate = true
        });
        var service = new StudyDataExportService();

        var json = Encoding.UTF8.GetString(service
            .Export(data, DefaultExport, StudyExportFormat.Json, Start.AddHours(1))
            .Files.Single().Content);

        Assert.Contains("\"schemaVersion\": \"1.0\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("nota riservata", json, StringComparison.Ordinal);
        Assert.DoesNotContain("appunto privato", json, StringComparison.Ordinal);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("1.0", parsed.RootElement.GetProperty("schemaVersion").GetString());

        var full = Encoding.UTF8.GetString(service
            .Export(data, DefaultExport with { IncludePrivateNotes = true }, StudyExportFormat.Json, Start.AddHours(1))
            .Files.Single().Content);
        Assert.Contains("nota riservata", full, StringComparison.Ordinal);
        Assert.Contains("appunto privato", full, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlappingTimerAndManualWorkCountsOnceInProgress()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", "Derivate", 30, Start);
        focus.Finish(data, session.Id, "Completata", Start.AddMinutes(30));
        ledger.SyncFocusSession(data, session, Start.AddMinutes(30));
        ledger.AddQuickLog(data, StudyActivityType.Exercise, "Esercizi",
            Start.AddMinutes(15), Start.AddMinutes(45), courseId: "analisi", now: Start.AddMinutes(46));

        var summary = ledger.Summarize(data, Day, Day, Start.AddMinutes(46), "UTC");
        Assert.Equal(2, summary.ActivityCount);
        Assert.Equal(2700, summary.RecordedSeconds);
        Assert.Equal(900, summary.OverlapSeconds);
        Assert.Equal(2700, summary.ByCourse["analisi"]);

        var progress = new ProgressService().Calculate(data, Day, Day, Start.AddMinutes(46));
        Assert.Equal(2700, progress.RecordedSeconds);
        Assert.Equal(2700, progress.RecordedSecondsByCourse["analisi"]);
        Assert.Equal(900, progress.OverlapSeconds);
        Assert.Equal(30, progress.FocusMinutes);
        Assert.Equal(2700, progress.Daily.Sum(x => x.RecordedSeconds));
    }
}
