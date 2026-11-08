using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class CalendarAndStudyLabTests
{
    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    [Fact]
    public void FocusTimerIsReconstructedFromTimestampsAcrossPauseAndRestart()
    {
        var data = Data();
        var service = new FocusSessionService();
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var session = service.Start(data, "analisi", "Limiti", 25, start);

        var running = service.Snapshot(data, session.Id, start.AddMinutes(10));
        Assert.Equal(600, running.ElapsedSeconds);
        Assert.Equal(900, running.RemainingSeconds);

        service.Pause(data, session.Id, start.AddMinutes(10));
        var paused = service.Snapshot(data, session.Id, start.AddMinutes(20));
        Assert.Equal(StudySessionState.Paused, paused.State);
        Assert.Equal(600, paused.ElapsedSeconds);

        service.Resume(data, session.Id, start.AddMinutes(30));
        service.Finish(data, session.Id, "Completata", start.AddMinutes(40));
        var completed = service.Snapshot(data, session.Id, start.AddMinutes(90));
        Assert.Equal(StudySessionState.Completed, completed.State);
        Assert.Equal(1200, completed.ElapsedSeconds);
        Assert.Equal(300, completed.RemainingSeconds);
    }

    [Fact]
    public void FocusTimerRejectsASecondActiveSession()
    {
        var data = Data();
        var service = new FocusSessionService();
        var now = DateTimeOffset.UtcNow;
        service.Start(data, "analisi", "Prima", 25, now);

        Assert.Throws<InvalidOperationException>(() => service.Start(data, "analisi", "Seconda", 25, now));
    }

    [Fact]
    public void IcsParserHandlesTimezoneAllDayExceptionsAndRecurrence()
    {
        const string ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:analisi-1
SUMMARY:Lezione di Analisi
DTSTART;TZID=Europe/Rome:20261104T090000
DTEND;TZID=Europe/Rome:20261104T110000
RRULE:FREQ=DAILY;COUNT=3
EXDATE;TZID=Europe/Rome:20261106T090000
LOCATION:Aula 1
END:VEVENT
BEGIN:VEVENT
UID:all-day-1
SUMMARY:Consegna
DTSTART;VALUE=DATE:20261110
DTEND;VALUE=DATE:20261111
END:VEVENT
END:VCALENDAR
""";
        var windowStart = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        var result = new IcsCalendarParser().Parse(ics, windowStart, windowStart.AddDays(20));

        Assert.Equal(3, result.Events.Count);
        var lectures = result.Events.Where(x => x.ExternalId?.StartsWith("analisi-1", StringComparison.Ordinal) == true).ToList();
        Assert.Equal(2, lectures.Count);
        Assert.DoesNotContain(lectures, x => x.StartsAt.ToUniversalTime().Date == new DateTime(2026, 11, 6));
        Assert.Contains(lectures, x => x.TimeZoneId == "Europe/Rome");
        var allDay = Assert.Single(result.Events, x => x.ExternalId == "all-day-1");
        Assert.True(allDay.IsAllDay);
        Assert.Equal(TimeSpan.FromDays(1), allDay.EndsAt - allDay.StartsAt);
    }

    [Fact]
    public void LegacyCalendarMergeKeepsMissingEventsWhenSqliteIsAlreadyPopulated()
    {
        var data = Data();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "sqlite-1",
            Source = CalendarEventSource.Ics,
            ExternalId = "uid-existing",
            Title = "Versione SQLite"
        });

        var legacy = new[]
        {
            new CalendarEvent
            {
                Id = "sqlite-1",
                Source = CalendarEventSource.Ics,
                ExternalId = "uid-existing",
                Title = "Versione legacy duplicata"
            },
            new CalendarEvent
            {
                Id = "legacy-duplicate-id",
                Source = CalendarEventSource.Ics,
                ExternalId = "uid-existing",
                Title = "Duplicato per UID"
            },
            new CalendarEvent
            {
                Id = "legacy-new",
                Source = CalendarEventSource.Manual,
                Title = "Evento legacy mancante"
            }
        };

        var added = CalendarEventStore.MergeLegacyEvents(data, legacy);
        var retried = CalendarEventStore.MergeLegacyEvents(data, legacy);

        Assert.Equal(1, added);
        Assert.Equal(0, retried);
        Assert.Equal(2, data.CalendarEvents.Count);
        Assert.Contains(data.CalendarEvents, x => x.Id == "legacy-new");
        Assert.Equal("Versione SQLite", data.CalendarEvents.Single(x => x.Id == "sqlite-1").Title);
    }

    [Fact]
    public void IcsImportIsIdempotentAndAppliesCancellation()
    {
        const string first = """
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:event-1
SUMMARY:Lezione
DTSTART;TZID=UTC:20261104T090000
DTEND;TZID=UTC:20261104T100000
END:VEVENT
END:VCALENDAR
""";
        const string cancelled = """
BEGIN:VCALENDAR
BEGIN:VEVENT
UID:event-1
SEQUENCE:2
STATUS:CANCELLED
SUMMARY:Lezione
DTSTART;TZID=UTC:20261104T090000
DTEND;TZID=UTC:20261104T100000
END:VEVENT
END:VCALENDAR
""";
        var data = Data();
        var service = new IcsImportService();
        var firstResult = service.ImportText(first, data);
        var secondResult = service.ImportText(first, data);
        var cancellation = service.ImportText(cancelled, data);

        Assert.Equal(1, firstResult.Added);
        Assert.Equal(0, secondResult.Added);
        Assert.Equal(1, cancellation.Updated);
        Assert.True(Assert.Single(data.CalendarEvents).IsCancelled);
        Assert.Empty(CalendarService.GetEventsInRange(data.CalendarEvents,
            new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void CalendarCrudValidatesIntervalsAndKeepsMidnightEventsSeparate()
    {
        var data = Data();
        var service = new CalendarApplicationService();
        var start = new DateTimeOffset(2026, 11, 4, 22, 0, 0, TimeSpan.Zero);
        var item = new CalendarEvent
        {
            Title = "Studio notturno",
            Category = CalendarEventCategory.Study,
            StartsAt = start,
            EndsAt = start.AddHours(4),
            TimeZoneId = "UTC"
        };
        service.Add(data, item);
        Assert.Single(service.GetRange(data, start.Date, start.Date.AddDays(2)));

        var update = new CalendarEvent
        {
            Id = item.Id,
            Title = "Studio notturno aggiornato",
            Category = CalendarEventCategory.Study,
            StartsAt = start,
            EndsAt = start.AddHours(3),
            TimeZoneId = "UTC"
        };
        service.Update(data, update);
        Assert.Equal("Studio notturno aggiornato", data.CalendarEvents[0].Title);

        var invalid = new CalendarEvent
        {
            Title = "Intervallo errato",
            Category = CalendarEventCategory.Personal,
            StartsAt = start.AddHours(2),
            EndsAt = start.AddHours(1)
        };
        Assert.Throws<ArgumentException>(() => service.Add(data, invalid));

        var dstAllDay = new CalendarEvent
        {
            Title = "Giornata con cambio ora",
            Category = CalendarEventCategory.Personal,
            StartsAt = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1)),
            EndsAt = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.FromHours(2)),
            IsAllDay = true,
            TimeZoneId = "Europe/Rome"
        };
        CalendarService.Validate(dstAllDay);

        service.Delete(data, item.Id);
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ExamLinkIsIdempotentAndMarkedIndicative()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi scritto",
            PlannedDate = new DateTime(2026, 12, 10)
        });
        var service = new CalendarApplicationService();
        service.LinkPlannedExams(data, new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));
        service.LinkPlannedExams(data, new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.Zero));

        var linked = Assert.Single(data.CalendarEvents);
        Assert.Equal(CalendarEventSource.Planner, linked.Source);
        Assert.Equal(CalendarEventVerification.Indicative, linked.Verification);
        Assert.Equal("exam:exam-1", linked.ExternalId);
    }

    [Fact]
    public void FlashcardReviewPersistsHistoryAndSchedulesTheNextInterval()
    {
        var data = Data();
        var service = new FlashcardService();
        var card = service.Add(data, new FlashcardData
        {
            CourseId = "analisi",
            Front = "Definizione di limite",
            Back = "Testo",
            DueAt = DateTimeOffset.UtcNow
        });
        var first = service.Review(data, card.Id, RecallRating.Good,
            new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));
        var second = service.Review(data, card.Id, RecallRating.Good,
            new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(2, data.FlashcardReviews.Count);
        Assert.Equal(1, first.NewIntervalDays);
        Assert.Equal(6, second.NewIntervalDays);
        Assert.NotNull(card.LastReviewedAt);
        Assert.Equal(2, card.Repetitions);
    }

    [Fact]
    public void ProgressUsesStoredSessionsAndDoesNotInventAStreak()
    {
        var data = Data();
        var now = new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
        data.StudySessions.Add(new StudySessionData
        {
            CourseId = "analisi",
            Topic = "Limiti",
            PlannedMinutes = 25,
            StartedAt = now.AddMinutes(-25),
            EndedAt = now,
            LastResumedAt = null,
            State = StudySessionState.Completed,
            AccumulatedFocusSeconds = 1500
        });
        var progress = new ProgressService().Calculate(data, new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 7), now);

        Assert.Equal(25, progress.FocusMinutes);
        Assert.Equal(1, progress.CompletedSessions);
        Assert.Equal(1, progress.FocusStreakDays);
        Assert.Equal(1200, progress.WeeklyGoalMinutes);
    }

    [Fact]
    public void ErrorPatternsExposeEvidenceAndWeeklyReviewIsDataDriven()
    {
        var data = Data();
        var journal = new ErrorJournalService();
        journal.Add(data, new ErrorJournalEntryData
        {
            CourseId = "analisi",
            Topic = "Derivate",
            Question = "Derivata di x²",
            IncorrectAnswer = "x",
            CorrectAnswer = "2x",
            ErrorType = "Calcolo"
        });
        journal.Add(data, new ErrorJournalEntryData
        {
            CourseId = "analisi",
            Topic = "Derivate",
            Question = "Derivata di x³",
            IncorrectAnswer = "3x",
            CorrectAnswer = "3x²",
            ErrorType = "Calcolo"
        });
        var patterns = new ErrorPatternService().Detect(data);
        Assert.Single(patterns);
        Assert.Equal(2, patterns[0].Count);
        var review = new WeeklyReviewService().Create(data, new DateOnly(2026, 11, 2),
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero));
        Assert.Contains(review.Observations, x => x.Contains("Tempo Focus", StringComparison.Ordinal));
        Assert.Contains(review.SuggestedActions, x => x.Contains("diario errori", StringComparison.Ordinal));
    }

    [Fact]
    public void DistractionAndEnergyAreVoluntaryAndLocal()
    {
        var data = Data();
        var session = new FocusSessionService().Start(data, "analisi", "Studio", 25,
            new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero));
        var distraction = new DistractionService().Add(data, "Controllare il telefono", session.Id);
        var energy = new EnergyService().Add(data, 3, 4, "Va bene");

        Assert.False(distraction.ResolvedAt.HasValue);
        Assert.Equal(4, energy.FocusLevel);
        Assert.Single(data.Distractions);
    }
}
