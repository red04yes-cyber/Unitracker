using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record StudyNotificationItem(
    string Id,
    string Title,
    string Detail,
    DateTimeOffset OccursAt,
    string Kind);

/// <summary>
/// Builds local reminders from the user's persisted calendar, tasks and unread
/// Moodle discussions. It does not invent events or send data remotely.
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
        var window = horizon ?? TimeSpan.FromHours(24);
        if (window < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(horizon), "The reminder horizon cannot be negative.");

        var until = at + window;
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

        items.AddRange(data.Announcements
            .Where(AnnouncementSyncService.IsInboxItem)
            .Where(x => x.IsNew)
            .Select(x => new StudyNotificationItem(
                $"announcement:{x.Id}",
                string.IsNullOrWhiteSpace(x.Title) ? "Aggiornamento Moodle" : x.Title,
                string.Join(" · ", new[] { x.CourseName, x.Summary }.Where(value => !string.IsNullOrWhiteSpace(value))),
                ToUtcTimestamp(x.UpdatedAt, at),
                "Moodle")));

        return items.OrderBy(x => x.OccursAt).ThenBy(x => x.Id, StringComparer.Ordinal).ToArray();
    }

    private static DateTimeOffset ToUtcTimestamp(DateTime value, DateTimeOffset fallback)
    {
        if (value == default)
            return fallback;

        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
        return new DateTimeOffset(utc);
    }
}
