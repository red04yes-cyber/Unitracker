using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>
/// Describes the part of two calendar events that is occupied by both events.
/// The interval is half-open: StartsAt is included and EndsAt is excluded.
/// </summary>
public sealed record CalendarConflict(
    CalendarEvent FirstEvent,
    CalendarEvent SecondEvent,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt)
{
    public TimeSpan OverlapDuration => EndsAt - StartsAt;
}

/// <summary>A free interval expressed in the requested display time zone.</summary>
public sealed record CalendarFreeWindow(DateTimeOffset StartsAt, DateTimeOffset EndsAt)
{
    public TimeSpan Duration => EndsAt - StartsAt;
}

/// <summary>Busy-time totals for one local civil day.</summary>
public sealed record CalendarDaySummary(
    DateOnly Date,
    int EventCount,
    int BlockingEventCount,
    int ConflictCount,
    TimeSpan BusyDuration,
    TimeSpan AvailableDuration);

/// <summary>
/// A read-only calendar analysis. It never mutates events or persists proposed
/// changes; consumers decide how to display or act on the returned insights.
/// </summary>
public sealed record CalendarRangeAnalysis(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEndExclusive,
    string TimeZoneId,
    bool IncludesAllDayEvents,
    IReadOnlyList<CalendarEvent> Events,
    IReadOnlyList<CalendarConflict> Conflicts,
    IReadOnlyList<CalendarFreeWindow> FreeWindows,
    IReadOnlyList<CalendarDaySummary> Days,
    IReadOnlyList<CalendarEvent> UpcomingDeadlines,
    CalendarEvent? NextEvent);

/// <summary>
/// Calendar insights over the existing shared calendar snapshot. All interval
/// comparisons use DateTimeOffset instants and half-open ranges, so events that
/// merely touch at an endpoint are not reported as conflicts.
/// </summary>
public sealed class CalendarInsightsService
{
    public const int MaximumEventsPerRange = 10_000;
    public const int MaximumConflictPairs = 25_000;
    public const int MaximumDailySummaryDays = 366;
    public const int MaximumDeadlineResults = 100;

    public CalendarRangeAnalysis AnalyzeRange(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        DateTimeOffset now,
        TimeSpan minimumFreeWindow,
        TimeZoneInfo? displayTimeZone = null,
        bool includeAllDayEvents = false,
        int upcomingDeadlineLimit = 5)
    {
        ValidateRange(start, endExclusive);
        ValidateMinimumFreeWindow(minimumFreeWindow);
        ValidateDeadlineLimit(upcomingDeadlineLimit);

        var zone = displayTimeZone ?? TimeZoneInfo.Local;
        var rangeEvents = GetRangeEvents(events, start, endExclusive);
        var conflicts = FindConflictsInRange(rangeEvents, start, endExclusive, includeAllDayEvents);
        var freeWindows = BuildFreeWindows(rangeEvents, start, endExclusive, minimumFreeWindow, zone,
            includeAllDayEvents);
        var days = BuildDailySummaries(rangeEvents, conflicts, start, endExclusive, zone, includeAllDayEvents);
        var deadlines = SelectUpcomingDeadlines(rangeEvents, start, endExclusive, upcomingDeadlineLimit);
        var nextEvent = GetNextEvent(rangeEvents, now);

        return new CalendarRangeAnalysis(start, endExclusive, zone.Id, includeAllDayEvents,
            rangeEvents, conflicts, freeWindows, days, deadlines, nextEvent);
    }

    /// <summary>Returns every overlapping pair in the requested period.</summary>
    public IReadOnlyList<CalendarConflict> FindConflicts(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        bool includeAllDayEvents = false)
    {
        ValidateRange(start, endExclusive);
        return FindConflictsInRange(GetRangeEvents(events, start, endExclusive), start, endExclusive,
            includeAllDayEvents);
    }

    /// <summary>
    /// Returns gaps not occupied by active events. Adjacent or overlapping
    /// commitments are merged. All-day events are non-blocking by default
    /// because an all-day deadline does not necessarily reserve study time.
    /// </summary>
    public IReadOnlyList<CalendarFreeWindow> GetFreeWindows(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        TimeSpan minimumDuration,
        TimeZoneInfo? displayTimeZone = null,
        bool includeAllDayEvents = false)
    {
        ValidateRange(start, endExclusive);
        ValidateMinimumFreeWindow(minimumDuration);
        var zone = displayTimeZone ?? TimeZoneInfo.Local;
        var rangeEvents = GetRangeEvents(events, start, endExclusive);
        return BuildFreeWindows(rangeEvents, start, endExclusive, minimumDuration, zone, includeAllDayEvents);
    }

