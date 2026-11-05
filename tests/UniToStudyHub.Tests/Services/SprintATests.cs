using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class SprintATests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    private static PomodoroService NewPomodoro(out FocusSessionService focus, out ActivityLedgerService ledger)
    {
        focus = new FocusSessionService();
        ledger = new ActivityLedgerService();
        return new PomodoroService(focus, ledger);
    }

    [Fact]
    public void PomodoroTransitionsFromFocusToBreakAndCompletesTheCycle()
    {
        var data = Data();
        var service = NewPomodoro(out var focus, out var ledger);
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Limiti", Start);
        var session = Assert.Single(data.StudySessions);

        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(900, service.Snapshot(data, Start.AddMinutes(10)).RemainingSeconds);
        service.Pause(data, Start.AddMinutes(10));
        Assert.Equal(PomodoroTimerStatus.Paused, timer.Status);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(900, timer.RemainingSeconds);
        service.Resume(data, Start.AddMinutes(20));

        var focusEnd = Start.AddMinutes(35);
        var transition = service.Reconcile(data, focusEnd);
        Assert.True(transition.HasTransitions);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Single(data.PomodoroCycles);
        Assert.Equal(1500, data.PomodoroCycles[0].FocusSeconds);
        Assert.Equal(StudySessionState.Paused, session.State);

        service.Reconcile(data, focusEnd.AddMinutes(5));
        Assert.Equal(PomodoroTimerStatus.Completed, timer.Status);
        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.True(cycle.IsCompleted);
        Assert.Equal(300, cycle.BreakSeconds);
        Assert.NotNull(cycle.CompletedAt);
        Assert.Equal(PomodoroTimerStatus.Completed, service.Snapshot(data, focusEnd.AddMinutes(6)).Status);

        service.StartNextFocus(data, focusEnd.AddMinutes(6));
        Assert.Equal(PomodoroPhase.Focus, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(StudySessionState.Running, session.State);
        Assert.Equal(1, ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            focusEnd.AddMinutes(6), "UTC").ActivityCount);
    }

    [Fact]
    public void FourthFocusStartsALongBreak()
    {
        var data = Data();
        var service = NewPomodoro(out _, out _);
        service.UpdateSettings(data, 25, 1, 3, 4);
        var timer = service.Start(data, "analisi", "Cicli", Start);

        for (var index = 0; index < 4; index++)
        {
            var focusEnd = timer.PhaseEndsAt!.Value;
            service.Reconcile(data, focusEnd);
            if (index == 3)
            {
                Assert.Equal(PomodoroPhase.LongBreak, timer.Phase);
                break;
            }

            Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
            var breakEnd = timer.PhaseEndsAt!.Value;
            service.Reconcile(data, breakEnd);
            Assert.Equal(PomodoroTimerStatus.Completed, timer.Status);
            service.StartNextFocus(data, breakEnd);
        }

        Assert.Equal(4, data.PomodoroCycles.Count);
        Assert.Equal(3, data.PomodoroCycles.Count(x => x.BreakType == PomodoroPhase.ShortBreak));
        Assert.Single(data.PomodoroCycles, x => x.BreakType == PomodoroPhase.LongBreak);
        Assert.Equal(4, timer.FocusesCompletedInSet);
    }

    [Fact]
    public void QuickLogUsesTheDiaryContractAndPrivateFilter()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        var activity = ledger.AddQuickLog(
            data,
            StudyActivityType.Exercise,
            "Esercizi sui limiti",
            Start,
            Start.AddMinutes(40),
            courseId: "analisi",
            note: "Nota privata",
            tags: new[] { "limiti", "esame" },
            isPrivate: true,
            qualityRating: 4,
            now: Start.AddMinutes(45));

        Assert.Equal(StudyActivityOrigin.Manual, activity.Origin);
        Assert.Equal(2400, activity.EffectiveSeconds);
        Assert.Equal(StudyActivityType.Exercise, activity.Type);
        Assert.Equal(4, activity.QualityRating);
        Assert.Single(data.ActivityRevisions);
        Assert.Single(ledger.GetDiary(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4)));
        Assert.Empty(ledger.GetDiary(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), includePrivate: false));
    }

    [Fact]
    public void ProgressUsesUniqueLedgerSecondsAndDoesNotSumDimensions()
    {
        var        data = Data();
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "topic-1",
            CourseId = "analisi",
            Title = "Limiti"
        });
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Prima",
            CourseId = "analisi",
            SyllabusTopicId = "topic-1",
            StartedAt = Start,
            EndedAt = Start.AddHours(1),
            TimeZoneId = "UTC"
        }, Start.AddHours(2));
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Review,
            Title = "Seconda",
            CourseId = "analisi",
            SyllabusTopicId = "topic-1",
            StartedAt = Start.AddMinutes(30),
            EndedAt = Start.AddMinutes(90),
            TimeZoneId = "UTC"
        }, Start.AddHours(2));

        var progress = new ProgressService().Calculate(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(2));

        Assert.Equal(5400, progress.RecordedSeconds);
        Assert.Equal(1800, progress.OverlapSeconds);
        Assert.Equal(90, progress.RecordedMinutes);
        Assert.Equal(5400, progress.RecordedSecondsByCourse["analisi"]);
        Assert.Equal(5400, progress.RecordedSecondsByTopic["topic-1"]);
        var day = Assert.Single(progress.Daily);
        Assert.Equal(5400, day.RecordedSeconds);
        Assert.Equal(1800, day.OverlapSeconds);
    }

    [Fact]
    public void CsvExportEscapesFormulaInjectionAndHidesPrivateNotesByDefault()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "=SUM(1,2)",
            CourseId = "analisi",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            Note = "segreto",
            IsPrivate = true,
            TimeZoneId = "UTC"
        }, Start.AddHours(1));

        var bundle = new StudyDataExportService().Export(data,
            new StudyExportRequest(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), "UTC"),
            StudyExportFormat.Csv, Start.AddHours(1));
        var activities = bundle.Files.Single(x => x.FileName == "activities.csv");
        var csv = Encoding.UTF8.GetString(activities.Content);

        Assert.Contains("'=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("segreto", csv, StringComparison.Ordinal);
        Assert.Contains("Titolo", csv, StringComparison.Ordinal);
        Assert.Contains("Nota", csv, StringComparison.Ordinal);

        var withPrivate = new StudyDataExportService().Export(data,
            new StudyExportRequest(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), "UTC",
                IncludePrivateNotes: true), StudyExportFormat.Csv, Start.AddHours(1));
        Assert.Contains("segreto", Encoding.UTF8.GetString(withPrivate.Files.Single(x => x.FileName == "activities.csv").Content),
            StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxIsARealWorkbookAndJsonIsVersioned()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Reading,
            Title = "Lettura",
            CourseId = "analisi",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            TimeZoneId = "UTC"
        }, Start.AddHours(1));
        var service = new StudyDataExportService();
        var request = new StudyExportRequest(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), "UTC");
        var xlsx = service.Export(data, request, StudyExportFormat.Xlsx, Start.AddHours(1)).Files.Single();
        using (var archive = new ZipArchive(new MemoryStream(xlsx.Content), ZipArchiveMode.Read))
        {
            Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
            Assert.NotNull(archive.GetEntry("xl/styles.xml"));
            Assert.NotNull(archive.GetEntry("xl/worksheets/sheet1.xml"));
            using var reader = new StreamReader(archive.GetEntry("xl/worksheets/sheet1.xml")!.Open());
            var xml = reader.ReadToEnd();
            Assert.Contains("inlineStr", xml, StringComparison.Ordinal);
            Assert.Contains("s=\"1\"", xml, StringComparison.Ordinal);
        }

        var json = Encoding.UTF8.GetString(service.Export(data, request, StudyExportFormat.Json, Start.AddHours(1))
            .Files.Single().Content);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("Lettura", document.RootElement.GetProperty("activities")[0].GetProperty("title").GetString());
        Assert.Equal(1800, document.RootElement.GetProperty("summary").GetProperty("recordedSeconds").GetInt32());
    }
}
