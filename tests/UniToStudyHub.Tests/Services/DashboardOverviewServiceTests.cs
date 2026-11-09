using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class DashboardOverviewServiceTests
{
    private readonly DashboardOverviewService _service = new(new ProgressService());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private static DateTimeOffset LocalAt(DateOnly day, int hour = 10, int minute = 0)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(new TimeOnly(hour, minute)), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static StudyData Data() => new()
    {
        WeeklyStudyHours = 2,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi I", Cfu = 9 },
            new() { Id = "fisica", Name = "Fisica", Cfu = 6 }
        }
    };

    private static StudyTaskData Task(
        string id,
        string title,
        DateTimeOffset? dueAt,
        int priority = 3,
        StudyTaskStatus status = StudyTaskStatus.Todo,
        string courseId = "analisi") => new()
    {
        Id = id,
        Title = title,
        DueAt = dueAt,
        Priority = priority,
        Status = status,
        CourseId = courseId
    };

    [Fact]
    public void NextExamSelectsNearestFutureIncompleteExamAndCalculatesCalendarDays()
    {
        var data = Data();
        var today = Today;
        var now = LocalAt(today, 10);
        data.Exams.Add(new Exam { Id = "past", Name = "Esame passato", PlannedDate = today.AddDays(-1).ToDateTime(TimeOnly.MinValue) });
        data.Exams.Add(new Exam { Id = "completed", Name = "Già superato", PlannedDate = today.ToDateTime(TimeOnly.MinValue), Completed = true });
        data.Exams.Add(new Exam { Id = "later", Name = "Esame più lontano", PlannedDate = today.AddDays(5).ToDateTime(TimeOnly.MinValue) });
        data.Exams.Add(new Exam { Id = "next", Name = "Analisi", CourseId = "analisi", CourseName = "Analisi I", PlannedDate = today.AddDays(2).ToDateTime(TimeOnly.MinValue) });

        var result = _service.Calculate(data, now);

        Assert.Equal("next", result.NextExam?.Id);
        Assert.Equal(2, result.DaysUntilNextExam);
    }

    [Fact]
    public void TodayTasksIncludeOverdueAndDueTodayButExcludeFutureUndatedAndClosedTasks()
    {
        var data = Data();
        var today = Today;
        var now = LocalAt(today);
        data.StudyTasks.Add(Task("tomorrow", "Domani", LocalAt(today.AddDays(1))));
        data.StudyTasks.Add(Task("undated", "Senza data", null));
        data.StudyTasks.Add(Task("completed", "Completata", LocalAt(today), status: StudyTaskStatus.Completed));
        data.StudyTasks.Add(Task("today", "Consegna oggi", LocalAt(today, 8)));
        data.StudyTasks.Add(Task("late", "In ritardo", LocalAt(today.AddDays(-2))));

        var result = _service.Calculate(data, now);

        Assert.Equal(new[] { "late", "today" }, result.TodayTasks.Select(task => task.Id));
    }

    [Fact]
    public void WeeklyStudyTimeUsesUniqueActivityLedgerMinutesAndClampsGoalProgress()
    {
        var data = Data();
        var today = Today;
        var now = LocalAt(today, 11);
        var start = LocalAt(today, 9);
        var end = start.AddHours(1);

        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "manual-a",
            CourseId = "analisi",
            Title = "Studio capitolo 1",
            StartedAt = start,
            EndedAt = end,
            EffectiveSeconds = 3600,
            Status = StudyActivityStatus.Completed
        });
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "manual-b",
            CourseId = "fisica",
            Title = "Intervallo sovrapposto",
            StartedAt = start,
            EndedAt = end,
            EffectiveSeconds = 3600,
            Status = StudyActivityStatus.Completed
        });

        var result = _service.Calculate(data, now);

        Assert.Equal(60, result.RecordedStudyMinutes);
        Assert.Equal(120, result.WeeklyGoalMinutes);
        Assert.Equal(50, result.WeeklyGoalProgressPercent);
    }

    [Fact]
    public void UpcomingDeadlinesIncludeTasksAndNonCancelledDeadlineEventsWithinFourteenDays()
    {
        var data = Data();
        var today = Today;
        var now = LocalAt(today);
        data.StudyTasks.Add(Task("task-today", "Consegna esercizi", LocalAt(today, 17)));
        data.StudyTasks.Add(Task("task-outside", "Consegna lontana", LocalAt(today.AddDays(15))));
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "official-deadline",
            Title = "Scadenza iscrizione",
            Category = CalendarEventCategory.Deadline,
            StartsAt = LocalAt(today.AddDays(2), 9),
            EndsAt = LocalAt(today.AddDays(2), 10),
            Source = CalendarEventSource.Moodle,
            Verification = CalendarEventVerification.Official
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "cancelled",
            Title = "Scadenza annullata",
            Category = CalendarEventCategory.Deadline,
            StartsAt = LocalAt(today.AddDays(1)),
            EndsAt = LocalAt(today.AddDays(1)).AddHours(1),
            IsCancelled = true
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "lecture",
            Title = "Lezione",
            Category = CalendarEventCategory.Lecture,
            StartsAt = LocalAt(today.AddDays(1)),
            EndsAt = LocalAt(today.AddDays(1)).AddHours(1)
        });

        var result = _service.Calculate(data, now);

        Assert.Equal(new[] { "task-today", "official-deadline" }, result.UpcomingDeadlines.Select(item => item.Id));
        var official = Assert.Single(result.UpcomingDeadlines, item => item.Id == "official-deadline");
        Assert.Equal("Moodle", official.SourceDisplay);
        Assert.Equal("Fonte ufficiale", official.VerificationDisplay);
    }

    [Fact]
    public void SuggestionPrioritizesOverdueTaskByUrgencyThenPriority()
    {
        var data = Data();
        var today = Today;
        var now = LocalAt(today);
        data.StudyTasks.Add(Task("low", "Task oggi meno urgente", LocalAt(today, 18), priority: 2));
        data.StudyTasks.Add(Task("high", "Task arretrato prioritario", LocalAt(today.AddDays(-1)), priority: 5));
        data.StudyTasks.Add(Task("future", "Task futuro", LocalAt(today.AddDays(20)), priority: 5));

        var result = _service.Calculate(data, now);

        Assert.Equal("task", result.Suggestion.Kind);
        Assert.Equal("high", result.Suggestion.TaskId);
        Assert.Contains("ritardo", result.Suggestion.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SuggestionFallsBackToDueFlashcardsAndIgnoresSuspendedCards()
    {
        var data = Data();
        var now = LocalAt(Today);
        data.Flashcards.Add(new FlashcardData { Id = "due-1", CourseId = "fisica", DueAt = now.AddHours(-1) });
        data.Flashcards.Add(new FlashcardData { Id = "due-2", CourseId = "fisica", DueAt = now.AddHours(-1) });
        data.Flashcards.Add(new FlashcardData { Id = "suspended", CourseId = "analisi", DueAt = now.AddHours(-2), IsSuspended = true });

        var result = _service.Calculate(data, now);

        Assert.Equal("flashcards", result.Suggestion.Kind);
        Assert.Equal("fisica", result.Suggestion.CourseId);
        Assert.Contains("Fisica", result.Suggestion.Title, StringComparison.Ordinal);
        Assert.Contains("2 schede", result.Suggestion.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void SuggestionFallsBackToNextExamWhenNoTasksOrCardsAreDue()
    {
        var data = Data();
        var now = LocalAt(Today);
        data.Exams.Add(new Exam
        {
            Id = "exam",
            Name = "Analisi",
            CourseId = "analisi",
            CourseName = "Analisi I",
            PlannedDate = Today.AddDays(3).ToDateTime(TimeOnly.MinValue)
        });

        var result = _service.Calculate(data, now);

        Assert.Equal("exam", result.Suggestion.Kind);
        Assert.Contains("Analisi I", result.Suggestion.Title, StringComparison.Ordinal);
        Assert.Contains("3 giorni", result.Suggestion.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingDataProducesExplicitSetupSuggestionAndNoGoalProgress()
    {
        var data = Data();
        data.WeeklyStudyHours = 0;

        var result = _service.Calculate(data, LocalAt(Today));

        Assert.Null(result.NextExam);
        Assert.Null(result.DaysUntilNextExam);
        Assert.Empty(result.TodayTasks);
        Assert.Empty(result.UpcomingDeadlines);
        Assert.Equal(0, result.RecordedStudyMinutes);
        Assert.Equal(0, result.WeeklyGoalMinutes);
        Assert.Equal(0, result.WeeklyGoalProgressPercent);
        Assert.False(result.HasWeeklyGoal);
        Assert.Equal("setup", result.Suggestion.Kind);
    }

    [Fact]
    public void CalculationDoesNotMutateTheSuppliedStudySnapshot()
    {
        var data = Data();
        data.StudyTasks.Add(Task("task", "Consegna", LocalAt(Today)));
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "deadline",
            Title = "Scadenza",
            Category = CalendarEventCategory.Deadline,
            StartsAt = LocalAt(Today.AddDays(1)),
            EndsAt = LocalAt(Today.AddDays(1)).AddHours(1)
        });
        var tasksBefore = data.StudyTasks.ToArray();
        var eventsBefore = data.CalendarEvents.ToArray();
        var activitiesBefore = data.StudyActivities.ToArray();

        _service.Calculate(data, LocalAt(Today));

        Assert.Equal(tasksBefore, data.StudyTasks);
        Assert.Equal(eventsBefore, data.CalendarEvents);
        Assert.Equal(activitiesBefore, data.StudyActivities);
    }
}