    /// <summary>
    /// Summarizes event counts and union busy-time per local day. Overlapping
    /// events are counted once in BusyDuration; civil days may be 23 or 25 hours
    /// when daylight-saving transitions occur.
    /// </summary>
    public IReadOnlyList<CalendarDaySummary> GetDailySummaries(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        TimeZoneInfo displayTimeZone,
        bool includeAllDayEvents = false)
    {
        ValidateRange(start, endExclusive);
        ArgumentNullException.ThrowIfNull(displayTimeZone);
        var rangeEvents = GetRangeEvents(events, start, endExclusive);
        var conflicts = FindConflictsInRange(rangeEvents, start, endExclusive, includeAllDayEvents);
        return BuildDailySummaries(rangeEvents, conflicts, start, endExclusive, displayTimeZone,
            includeAllDayEvents);
    }

    /// <summary>Returns non-cancelled deadline events ordered by their start time.</summary>
    public IReadOnlyList<CalendarEvent> GetUpcomingDeadlines(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        int limit = 5)
    {
        ValidateRange(start, endExclusive);
        ValidateDeadlineLimit(limit);
        var rangeEvents = GetRangeEvents(events, start, endExclusive);
        return SelectUpcomingDeadlines(rangeEvents, start, endExclusive, limit);
    }

    /// <summary>
    /// Returns the next event that has not ended. An event already in progress
    /// is returned before a future event; cancelled and malformed intervals are
    /// ignored.
    /// </summary>
    public CalendarEvent? GetNextEvent(IEnumerable<CalendarEvent> events, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events
            .Where(item => item is not null && !item.IsCancelled &&
                           item.EndsAt > item.StartsAt && item.EndsAt > now)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.EndsAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static CalendarEvent[] GetRangeEvents(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive)
    {
        ArgumentNullException.ThrowIfNull(events);
        var result = events
            .Where(item => item is not null && !item.IsCancelled &&
                           item.EndsAt > item.StartsAt &&
                           item.StartsAt < endExclusive && item.EndsAt > start)
            .Take(MaximumEventsPerRange + 1)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.EndsAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        if (result.Length > MaximumEventsPerRange)
            throw new InvalidOperationException(
                $"Il periodo contiene più di {MaximumEventsPerRange} eventi: restringi l'intervallo.");

        return result;
    }

    private static IReadOnlyList<CalendarConflict> FindConflictsInRange(
        IReadOnlyList<CalendarEvent> rangeEvents,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        bool includeAllDayEvents)
    {
        var candidates = rangeEvents
            .Where(item => includeAllDayEvents || !item.IsAllDay)
            .Where(item => item.StartsAt < endExclusive && item.EndsAt > start)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.EndsAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .ToArray();

        var active = new List<CalendarEvent>();
        var conflicts = new List<CalendarConflict>();

        foreach (var current in candidates)
        {
            active.RemoveAll(previous => previous.EndsAt <= current.StartsAt);
            foreach (var previous in active)
            {
                if (string.Equals(previous.Id, current.Id, StringComparison.Ordinal))
                    continue;

                var overlapStart = previous.StartsAt > current.StartsAt
                    ? previous.StartsAt
                    : current.StartsAt;
                var overlapEnd = previous.EndsAt < current.EndsAt
                    ? previous.EndsAt
                    : current.EndsAt;
                if (overlapEnd <= overlapStart)
                    continue;

                if (conflicts.Count >= MaximumConflictPairs)
                    throw new InvalidOperationException(
                        $"L'intervallo produce più di {MaximumConflictPairs} conflitti: restringi il periodo.");

                conflicts.Add(new CalendarConflict(previous, current, overlapStart, overlapEnd));
            }

            active.Add(current);
        }

        return conflicts
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.FirstEvent.Id, StringComparer.Ordinal)
            .ThenBy(item => item.SecondEvent.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<CalendarFreeWindow> BuildFreeWindows(
        IReadOnlyList<CalendarEvent> rangeEvents,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        TimeSpan minimumDuration,
        TimeZoneInfo displayTimeZone,
        bool includeAllDayEvents)
    {
        var busy = MergeIntervals(rangeEvents
            .Where(item => includeAllDayEvents || !item.IsAllDay)
            .Select(item => new TimeRange(
                item.StartsAt < start ? start : item.StartsAt,
                item.EndsAt > endExclusive ? endExclusive : item.EndsAt)));

        var free = new List<CalendarFreeWindow>();
        var cursor = start;
        foreach (var interval in busy)
        {
            if (interval.Start > cursor && interval.Start - cursor >= minimumDuration)
                free.Add(new CalendarFreeWindow(
                    TimeZoneInfo.ConvertTime(cursor, displayTimeZone),
                    TimeZoneInfo.ConvertTime(interval.Start, displayTimeZone)));

            if (interval.End > cursor)
                cursor = interval.End;
        }

        if (endExclusive > cursor && endExclusive - cursor >= minimumDuration)
            free.Add(new CalendarFreeWindow(
                TimeZoneInfo.ConvertTime(cursor, displayTimeZone),
                TimeZoneInfo.ConvertTime(endExclusive, displayTimeZone)));

        return free;
    }

    private static IReadOnlyList<CalendarDaySummary> BuildDailySummaries(
        IReadOnlyList<CalendarEvent> rangeEvents,
        IReadOnlyList<CalendarConflict> conflicts,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        TimeZoneInfo displayTimeZone,
        bool includeAllDayEvents)
    {
        var firstDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, displayTimeZone).DateTime);
        var lastDay = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(endExclusive.AddTicks(-1), displayTimeZone).DateTime);
        if (lastDay == DateOnly.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(endExclusive),
                "The requested period ends beyond the supported local calendar range.");

