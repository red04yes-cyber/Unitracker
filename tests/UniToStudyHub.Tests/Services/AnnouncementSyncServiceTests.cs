using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class AnnouncementSyncServiceTests
{
    private static readonly DateTime SyncedAt = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstSyncCreatesOneUnreadAnnouncement()
    {
        var announcements = new ObservableCollection<Announcement>();

        var result = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Nuovo appello",
            "Il docente ha pubblicato un aggiornamento.", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt);

        Assert.True(result.Created);
        Assert.True(result.ContentChanged);
        Assert.True(result.Item.IsNew);
        Assert.Single(announcements);
        Assert.Equal("moodle-discussion:42", result.Item.SourceKey);
    }

    [Fact]
    public void RepeatedIdenticalSyncPreservesReadStateAndDoesNotDuplicate()
    {
        var announcements = new ObservableCollection<Announcement>();
        var first = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Nuovo appello",
            "Dettagli", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt);
        first.Item.IsNew = false;

        var second = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Nuovo appello",
            "Dettagli", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt.AddHours(2));

        Assert.False(second.Created);
        Assert.False(second.ContentChanged);
        Assert.False(second.Item.IsNew);
        Assert.Equal(SyncedAt.AddHours(2), second.Item.UpdatedAt);
        Assert.Single(announcements);
    }

    [Fact]
    public void ChangedDiscussionContentReopensTheExistingItemAsUnread()
    {
        var announcements = new ObservableCollection<Announcement>();
        var first = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Nuovo appello",
            "Prima versione", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt);
        first.Item.IsNew = false;

        var updated = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Nuovo appello",
            "Seconda versione", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt.AddDays(1));

        Assert.False(updated.Created);
        Assert.True(updated.ContentChanged);
        Assert.True(updated.Item.IsNew);
        Assert.Single(announcements);
    }

    [Fact]
    public void ChangedDiscussionReappearsInInboxAfterItWasMarkedRead()
    {
        var data = new StudyData();
        AnnouncementSyncService.Upsert(
            data.Announcements, "moodle-discussion:42", "Analisi", "Discussione",
            "Prima versione", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt);
        Assert.Equal(1, AnnouncementSyncService.MarkAllRead(data));
        var center = new NotificationCenterService();
        var now = new DateTimeOffset(SyncedAt);

        Assert.Empty(center.GetUpcoming(data, now));

        AnnouncementSyncService.Upsert(
            data.Announcements, "moodle-discussion:42", "Analisi", "Discussione",
            "Versione aggiornata", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt.AddDays(1));

        var notification = Assert.Single(center.GetUpcoming(data, now));
        Assert.Equal("announcement:" + data.Announcements.Single().Id, notification.Id);
        Assert.Equal("Discussione", notification.Title);
    }

    [Fact]
    public void ChangedDestinationAlsoReopensTheExistingItem()
    {
        var announcements = new ObservableCollection<Announcement>();
        var first = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Discussione",
            "Dettagli", "https://moodle.example/mod/forum/view.php?id=7", SyncedAt);
        first.Item.IsNew = false;

        var updated = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Discussione",
            "Dettagli", "https://moodle.example/mod/forum/view.php?id=8", SyncedAt.AddDays(1));

        Assert.True(updated.ContentChanged);
        Assert.True(updated.Item.IsNew);
    }

    [Fact]
    public void InboxReadActionsDoNotChangeCourseActivityCompletionState()
    {
        var data = new StudyData();
        data.Announcements.Add(new Announcement
        {
            Id = "discussion",
            SourceKey = "moodle-discussion:42",
            IsNew = true
        });
        data.Announcements.Add(new Announcement
        {
            Id = "activity",
            SourceKey = "moodle-activity:17:9",
            IsNew = true
        });
        data.Announcements.Add(new Announcement
        {
            Id = "api-detail",
            SourceKey = "moodle-activity-api:17:assign",
            IsNew = true
        });

        Assert.True(AnnouncementSyncService.MarkRead(data, "discussion"));
        Assert.False(AnnouncementSyncService.MarkRead(data, "discussion"));
        Assert.Equal(1, AnnouncementSyncService.MarkAllRead(data));
        Assert.False(data.Announcements.Single(x => x.Id == "discussion").IsNew);
        Assert.True(data.Announcements.Single(x => x.Id == "activity").IsNew);
        Assert.True(data.Announcements.Single(x => x.Id == "api-detail").IsNew);
    }

    [Fact]
    public void NotificationCenterIncludesOnlyUnreadMoodleDiscussions()
    {
        var now = new DateTimeOffset(SyncedAt);
        var data = new StudyData();
        data.Announcements.Add(new Announcement
        {
            Id = "unread",
            SourceKey = "moodle-discussion:42",
            CourseName = "Analisi",
            Title = "Nuova discussione",
            Summary = "Nuovi esercizi",
            UpdatedAt = SyncedAt,
            IsNew = true
        });
        data.Announcements.Add(new Announcement
        {
            Id = "read",
            SourceKey = "moodle-discussion:43",
            Title = "Già letta",
            IsNew = false
        });
        data.Announcements.Add(new Announcement
        {
            Id = "activity",
            SourceKey = "moodle-activity:17:9",
            Title = "Attività da completare",
            IsNew = true
        });

        var result = new NotificationCenterService().GetUpcoming(data, now, TimeSpan.Zero);

        var notification = Assert.Single(result);
        Assert.Equal("announcement:unread", notification.Id);
        Assert.Equal("Nuova discussione", notification.Title);
        Assert.Contains("Analisi", notification.Detail, StringComparison.Ordinal);
        Assert.Equal("Moodle", notification.Kind);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:password@moodle.example/mod/forum/view.php?id=7")]
    [InlineData("https://moodle.example/mod/forum/view.php?wstoken=secret")]
    [InlineData("https://moodle.example/mod/forum/view.php#token=secret")]
    public void UnsafeAnnouncementUrlsAreDiscarded(string url)
    {
        var announcements = new ObservableCollection<Announcement>();

        var result = AnnouncementSyncService.Upsert(
            announcements, "moodle-discussion:42", "Analisi", "Discussione",
            "Dettagli", url, SyncedAt);

        Assert.Equal(string.Empty, result.Item.Url);
    }

    [Fact]
    public void AnnouncementTextIsBoundedAndControlCharactersAreRemoved()
    {
        var announcements = new ObservableCollection<Announcement>();
        var result = AnnouncementSyncService.Upsert(
            announcements,
            "moodle-discussion:42",
            "Analisi\0",
            new string('T', AnnouncementSyncService.MaximumTitleLength + 20),
            "Prima riga\0\nSeconda riga",
            "https://moodle.example/mod/forum/view.php?id=7",
            SyncedAt);

        Assert.Equal(AnnouncementSyncService.MaximumTitleLength, result.Item.Title.Length);
        Assert.Equal("Analisi", result.Item.CourseName);
        Assert.Equal("Prima riga\nSeconda riga", result.Item.Summary);
        Assert.True(result.Item.Url.Length <= AnnouncementSyncService.MaximumUrlLength);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("moodle-discussion:\n42")]
    public void InvalidSourceKeysAreRejected(string sourceKey)
    {
        var announcements = new ObservableCollection<Announcement>();

        Assert.Throws<ArgumentException>(() => AnnouncementSyncService.Upsert(
            announcements, sourceKey, "Analisi", "Discussione", "Dettagli",
            "https://moodle.example/mod/forum/view.php?id=7", SyncedAt));
        Assert.Empty(announcements);
    }
}
