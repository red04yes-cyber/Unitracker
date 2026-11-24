using System.Collections.ObjectModel;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record AnnouncementUpsertResult(Announcement Item, bool Created, bool ContentChanged);

/// <summary>
/// Normalizes Moodle inbox records and keeps their unread state stable across
/// repeated synchronization. Course-activity completion rows are deliberately
/// not inbox items: their legacy IsNew flag represents completion state.
/// </summary>
public static class AnnouncementSyncService
{
    public const int MaximumSourceKeyLength = 256;
    public const int MaximumCourseNameLength = 200;
    public const int MaximumTitleLength = 300;
    public const int MaximumSummaryLength = 1800;
    public const int MaximumUrlLength = 2048;

    private static readonly HashSet<string> SensitiveQueryKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "wstoken", "access_token", "api_key", "apikey", "password",
        "signature", "sig", "sesskey", "authorization"
    };

    public static AnnouncementUpsertResult Upsert(
        ObservableCollection<Announcement> announcements,
        string sourceKey,
        string? courseName,
        string? title,
        string? summary,
        string? url,
        DateTime updatedAt)
    {
        ArgumentNullException.ThrowIfNull(announcements);

        if (string.IsNullOrWhiteSpace(sourceKey) ||
            sourceKey.Length > MaximumSourceKeyLength ||
            sourceKey.Any(char.IsControl))
            throw new ArgumentException("The announcement source key is invalid.", nameof(sourceKey));

        var normalizedSourceKey = sourceKey.Trim();
        var normalizedCourse = NormalizeText(courseName, MaximumCourseNameLength);
        var normalizedTitle = NormalizeText(title, MaximumTitleLength);
        if (normalizedTitle.Length == 0)
            normalizedTitle = "Aggiornamento Moodle";
        var normalizedSummary = NormalizeText(summary, MaximumSummaryLength, allowLineBreaks: true);
        var normalizedUrl = NormalizeUrl(url);
        var normalizedUpdatedAt = NormalizeTimestamp(updatedAt);

        var existing = announcements.FirstOrDefault(item =>
            string.Equals(item.SourceKey, normalizedSourceKey, StringComparison.Ordinal));

        if (existing is null)
        {
            var created = new Announcement
            {
                SourceKey = normalizedSourceKey,
                CourseName = normalizedCourse,
                Title = normalizedTitle,
                Summary = normalizedSummary,
                Url = normalizedUrl,
                UpdatedAt = normalizedUpdatedAt,
                IsNew = true
            };
            announcements.Add(created);
            return new AnnouncementUpsertResult(created, Created: true, ContentChanged: true);
        }

        var contentChanged =
            !string.Equals(existing.CourseName, normalizedCourse, StringComparison.Ordinal) ||
            !string.Equals(existing.Title, normalizedTitle, StringComparison.Ordinal) ||
            !string.Equals(existing.Summary, normalizedSummary, StringComparison.Ordinal) ||
            !string.Equals(existing.Url, normalizedUrl, StringComparison.Ordinal);

        existing.CourseName = normalizedCourse;
        existing.Title = normalizedTitle;
        existing.Summary = normalizedSummary;
        existing.Url = normalizedUrl;
        existing.UpdatedAt = normalizedUpdatedAt;

        // A read item stays read on an identical sync. Meaningful source
        // changes create a new inbox entry without duplicating the record.
        if (contentChanged)
            existing.IsNew = true;

        return new AnnouncementUpsertResult(existing, Created: false, ContentChanged: contentChanged);
    }

    public static bool IsInboxItem(Announcement? announcement) =>
        announcement is not null &&
        announcement.SourceKey.StartsWith("moodle-discussion:", StringComparison.Ordinal);

    public static bool MarkRead(StudyData data, string? announcementId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (string.IsNullOrWhiteSpace(announcementId))
            return false;

        var item = data.Announcements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, announcementId, StringComparison.Ordinal) &&
            IsInboxItem(candidate));
        if (item is null || !item.IsNew)
            return false;

        item.IsNew = false;
        return true;
    }

    public static int MarkAllRead(StudyData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var changed = 0;
        foreach (var item in data.Announcements.Where(item => IsInboxItem(item) && item.IsNew))
        {
            item.IsNew = false;
            changed++;
        }

        return changed;
    }

    private static string NormalizeText(string? value, int maximumLength, bool allowLineBreaks = false)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var normalized = new string(value
            .Where(character =>
                !char.IsControl(character) ||
                (allowLineBreaks && (character == '\r' || character == '\n' || character == '\t')))
            .ToArray())
            .Trim();

        if (normalized.Length <= maximumLength)
            return normalized;

        var length = maximumLength;
        if (length > 0 && char.IsHighSurrogate(normalized[length - 1]))
            length--;
        return normalized[..length];
    }

    private static string NormalizeUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumUrlLength ||
            value.Any(char.IsControl))
            return string.Empty;

        var candidate = value.Trim();
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
            return string.Empty;

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var encodedKey = pair.Split('=', 2)[0].Replace('+', ' ');
            var key = Uri.UnescapeDataString(encodedKey);
            if (SensitiveQueryKeys.Contains(key))
                return string.Empty;
        }

        return uri.AbsoluteUri.Length <= MaximumUrlLength ? uri.AbsoluteUri : string.Empty;
    }

    private static DateTime NormalizeTimestamp(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
