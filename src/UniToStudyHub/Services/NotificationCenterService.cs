using System.Security.Cryptography;
using System.Text;
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
    /// <summary>
    /// Marks one reminder occurrence as read. The persisted key is a digest of
    /// its source identity and UTC occurrence instant, not its title or detail.
    /// </summary>
    public bool Dismiss(StudyData data, StudyNotificationItem notification, DateTimeOffset? dismissedAt = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(notification);

        var key = GetDismissalKey(notification);
        if (data.NotificationDismissals.Any(x => string.Equals(x.Key, key, StringComparison.Ordinal)))
            return false;

        data.NotificationDismissals.Add(new NotificationDismissalData
        {
            Key = key,
            DismissedAt = dismissedAt ?? DateTimeOffset.Now
        });
        return true;
    }

    public bool IsDismissed(StudyData data, StudyNotificationItem notification)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(notification);
        var key = GetDismissalKey(notification);
        return data.NotificationDismissals.Any(x => string.Equals(x.Key, key, StringComparison.Ordinal));
    }

    /// <summary>
    /// Produces a stable, content-free key. Changing the scheduled instant
    /// creates a new reminder occurrence that can be surfaced again.
    /// </summary>
    public static string GetDismissalKey(StudyNotificationItem notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var identity = string.Concat(
            notification.Id,
            "\n",
            notification.OccursAt.UtcDateTime.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

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

        return items.Where(x => !IsDismissed(data, x)).OrderBy(x => x.OccursAt).ToArray();
    }
}