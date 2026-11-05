using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class SprintAReviewTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    private static PomodoroService NewPomodoro(out ActivityLedgerService ledger)
    {
        ledger = new ActivityLedgerService();
        return new PomodoroService(new FocusSessionService(), ledger);
    }

    private static string Csv(StudyExportBundle bundle, string fileName) =>
        Encoding.UTF8.GetString(bundle.Files.Single(x => x.FileName == fileName).Content);

    private static string Sheet(StudyExportFile file, int index)
    {
        using var archive = new ZipArchive(new MemoryStream(file.Content), ZipArchiveMode.Read);
        using var reader = new StreamReader(archive.GetEntry($"xl/worksheets/sheet{index}.xml")!.Open());
        return reader.ReadToEnd();
    }

    private static StudyData OpenSessionData(string notes = "", bool notesArePrivate = true)
    {
        var data = Data();
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-open",
            CourseId = "analisi",
            Topic = "Sessione aperta",
            PlannedMinutes = 25,
            StartedAt = Start,
            LastResumedAt = Start,
            State = StudySessionState.Running,
            Notes = notes,
            NotesArePrivate = notesArePrivate,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-open",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = Start
                }
            }
        });
        return data;
    }

    private static readonly StudyExportRequest ExportRequest =
        new(new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), "UTC");

    [Fact]
    public void CompletePhaseAfterDeadlineUsesPersistedDeadlineAndDoesNotCreditLateTime()
    {
        var data = Data();
        var service = NewPomodoro(out var ledger);
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Scadenza", Start);
        var focusEnd = timer.PhaseEndsAt!.Value;
        var session = Assert.Single(data.StudySessions);

        service.CompletePhase(data, focusEnd.AddMinutes(10));

        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.Equal(focusEnd, cycle.FocusEndedAt);
        Assert.Equal(1500, cycle.FocusSeconds);
        Assert.True(cycle.IsCompleted);
        Assert.Equal(focusEnd.AddMinutes(5), cycle.CompletedAt);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Completed, timer.Status);
        Assert.Equal(StudySessionState.Paused, session.State);
        Assert.Equal(1500, session.AccumulatedFocusSeconds);
        var focusSegment = Assert.Single(session.Segments, x => x.Type == StudySessionSegmentType.Focus);
        Assert.Equal(focusEnd, focusSegment.EndedAt);
        var pauseSegment = Assert.Single(session.Segments, x => x.Type == StudySessionSegmentType.Pause);
        Assert.Equal(focusEnd, pauseSegment.StartedAt);
        Assert.Null(pauseSegment.EndedAt);
        Assert.Equal(1500, ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            focusEnd.AddMinutes(10), "UTC").RecordedSeconds);
    }

    [Fact]
    public void SkipPhaseAfterDeadlineIsReconciledAsNaturalCompletion()
    {
        var data = Data();
        var service = NewPomodoro(out _);
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Skip tardivo", Start);
        var focusEnd = timer.PhaseEndsAt!.Value;

        service.SkipPhase(data, focusEnd.AddMinutes(2));

        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.False(cycle.WasSkipped);
        Assert.Equal(focusEnd, cycle.FocusEndedAt);
        Assert.Equal(1500, cycle.FocusSeconds);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, timer.Status);
        Assert.Equal(focusEnd.AddMinutes(5), timer.PhaseEndsAt);
        Assert.Equal(StudySessionState.Paused, Assert.Single(data.StudySessions).State);
    }

    [Fact]
    public void ResetAfterDeadlineDoesNotExtendFocusOrMoveTheFocusSegment()
    {
        var data = Data();
        var service = NewPomodoro(out var ledger);
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Reset tardivo", Start);
        var focusEnd = timer.PhaseEndsAt!.Value;
        var resetAt = focusEnd.AddMinutes(2);

        service.Reset(data, resetAt);

        var session = Assert.Single(data.StudySessions);
        var cycle = Assert.Single(data.PomodoroCycles);
        Assert.Null(data.PomodoroTimer);
        Assert.Equal(StudySessionState.Completed, session.State);
        Assert.Equal(1500, session.AccumulatedFocusSeconds);
        Assert.Equal(focusEnd, Assert.Single(session.Segments, x => x.Type == StudySessionSegmentType.Focus).EndedAt);
        Assert.Equal(focusEnd, cycle.FocusEndedAt);
        Assert.Equal(1500, cycle.FocusSeconds);
        Assert.Equal(focusEnd, cycle.BreakStartedAt);
        Assert.Equal(resetAt, cycle.BreakEndedAt);
        Assert.False(cycle.IsCompleted);
        Assert.Equal(1500, ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            resetAt, "UTC").RecordedSeconds);
    }

    [Fact]
    public void ResetBeforeDeadlineKeepsVoluntaryElapsedTime()
    {
        var data = Data();
        var service = NewPomodoro(out var ledger);
        service.UpdateSettings(data, 25, 5, 15, 4);
        service.Start(data, "analisi", "Reset puntuale", Start);
        var resetAt = Start.AddMinutes(10);

        service.Reset(data, resetAt);

        var session = Assert.Single(data.StudySessions);
        Assert.Null(data.PomodoroTimer);
        Assert.Equal(StudySessionState.Completed, session.State);
        Assert.Equal(600, session.AccumulatedFocusSeconds);
        Assert.Equal(resetAt, Assert.Single(session.Segments).EndedAt);
        Assert.Empty(data.PomodoroCycles);
        Assert.Equal(600, ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            resetAt, "UTC").RecordedSeconds);
    }

    [Fact]
    public void ReconcileReportsTheOldFocusDeadlineAsTransitionTimestamp()
    {
        var data = Data();
        var service = NewPomodoro(out _);
        service.UpdateSettings(data, 25, 5, 15, 4);
        var timer = service.Start(data, "analisi", "Transizione", Start);
        var focusEnd = timer.PhaseEndsAt!.Value;

        var result = service.Reconcile(data, focusEnd.AddMinutes(1));

        var transition = Assert.Single(result.Transitions);
        Assert.Equal(PomodoroPhase.Focus, transition.Phase);
        Assert.Equal(PomodoroTimerStatus.Running, transition.Status);
        Assert.Equal(focusEnd, transition.At);
        Assert.Equal(PomodoroPhase.ShortBreak, timer.Phase);
        Assert.Equal(focusEnd, timer.PhaseWindowStartedAt);
        Assert.Equal(focusEnd.AddMinutes(5), timer.PhaseEndsAt);
    }

    [Fact]
    public void WeeklyGoalUsesUniqueRecordedMinutesAndKeepsFocusSeparate()
    {
        var data = Data();
        data.WeeklyStudyHours = 1;
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Prima",
            CourseId = "analisi",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(40),
            TimeZoneId = "UTC"
        }, Start.AddHours(2));
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Seconda",
            CourseId = "analisi",
            StartedAt = Start.AddMinutes(20),
            EndedAt = Start.AddHours(1),
            TimeZoneId = "UTC"
        }, Start.AddHours(2));

        var review = new WeeklyReviewService().Create(data, new DateOnly(2026, 11, 2), Start.AddHours(2));

        Assert.Equal(0, review.Progress.FocusMinutes);
        Assert.Equal(60, review.Progress.RecordedMinutes);
        Assert.Equal(1200, review.Progress.OverlapSeconds);
        Assert.Equal(0, review.Progress.RecordedMinutesRemaining);
        Assert.Contains(review.Observations,
            observation => observation.Contains("Obiettivo settimanale: 60 minuti; raggiunto", StringComparison.Ordinal));
        Assert.Contains(review.Observations,
            observation => observation.Contains("Tempo Focus: 0 minuti", StringComparison.Ordinal));

        data.WeeklyStudyHours = 2;
        var notReached = new WeeklyReviewService().Create(data, new DateOnly(2026, 11, 2), Start.AddHours(2));
        Assert.Equal(60, notReached.Progress.RecordedMinutesRemaining);
        Assert.Contains(notReached.Observations,
            observation => observation.Contains("mancano 60 minuti", StringComparison.Ordinal));
    }

    [Fact]
    public void CsvPreservesExplicitOffsetsAndNeutralizesLeadingWhitespaceFormulas()
    {
        var data = Data();
        var offsetStart = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromMinutes(330));
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "offset",
            Type = StudyActivityType.Study,
            Title = "Offset",
            StartedAt = offsetStart,
            EndedAt = offsetStart.AddMinutes(30),
            TimeZoneId = "Asia/Kolkata",
            EffectiveSeconds = 1800
        });
        var titles = new[]
        {
            "=SUM(1,2)",
            " +SUM(1,2)",
            "\t=SUM(1,2)",
            "\n=SUM(1,2)",
            "\r@SUM(1,2)",
            "Testo normale"
        };
        for (var index = 0; index < titles.Length; index++)
        {
            data.StudyActivities.Add(new StudyActivityData
            {
                Id = $"formula-{index}",
                Type = StudyActivityType.Study,
                Title = titles[index],
                StartedAt = Start.AddMinutes(index),
                EndedAt = Start.AddMinutes(index + 1),
                TimeZoneId = "UTC",
                EffectiveSeconds = 60,
                FocusRating = -1
            });
        }

        var bundle = new StudyDataExportService().Export(data, ExportRequest, StudyExportFormat.Csv, Start.AddHours(2));
        var csv = Csv(bundle, "activities.csv");

        Assert.Contains("2026-11-04T09:00:00.0000000+05:30", csv, StringComparison.Ordinal);
        Assert.Contains("'=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("' +SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("'\t=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("'\n=SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("'\r@SUM(1,2)", csv, StringComparison.Ordinal);
        Assert.Contains("Testo normale", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("'Testo normale", csv, StringComparison.Ordinal);
        Assert.Contains(",-1,", csv, StringComparison.Ordinal);
        Assert.DoesNotContain("'-1", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void XlsxKeepsTypedDatesAndAddsIsoColumnsForTwoOffsets()
    {
        var data = Data();
        var positive = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromMinutes(330));
        var negative = new DateTimeOffset(2026, 11, 4, 8, 30, 0, TimeSpan.FromHours(-8));
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "positive",
            Type = StudyActivityType.Study,
            Title = "Offset positivo",
            StartedAt = positive,
            EndedAt = positive.AddMinutes(10),
            TimeZoneId = "Asia/Kolkata",
            EffectiveSeconds = 600
        });
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "negative",
            Type = StudyActivityType.Study,
            Title = "Offset negativo",
            StartedAt = negative,
            EndedAt = negative.AddMinutes(10),
            TimeZoneId = "America/Los_Angeles",
            EffectiveSeconds = 600
        });

        var file = new StudyDataExportService().Export(data, ExportRequest, StudyExportFormat.Xlsx, Start.AddHours(2))
            .Files.Single();
        var xml = Sheet(file, 1);

        Assert.Contains("Inizio ISO", xml, StringComparison.Ordinal);
        Assert.Contains("2026-11-04T09:00:00.0000000+05:30", xml, StringComparison.Ordinal);
        Assert.Contains("2026-11-04T08:30:00.0000000-08:00", xml, StringComparison.Ordinal);
        Assert.Contains("s=\"1\"", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenSegmentsUseExportTimeInCsvXlsxAndJsonWithoutBeingClosed()
    {
        var data = OpenSessionData();
        var generatedAt = Start.AddMinutes(10);
        var service = new StudyDataExportService();

        var csv = service.Export(data, ExportRequest, StudyExportFormat.Csv, generatedAt);
        var segmentCsv = Csv(csv, "segments.csv");
        Assert.Contains("\"2026-11-04T09:00:00.0000000+00:00\",,600", segmentCsv, StringComparison.Ordinal);

        var xlsx = service.Export(data, ExportRequest, StudyExportFormat.Xlsx, generatedAt).Files.Single();
        var segmentSheet = Sheet(xlsx, 3);
        Assert.Contains(">600<", segmentSheet, StringComparison.Ordinal);
        Assert.Contains("Fine ISO", segmentSheet, StringComparison.Ordinal);

        var json = service.Export(data, ExportRequest, StudyExportFormat.Json, generatedAt);
        using var document = JsonDocument.Parse(json.Files.Single().Content);
        var segment = document.RootElement.GetProperty("segments")[0];
        Assert.Equal(JsonValueKind.Null, segment.GetProperty("endedAt").ValueKind);
        Assert.Equal(600, segment.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(JsonValueKind.Null,
            document.RootElement.GetProperty("sessions")[0].GetProperty("segments")[0].GetProperty("endedAt").ValueKind);
    }

    [Fact]
    public void SessionNotesArePrivateByDefaultAndIncludedOnlyWithExplicitConsent()
    {
        var data = OpenSessionData("nota privata di sessione");
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-public",
            CourseId = "analisi",
            Topic = "Sessione pubblica",
            StartedAt = Start.AddHours(1),
            EndedAt = Start.AddHours(2),
            State = StudySessionState.Completed,
            Notes = "nota pubblica di sessione",
            NotesArePrivate = false
        });
        var service = new StudyDataExportService();

        var defaults = service.Export(data, ExportRequest, StudyExportFormat.Csv, Start.AddHours(2));
        var sessionsCsv = Csv(defaults, "sessions.csv");
        Assert.DoesNotContain("nota privata di sessione", sessionsCsv, StringComparison.Ordinal);
        Assert.Contains("nota pubblica di sessione", sessionsCsv, StringComparison.Ordinal);

        var xlsx = service.Export(data, ExportRequest, StudyExportFormat.Xlsx, Start.AddHours(2)).Files.Single();
        var sessionsSheet = Sheet(xlsx, 2);
        Assert.DoesNotContain("nota privata di sessione", sessionsSheet, StringComparison.Ordinal);
        Assert.Contains("nota pubblica di sessione", sessionsSheet, StringComparison.Ordinal);

        var json = service.Export(data, ExportRequest, StudyExportFormat.Json, Start.AddHours(2));
        using (var document = JsonDocument.Parse(json.Files.Single().Content))
        {
            var sessions = document.RootElement.GetProperty("sessions");
            Assert.Equal(JsonValueKind.Null, sessions[0].GetProperty("notes").ValueKind);
            Assert.True(sessions[0].GetProperty("notesArePrivate").GetBoolean());
            Assert.Equal("nota pubblica di sessione", sessions[1].GetProperty("notes").GetString());
            Assert.False(sessions[1].GetProperty("notesArePrivate").GetBoolean());
        }

        var explicitRequest = ExportRequest with { IncludePrivateNotes = true };
        var explicitCsv = Csv(service.Export(data, explicitRequest, StudyExportFormat.Csv, Start.AddHours(2)), "sessions.csv");
        Assert.Contains("nota privata di sessione", explicitCsv, StringComparison.Ordinal);
        var explicitXlsx = Sheet(service.Export(data, explicitRequest, StudyExportFormat.Xlsx, Start.AddHours(2))
            .Files.Single(), 2);
        Assert.Contains("nota privata di sessione", explicitXlsx, StringComparison.Ordinal);
        var explicitJson = service.Export(data, explicitRequest, StudyExportFormat.Json, Start.AddHours(2));
        using var explicitDocument = JsonDocument.Parse(explicitJson.Files.Single().Content);
        Assert.Equal("nota privata di sessione",
            explicitDocument.RootElement.GetProperty("sessions")[0].GetProperty("notes").GetString());
    }
}
