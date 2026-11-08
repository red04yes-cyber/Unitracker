using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class StudyLabPersistenceTests
{
    [Fact]
    public async Task CalendarStudyLabAndPdfRoundTripThroughSqlite()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData
        {
            DegreeTotalCfu = 180,
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
            }
        };
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "calendar-1",
            Title = "Lezione",
            Category = CalendarEventCategory.Lecture,
            StartsAt = start,
            EndsAt = start.AddHours(2),
            CourseId = "analisi",
            CourseName = "Analisi",
            Source = CalendarEventSource.Manual,
            Verification = CalendarEventVerification.Unverified,
            TimeZoneId = "UTC"
        });
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            CourseId = "analisi",
            Topic = "Limiti",
            PlannedMinutes = 25,
            StartedAt = start,
            EndedAt = start.AddMinutes(25),
            LastResumedAt = null,
            AccumulatedFocusSeconds = 1500,
            State = StudySessionState.Completed
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Ripassare i limiti",
            EstimatedMinutes = 30,
            Status = StudyTaskStatus.Completed,
            CompletedAt = start.AddMinutes(30)
        });
        data.ErrorJournalEntries.Add(new ErrorJournalEntryData
        {
            Id = "error-1",
            CourseId = "analisi",
            Topic = "Derivate",
            Question = "Derivata di x²",
            IncorrectAnswer = "x",
            CorrectAnswer = "2x",
            ErrorType = "Calcolo"
        });
        data.Flashcards.Add(new FlashcardData
        {
            Id = "card-1",
            CourseId = "analisi",
            Front = "Limite",
            Back = "Definizione",
            DueAt = start,
            EaseFactor = 2.5m
        });
        data.FlashcardReviews.Add(new FlashcardReviewData
        {
            Id = "review-1",
            FlashcardId = "card-1",
            ReviewedAt = start,
            Rating = RecallRating.Good,
            NewIntervalDays = 1,
            EaseFactorAfter = 2.5m
        });
        data.Distractions.Add(new DistractionData
        {
            Id = "distraction-1",
            SessionId = "session-1",
            Text = "Notifica",
            CreatedAt = start
        });
        data.EnergyCheckIns.Add(new EnergyCheckInData
        {
            Id = "energy-1",
            RecordedAt = start,
            EnergyLevel = 3,
            FocusLevel = 4
        });
        data.PdfDocuments.Add(new PdfDocumentData
        {
            Id = "pdf-1",
            FileName = "analisi.pdf",
            Sha256 = new string('a', 64),
            PageCount = 1,
            CourseId = "analisi",
            Pages = new ObservableCollection<PdfPageData>
            {
                new() { PageNumber = 1, Text = "Testo di prova" }
            }
        });

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded.CalendarEvents);
        Assert.Equal("Lezione", loaded.CalendarEvents[0].Title);
        Assert.Single(loaded.StudySessions);
        Assert.Equal(1500, loaded.StudySessions[0].AccumulatedFocusSeconds);
        Assert.Single(loaded.StudyTasks);
        Assert.Single(loaded.ErrorJournalEntries);
        Assert.Single(loaded.Flashcards);
        Assert.Single(loaded.FlashcardReviews);
        Assert.Single(loaded.Distractions);
        Assert.Single(loaded.EnergyCheckIns);
        Assert.Single(loaded.PdfDocuments);
        Assert.Equal("Testo di prova", loaded.PdfDocuments[0].Pages[0].Text);
    }

    [Fact]
    public async Task InvalidNewDataIsRejectedBeforeTheExistingDatabaseIsDeleted()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var valid = new StudyData
        {
            Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } }
        };
        valid.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            PlannedMinutes = 25,
            StartedAt = DateTimeOffset.UtcNow,
            State = StudySessionState.Completed,
            LastResumedAt = null,
            EndedAt = DateTimeOffset.UtcNow.AddMinutes(25),
            AccumulatedFocusSeconds = 1500
        });
        await store.SaveAsync(valid);

        var invalid = new StudyData { Courses = valid.Courses };
        invalid.StudySessions.Add(new StudySessionData
        {
            Id = "bad-session",
            PlannedMinutes = 0,
            State = StudySessionState.Running
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));
        var loaded = await store.LoadAsync();
        Assert.Single(loaded.StudySessions);
        Assert.Equal("session-1", loaded.StudySessions[0].Id);
    }
}
