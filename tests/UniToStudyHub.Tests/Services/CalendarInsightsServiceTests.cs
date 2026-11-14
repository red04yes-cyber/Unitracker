using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class CalendarInsightsServiceTests
{
    private readonly CalendarInsightsService _service = new();

    private static CalendarEvent Event(
        string id,
        DateTimeOffset start,
        DateTimeOffset end,
        string category = CalendarEventCategory.Study,
        bool allDay = false,
        bool cancelled = false) => new()
    {
        Id = id,
        Title = id,
        Category = category,
        StartsAt = start,
        EndsAt = end,
        IsAllDay = allDay,
        IsCancelled = cancelled,
        TimeZoneId = "UTC"
    };

    [Fact]
    public void FindConflictsReturnsOverlappingPairsButNotTouchingEvents()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("first", start.AddHours(1), start.AddHours(3)),
            Event("second", start.AddHours(2), start.AddHours(4)),
            Event("touching", start.AddHours(4), start.AddHours(5))
        };

        var conflicts = _service.FindConflicts(events, start, start.AddHours(8));

        var conflict = Assert.Single(conflicts);
        Assert.Equal("first", conflict.FirstEvent.Id);
        Assert.Equal("second", conflict.SecondEvent.Id);
        Assert.Equal(start.AddHours(2), conflict.StartsAt);
        Assert.Equal(start.AddHours(3), conflict.EndsAt);
        Assert.Equal(TimeSpan.FromHours(1), conflict.OverlapDuration);
    }

    [Fact]
    public void FindConflictsIgnoresCancelledEventsAndTreatsAllDayAsNonBlockingByDefault()
    {
        var start = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("all-day", start, start.AddDays(1), allDay: true),
            Event("cancelled", start.AddHours(9), start.AddHours(11), cancelled: true),
            Event("timed", start.AddHours(10), start.AddHours(12))
        };

        Assert.Empty(_service.FindConflicts(events, start, start.AddDays(1)));
        var included = Assert.Single(_service.FindConflicts(events, start, start.AddDays(1),
            includeAllDayEvents: true));
        Assert.Equal("all-day", included.FirstEvent.Id);
        Assert.Equal("timed", included.SecondEvent.Id);
    }

    [Fact]
    public void FindConflictsUsesAbsoluteInstantsWhenOffsetsDiffer()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var first = Event("rome", new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(2)),
            new DateTimeOffset(2026, 11, 14, 11, 0, 0, TimeSpan.FromHours(2)));
        var second = Event("utc", start.AddMinutes(30), start.AddMinutes(90));

        var conflict = Assert.Single(_service.FindConflicts(new[] { first, second }, start, start.AddDays(1)));
        Assert.Equal(TimeSpan.FromMinutes(30), conflict.OverlapDuration);
    }

    [Fact]
    public void GetFreeWindowsMergesOverlappingAndAdjacentCommitments()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("one", start.AddHours(1), start.AddHours(2)),
            Event("two", start.AddHours(1.5), start.AddHours(3)),
            Event("three", start.AddHours(3), start.AddHours(4))
        };

        var free = _service.GetFreeWindows(events, start, start.AddHours(9), TimeSpan.FromMinutes(30),
            TimeZoneInfo.Utc);

        Assert.Equal(2, free.Count);
        Assert.Equal(start, free[0].StartsAt);
        Assert.Equal(start.AddHours(1), free[0].EndsAt);
        Assert.Equal(start.AddHours(4), free[1].StartsAt);
        Assert.Equal(start.AddHours(9), free[1].EndsAt);
    }

    [Fact]
    public void GetFreeWindowsClipsToRangeAndAppliesMinimumDuration()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("before", start.AddHours(-1), start.AddHours(1)),
            Event("middle", start.AddHours(2), start.AddHours(3.5))
        };

        var filtered = _service.GetFreeWindows(events, start, start.AddHours(4),
            TimeSpan.FromMinutes(61), TimeZoneInfo.Utc);
        Assert.Empty(filtered);

        var free = _service.GetFreeWindows(events, start, start.AddHours(4),
            TimeSpan.FromMinutes(30), TimeZoneInfo.Utc);
        Assert.Equal(2, free.Count);
        Assert.Equal(start.AddHours(1), free[0].StartsAt);
        Assert.Equal(start.AddHours(2), free[0].EndsAt);
        Assert.Equal(start.AddHours(3.5), free[1].StartsAt);
        Assert.Equal(start.AddHours(4), free[1].EndsAt);
    }

    [Fact]
    public void GetDailySummariesCountsUnionBusyTimeOnlyOnce()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("first", start.AddHours(1), start.AddHours(2)),
            Event("overlap", start.AddHours(1.5), start.AddHours(3)),
            Event("later", start.AddHours(5), start.AddHours(6))
        };

        var summary = Assert.Single(_service.GetDailySummaries(events, start, start.AddHours(10),
            TimeZoneInfo.Utc));

        Assert.Equal(new DateOnly(2026, 11, 14), summary.Date);
        Assert.Equal(3, summary.EventCount);
        Assert.Equal(3, summary.BlockingEventCount);
        Assert.Equal(1, summary.ConflictCount);
        Assert.Equal(TimeSpan.FromHours(3), summary.BusyDuration);
        Assert.Equal(TimeSpan.FromHours(7), summary.AvailableDuration);
    }

    [Fact]
    public void GetDailySummariesUsesTwentyThreeHourDayAcrossRomeSpringDst()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var start = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.FromHours(1));
        var end = new DateTimeOffset(2026, 3, 30, 0, 0, 0, TimeSpan.FromHours(2));

        var summary = Assert.Single(_service.GetDailySummaries(
            Array.Empty<CalendarEvent>(), start, end, zone));

        Assert.Equal(new DateOnly(2026, 3, 29), summary.Date);
        Assert.Equal(TimeSpan.FromHours(23), summary.AvailableDuration);
        Assert.Equal(TimeSpan.Zero, summary.BusyDuration);
    }

    [Fact]
    public void GetUpcomingDeadlinesReturnsChronologicalNonCancelledItemsAndHonorsLimit()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("late", start.AddHours(6), start.AddHours(7), CalendarEventCategory.Deadline),
            Event("early", start.AddHours(2), start.AddHours(3), CalendarEventCategory.Deadline),
            Event("cancelled", start.AddHours(1), start.AddHours(2), CalendarEventCategory.Deadline,
                cancelled: true),
            Event("middle", start.AddHours(4), start.AddHours(5), CalendarEventCategory.Deadline),
            Event("study", start.AddHours(1), start.AddHours(2))
        };

        var deadlines = _service.GetUpcomingDeadlines(events, start, start.AddDays(1), limit: 2);

        Assert.Equal(new[] { "early", "middle" }, deadlines.Select(item => item.Id));
    }

    [Fact]
    public void GetNextEventReturnsOngoingEventBeforeFutureAndIgnoresEndedOrCancelled()
    {
        var now = new DateTimeOffset(2026, 11, 14, 9, 30, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("ended", now.AddHours(-2), now.AddMinutes(-1)),
            Event("future", now.AddHours(1), now.AddHours(2)),
            Event("ongoing", now.AddMinutes(-30), now.AddMinutes(30)),
            Event("cancelled", now.AddMinutes(-10), now.AddHours(1), cancelled: true)
        };

        Assert.Equal("ongoing", _service.GetNextEvent(events, now)?.Id);
    }

    [Fact]
    public void AnalyzeRangeComposesInsightsWithoutMutatingTheCalendar()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = new[]
        {
            Event("study-a", start.AddHours(1), start.AddHours(3)),
            Event("study-b", start.AddHours(2), start.AddHours(4)),
            Event("deadline", start.AddHours(5), start.AddHours(6), CalendarEventCategory.Deadline)
        };
        var original = events.Select(item => (item.Id, item.StartsAt, item.EndsAt)).ToArray();

        var result = _service.AnalyzeRange(events, start, start.AddHours(8), start.AddHours(2.5),
            TimeSpan.FromHours(1), TimeZoneInfo.Utc);

        Assert.Equal(3, result.Events.Count);
        Assert.Single(result.Conflicts);
        Assert.Single(result.UpcomingDeadlines);
        Assert.Equal("study-a", result.NextEvent?.Id);
        Assert.Single(result.Days);
        Assert.Equal(original, events.Select(item => (item.Id, item.StartsAt, item.EndsAt)).ToArray());
    }

    [Fact]
    public void AnalyzeRangeRejectsInvalidRangesAndUnboundedDailyWindows()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.FindConflicts(Array.Empty<CalendarEvent>(), start, start));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.GetFreeWindows(Array.Empty<CalendarEvent>(), start, start.AddHours(1),
                TimeSpan.Zero, TimeZoneInfo.Utc));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.GetDailySummaries(Array.Empty<CalendarEvent>(), start, start.AddDays(367),
                TimeZoneInfo.Utc));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _service.GetUpcomingDeadlines(Array.Empty<CalendarEvent>(), start, start.AddDays(1), 101));
    }

    [Fact]
    public void FindConflictsRejectsExcessivePairCountsInsteadOfGrowingWithoutBound()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, 226)
            .Select(index => Event($"event-{index:D3}", start, start.AddHours(1)))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            _service.FindConflicts(events, start, start.AddHours(2)));
    }


    [Fact]
    public void GetNextEventRejectsExcessiveCandidateLists()
    {
        var now = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, CalendarInsightsService.MaximumEventsPerRange + 1)
            .Select(index => Event($"event-{index:D5}", now.AddMinutes(index), now.AddMinutes(index + 1)))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() => _service.GetNextEvent(events, now));
    }

    [Fact]
    public void GetRangeAnalysisRejectsExcessiveEventCounts()
    {
        var start = new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);
        var events = Enumerable.Range(0, CalendarInsightsService.MaximumEventsPerRange + 1)
            .Select(index => Event($"event-{index:D5}", start.AddMinutes(index), start.AddMinutes(index + 1)))
            .ToArray();

        Assert.Throws<InvalidOperationException>(() =>
            _service.FindConflicts(events, start, start.AddDays(1)));
    }
}
