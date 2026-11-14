using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class NotificationCenterServiceTests
{
    [Fact]
    public void UpcomingCenterIncludesOnlyActiveTasksAndNonCancelledCalendarEvents()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "lecture",
            Title = "Lezione di Analisi",
            StartsAt = now.AddHours(2),
            EndsAt = now.AddHours(3),
            Verification = CalendarEventVerification.Official
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "cancelled",
            Title = "Evento annullato",
            StartsAt = now.AddHours(1),
            EndsAt = now.AddHours(2),
            IsCancelled = true
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "later",
            Title = "Evento futuro",
            StartsAt = now.AddDays(2),
            EndsAt = now.AddDays(2).AddHours(1)
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "overdue",
            Title = "Consegna scaduta",
            DueAt = now.AddHours(-1),
            Status = StudyTaskStatus.Todo
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "due",
            Title = "Preparare esercizi",
            DueAt = now.AddHours(5),
            Status = StudyTaskStatus.InProgress
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "completed",
            Title = "Task completato",
            DueAt = now.AddHours(1),
            Status = StudyTaskStatus.Completed
        });

        var notifications = new NotificationCenterService().GetUpcoming(data, now);

        Assert.Equal(new[] { "overdue", "lecture", "due" },
            notifications.Select(x => x.Id.Replace("task:", "").Replace("event:", "")));
        Assert.Contains(notifications, x => x.Title == "Consegna scaduta" && x.Detail.StartsWith("Scaduta"));
        Assert.DoesNotContain(notifications, x => x.Title is "Evento annullato" or "Evento futuro" or "Task completato");
    }

    [Fact]
    public void UpcomingCenterCanUseANarrowerReminderHorizon()
    {
        var now = new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "soon",
            Title = "Tra un'ora",
            DueAt = now.AddHours(1),
            Status = StudyTaskStatus.Todo
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "later",
            Title = "Tra quattro ore",
            DueAt = now.AddHours(4),
            Status = StudyTaskStatus.Todo
        });

        var notifications = new NotificationCenterService().GetUpcoming(data, now, TimeSpan.FromHours(2));

        Assert.Single(notifications);
        Assert.Equal("task:soon", notifications[0].Id);
    
    [Fact]
    public void DismissalKeyIsDeterministicAndDoesNotContainNotificationText()
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var item = new StudyNotificationItem(
            "task:task-1", "Titolo privato", "Dettaglio personale", now, "Attività");

        var key = NotificationCenterService.GetDismissalKey(item);

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9A-F]{64}$", key);
        Assert.DoesNotContain("Titolo privato", key);
        Assert.DoesNotContain("Dettaglio personale", key);
        Assert.Equal(key, NotificationCenterService.GetDismissalKey(item));
    }

    [Fact]
    public void DismissalIsIdempotentAndScopedToOneOccurrence()
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData();
        var service = new NotificationCenterService();
        var item = new StudyNotificationItem("task:task-1", "Consegna", "In scadenza", now, "Attività");

        Assert.True(service.Dismiss(data, item, now.AddMinutes(1)));
        Assert.False(service.Dismiss(data, item, now.AddMinutes(2)));
        Assert.True(service.IsDismissed(data, item));
        Assert.Single(data.NotificationDismissals);
        Assert.Equal(now.AddMinutes(1), data.NotificationDismissals[0].DismissedAt);

        var rescheduled = item with { OccursAt = now.AddHours(1) };
        Assert.False(service.IsDismissed(data, rescheduled));
    }

    [Fact]
    public void UpcomingReminderReappearsWhenItsScheduledOccurrenceChanges()
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData();
        var task = new StudyTaskData
        {
            Id = "task-1",
            Title = "Preparare esercizi",
            DueAt = now.AddHours(1),
            Status = StudyTaskStatus.Todo
        };
        data.StudyTasks.Add(task);
        var service = new NotificationCenterService();

        var first = Assert.Single(service.GetUpcoming(data, now));
        Assert.True(service.Dismiss(data, first, now.AddMinutes(5)));
        Assert.Empty(service.GetUpcoming(data, now));

        task.DueAt = now.AddHours(2);

        var rescheduled = Assert.Single(service.GetUpcoming(data, now));
        Assert.Equal(now.AddHours(2), rescheduled.OccursAt);
    }
}
}