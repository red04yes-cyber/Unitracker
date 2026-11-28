using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class WeeklyReviewServiceTests
{
    private static readonly DateOnly WeekStart = new(2026, 11, 23);

    private static DateTimeOffset LocalAt(DateOnly day, int hour, int minute = 0)
    {
        var local = day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    [Fact]
    public void CreateReturnsSevenDayWindowAndDoesNotInventProgressForEmptyData()
    {
        var now = LocalAt(WeekStart.AddDays(2), 12);
        var review = new WeeklyReviewService().Create(new StudyData(), WeekStart, now);

        Assert.Equal(WeekStart, review.WeekStart);
        Assert.Equal(WeekStart, review.Progress.From);
        Assert.Equal(WeekStart.AddDays(6), review.Progress.To);
        Assert.Equal(0, review.Progress.RecordedSeconds);
        Assert.Equal(0, review.Progress.FocusMinutes);
        Assert.Equal(0, review.Progress.CompletedTasks);
        Assert.Empty(review.ErrorPatterns);
        Assert.Single(review.SuggestedActions);
        Assert.Contains("registra solo attività realmente svolte", review.SuggestedActions[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateCountsTheUnionOfOverlappingLedgerIntervalsAndReportsTheOverlap()
    {
        var data = new StudyData();
        var day = WeekStart.AddDays(2);
        var ledger = new ActivityLedgerService();
        var firstStart = LocalAt(day, 9);
        var secondStart = LocalAt(day, 9, 30);

        ledger.AddManual(data, new StudyActivityData
        {
            Title = "Studio primo intervallo",
            StartedAt = firstStart,
            EndedAt = LocalAt(day, 10),
            EffectiveSeconds = 3600,
            TimeZoneId = TimeZoneInfo.Local.Id
        }, LocalAt(day, 12));
        ledger.AddManual(data, new StudyActivityData
        {
            Title = "Studio secondo intervallo",
            StartedAt = secondStart,
            EndedAt = LocalAt(day, 10, 30),
            EffectiveSeconds = 3600,
            TimeZoneId = TimeZoneInfo.Local.Id
        }, LocalAt(day, 12));

        var review = new WeeklyReviewService().Create(data, WeekStart, LocalAt(day, 12));

        Assert.Equal(90 * 60, review.Progress.RecordedSeconds);
        Assert.Equal(30 * 60, review.Progress.OverlapSeconds);
        Assert.Contains(review.SuggestedActions, action => action.Contains("attività sovrapposte", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateIncludesSundayActivitiesButExcludesAdjacentWeeks()
    {
        var data = new StudyData();
        var ledger = new ActivityLedgerService();
        var before = WeekStart.AddDays(-1);
        var sunday = WeekStart.AddDays(6);
        var after = WeekStart.AddDays(7);
        var now = LocalAt(sunday, 23, 45);

        AddActivity(ledger, data, before);
        AddActivity(ledger, data, sunday);
        AddActivity(ledger, data, after);

        var review = new WeeklyReviewService().Create(data, WeekStart, now);

        Assert.Equal(15 * 60, review.Progress.RecordedSeconds);
    }

    [Fact]
    public void CreateCountsOnlyTasksCompletedInsideTheRequestedWeek()
    {
        var data = new StudyData();
        var tasks = new StudyTaskService();
        var completedInside = tasks.Add(data, new StudyTaskData
        {
            Id = "inside",
            Title = "Esercizi della settimana",
            EstimatedMinutes = 45,
            CreatedAt = LocalAt(WeekStart, 8)
        });
        var completedOutside = tasks.Add(data, new StudyTaskData
        {
            Id = "outside",
            Title = "Esercizi della settimana precedente",
            EstimatedMinutes = 30,
            CreatedAt = LocalAt(WeekStart.AddDays(-7), 8)
        });
        tasks.Complete(data, completedInside.Id, LocalAt(WeekStart.AddDays(2), 17));
        tasks.Complete(data, completedOutside.Id, LocalAt(WeekStart.AddDays(-1), 17));

        var review = new WeeklyReviewService().Create(data, WeekStart, LocalAt(WeekStart.AddDays(2), 18));

        Assert.Equal(1, review.Progress.CompletedTasks);
        Assert.Equal(45, review.Progress.CompletedTaskEstimatedMinutes);
    }

    [Fact]
    public void CreateReportsCurrentDueBacklogButExcludesSuspendedFlashcards()
    {
        var data = new StudyData();
        var now = LocalAt(WeekStart.AddDays(2), 12);
        var flashcards = new FlashcardService();
        flashcards.Add(data, new FlashcardData
        {
            Id = "due",
            Front = "Domanda dovuta",
            Back = "Risposta",
            DueAt = now.AddDays(-1)
        });
        flashcards.Add(data, new FlashcardData
        {
            Id = "suspended",
            Front = "Domanda sospesa",
            Back = "Risposta",
            DueAt = now.AddDays(-2),
            IsSuspended = true
        });

        var review = new WeeklyReviewService().Create(data, WeekStart, now);

        Assert.Equal(1, review.Progress.DueFlashcards);
        Assert.Contains(review.SuggestedActions, action => action.Contains("flashcard scadute", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateGroupsRepeatedUnresolvedErrorsAndExcludesResolvedEntries()
    {
        var data = new StudyData();
        var errors = new ErrorJournalService();
        var now = LocalAt(WeekStart.AddDays(2), 12);
        AddError(errors, data, "limiti-1", "Limiti", now);
        AddError(errors, data, "limiti-2", "Limiti", now);
        var resolved = AddError(errors, data, "limiti-resolved", "Limiti", now);
        errors.Review(data, resolved.Id, recalled: true, now: now.AddDays(1));
        errors.Review(data, resolved.Id, recalled: true, now: now.AddDays(2));
        errors.Review(data, resolved.Id, recalled: true, now: now.AddDays(3));

        var review = new WeeklyReviewService().Create(data, WeekStart, now.AddDays(4));

        var pattern = Assert.Single(review.ErrorPatterns);
        Assert.Equal("Limiti", pattern.Topic);
        Assert.Equal(2, pattern.Count);
        Assert.Equal(2, review.Progress.OpenErrors);
        Assert.Contains(review.SuggestedActions, action => action.Contains("pattern di errore", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateKeepsFocusSessionMinutesSeparateFromUniqueLedgerSeconds()
    {
        var data = new StudyData();
        var day = WeekStart.AddDays(2);
        var start = LocalAt(day, 9);
        var end = LocalAt(day, 9, 25);
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, null, "Ripasso", 25, start);
        focus.Finish(data, session.Id, "Completata", end);
        ledger.SyncFocusSession(data, session, end);

        var review = new WeeklyReviewService().Create(data, WeekStart, LocalAt(day, 12));

        Assert.Equal(25, review.Progress.FocusMinutes);
        Assert.Equal(25 * 60, review.Progress.RecordedSeconds);
        Assert.Equal(0, review.Progress.OverlapSeconds);
    }

    [Fact]
    public void CreateDoesNotMutateTheSourceStudyData()
    {
        var data = new StudyData();
        var now = LocalAt(WeekStart.AddDays(2), 12);
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task",
            Title = "Task",
            CreatedAt = now.AddDays(-1)
        });
        new StudyTaskService().Complete(data, task.Id, now);
        var activity = new ActivityLedgerService().AddManual(data, new StudyActivityData
        {
            Id = "activity",
            Title = "Studio",
            StartedAt = now.AddMinutes(-30),
            EndedAt = now,
            CreatedAt = now.AddMinutes(-30),
            EffectiveSeconds = 1800,
            TimeZoneId = TimeZoneInfo.Local.Id
        }, now);
        var taskCount = data.StudyTasks.Count;
        var activityCount = data.StudyActivities.Count;
        var revisionCount = data.ActivityRevisions.Count;
        var completedAt = task.CompletedAt;
        var updatedAt = activity.UpdatedAt;

        _ = new WeeklyReviewService().Create(data, WeekStart, now);

        Assert.Equal(taskCount, data.StudyTasks.Count);
        Assert.Equal(activityCount, data.StudyActivities.Count);
        Assert.Equal(revisionCount, data.ActivityRevisions.Count);
        Assert.Equal(completedAt, task.CompletedAt);
        Assert.Equal(updatedAt, activity.UpdatedAt);
        Assert.Same(task, Assert.Single(data.StudyTasks));
        Assert.Same(activity, Assert.Single(data.StudyActivities));
    }

    [Fact]
    public void CreateRejectsNullStudyData()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WeeklyReviewService().Create(null!, WeekStart, LocalAt(WeekStart, 12)));
    }

    [Fact]
    public void CreateRejectsAWeekStartThatCannotContainSixFollowingDays()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WeeklyReviewService().Create(new StudyData(), DateOnly.MaxValue, LocalAt(WeekStart, 12)));
    }

    private static void AddActivity(ActivityLedgerService ledger, StudyData data, DateOnly day)
    {
        var start = LocalAt(day, 23);
        ledger.AddManual(data, new StudyActivityData
        {
            Title = $"Studio {day:yyyy-MM-dd}",
            StartedAt = start,
            EndedAt = start.AddMinutes(15),
            CreatedAt = start,
            EffectiveSeconds = 15 * 60,
            TimeZoneId = TimeZoneInfo.Local.Id
        }, start.AddMinutes(20));
    }

    private static ErrorJournalEntryData AddError(
        ErrorJournalService service,
        StudyData data,
        string id,
        string topic,
        DateTimeOffset now) =>
        service.Add(data, new ErrorJournalEntryData
        {
            Id = id,
            Topic = topic,
            CreatedAt = now.AddDays(-1),
            Question = $"Domanda {id}",
            CorrectAnswer = "Risposta corretta",
            ErrorType = "Concetto"
        });
}