        var dayCount = lastDay.DayNumber - firstDay.DayNumber + 1;
        if (dayCount > MaximumDailySummaryDays)
            throw new ArgumentOutOfRangeException(nameof(endExclusive),
                $"Il riepilogo giornaliero è limitato a {MaximumDailySummaryDays} giorni.");

        var blockingEvents = rangeEvents
            .Where(item => includeAllDayEvents || !item.IsAllDay)
            .ToArray();
        var summaries = new List<CalendarDaySummary>(dayCount);

        for (var index = 0; index < dayCount; index++)
        {
            var day = firstDay.AddDays(index);
            var dayStart = StartOfLocalDay(day, displayTimeZone);
            var dayEnd = StartOfLocalDay(day.AddDays(1), displayTimeZone);
            if (dayEnd <= dayStart)
                continue; // A civil date skipped by a time-zone change has no elapsed duration.

            var effectiveDayStart = dayStart < start ? start : dayStart;
            var effectiveDayEnd = dayEnd > endExclusive ? endExclusive : dayEnd;
            if (effectiveDayEnd <= effectiveDayStart)
                continue;

            var dayEvents = rangeEvents
                .Where(item => item.StartsAt < effectiveDayEnd && item.EndsAt > effectiveDayStart)
                .ToArray();
            var dayBlocking = blockingEvents
                .Where(item => item.StartsAt < effectiveDayEnd && item.EndsAt > effectiveDayStart)
                .ToArray();

            var busyIntervals = MergeIntervals(dayBlocking.Select(item => new TimeRange(
                item.StartsAt < effectiveDayStart ? effectiveDayStart : item.StartsAt,
                item.EndsAt > effectiveDayEnd ? effectiveDayEnd : item.EndsAt)));
            var busyDuration = busyIntervals.Aggregate(
                TimeSpan.Zero, (total, interval) => total + (interval.End - interval.Start));
            var dayDuration = effectiveDayEnd - effectiveDayStart;
            var conflictCount = conflicts.Count(item =>
                item.StartsAt < effectiveDayEnd && item.EndsAt > effectiveDayStart);

            summaries.Add(new CalendarDaySummary(
                day,
                dayEvents.Length,
                dayBlocking.Length,
                conflictCount,
                busyDuration,
                dayDuration - busyDuration));
        }

        return summaries;
    }

    private static IReadOnlyList<CalendarEvent> SelectUpcomingDeadlines(
        IReadOnlyList<CalendarEvent> rangeEvents,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        int limit) =>
        rangeEvents
            .Where(item => string.Equals(item.Category, CalendarEventCategory.Deadline,
                StringComparison.OrdinalIgnoreCase))
            .Where(item => item.StartsAt < endExclusive && item.EndsAt > start)
            .OrderBy(item => item.StartsAt)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();

    private static List<TimeRange> MergeIntervals(IEnumerable<TimeRange> intervals)
    {
        var ordered = intervals
            .Where(item => item.End > item.Start)
            .OrderBy(item => item.Start)
            .ThenBy(item => item.End)
            .ToArray();
        var merged = new List<TimeRange>();
        foreach (var interval in ordered)
        {
            if (merged.Count == 0 || interval.Start > merged[^1].End)
            {
                merged.Add(interval);
                continue;
            }

            if (interval.End > merged[^1].End)
                merged[^1] = merged[^1] with { End = interval.End };
        }

        return merged;
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        for (var minute = 0; zone.IsInvalidTime(local) && minute < 24 * 60; minute++)
            local = local.AddMinutes(1);

        if (zone.IsInvalidTime(local))
            throw new ArgumentException("Il fuso orario non contiene un istante valido per il giorno richiesto.",
                nameof(zone));

        var offset = zone.IsAmbiguousTime(local)
            ? zone.GetAmbiguousTimeOffsets(local).Max()
            : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static void ValidateRange(DateTimeOffset start, DateTimeOffset endExclusive)
    {
        if (endExclusive <= start)
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "La fine deve essere successiva all'inizio.");
    }

    private static void ValidateMinimumFreeWindow(TimeSpan minimumDuration)
    {
        if (minimumDuration <= TimeSpan.Zero || minimumDuration > TimeSpan.FromDays(7))
            throw new ArgumentOutOfRangeException(nameof(minimumDuration),
                "La durata minima deve essere maggiore di zero e non superiore a sette giorni.");
    }

    private static void ValidateDeadlineLimit(int limit)
    {
        if (limit is < 1 or > MaximumDeadlineResults)
            throw new ArgumentOutOfRangeException(nameof(limit),
                $"Il limite deve essere compreso tra 1 e {MaximumDeadlineResults}.");
    }

    private sealed record TimeRange(DateTimeOffset Start, DateTimeOffset End);
}
