using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record StudyNotificationItem(
    string Id,
    string Title,
    string Detail,
    DateTimeOffset OccursAt,
    string Kind);

/// <summary>
/// Builds transparent, local reminders from the user's persisted calendar and task data.
/// It does not invent events, send data remotely, or persist read state.
/// </summary>
public sealed class NotificationCenterService
{
    public IReadOnlyList<StudyNotificationItem> GetUpcoming(
        StudyData data,
        DateTimeOffset? now = null,
        TimeSpan? horizon = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var at = now ?? DateTimeOffset.Now;
        var until = at + (horizon ?? TimeSpan.FromHours(24));
        var items = new List<StudyNotificationItem>();

        items.AddRange(data.CalendarEvents
            .Where(x => !x.IsCancelled && x.EndsAt >= at && x.StartsAt <= until)
            .Select(x => new StudyNotificationItem(
                $"event:{x.Id}",
                x.Title,
                $"Calendario · {x.StartsAt.ToLocalTime():dd/MM HH:mm} · {x.Verification}",
                x.StartsAt,
                "Calendario")));

        items.AddRange(data.StudyTasks
            .Where(x => (x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress) &&
                        x.DueAt is not null && x.DueAt <= until)
            .Select(x => new StudyNotificationItem(
                $"task:{x.Id}",
                x.Title,
                x.DueAt < at ? $"Scaduta · {x.DueAt:dd/MM HH:mm}" : $"In scadenza · {x.DueAt:dd/MM HH:mm}",
                x.DueAt!.Value,
                "Attività")));

        return items.OrderBy(x => x.OccursAt).ToArray();
    }
}