using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed record CalendarTimelineItem(
    CalendarEvent Event,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool IsClippedAtStart,
    bool IsClippedAtEnd,
    bool IsCurrent,
    int DurationMinutes);

public sealed record CalendarTimelineSlot(
    int Index,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    IReadOnlyList<string> EventIds,
    bool HasEvents,
    bool HasOverlap,
    bool IsCurrent);

public sealed record CalendarFreeBlock(
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int Minutes);

public sealed record CalendarTimelineDay(
    DateOnly Day,
    string TimeZoneId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    int SlotMinutes,
    DateTimeOffset Now,
    int BusyMinutes,
    int FreeMinutes,
    IReadOnlyList<CalendarTimelineItem> Events,
    IReadOnlyList<CalendarTimelineSlot> Slots,
    IReadOnlyList<CalendarFreeBlock> FreeBlocks);

/// <summary>
/// Builds a day timeline from persisted calendar events. It clips events that
/// cross midnight, keeps the real local day length (including DST) and never
/// invents tasks or study blocks to fill empty slots.
/// </summary>
public sealed class CalendarTimelineService
{
    public CalendarTimelineDay BuildDay(
        StudyData data,
        DateOnly day,
        int slotMinutes = 30,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (slotMinutes is not (15 or 30 or 60))
            throw new ArgumentOutOfRangeException(nameof(slotMinutes), "The timeline supports 15, 30 or 60 minute slots.");
        var zone = ResolveTimeZone(timeZoneId);
        var at = now ?? DateTimeOffset.Now;
        var start = StartOfDay(day, zone);
        var end = StartOfDay(day.AddDays(1), zone);
        if (end <= start) throw new InvalidOperationException("The selected day has no valid local interval.");

        var items = data.CalendarEvents
            .Where(x => !x.IsCancelled && x.StartsAt < end && x.EndsAt > start)
            .OrderBy(x => x.StartsAt)
            .ThenBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(x =>
            {
                var clippedStart = x.StartsAt < start ? start : x.StartsAt;
                var clippedEnd = x.EndsAt > end ? end : x.EndsAt;
                var duration = Math.Max(0, (int)Math.Floor((clippedEnd - clippedStart).TotalMinutes));
                return new CalendarTimelineItem(
                    x,
                    clippedStart,
                    clippedEnd,
                    x.StartsAt < start,
                    x.EndsAt > end,
                    at >= clippedStart && at < clippedEnd,
                    duration);
            })
            .Where(x => x.EndsAt > x.StartsAt)
            .ToList();

        var slots = new List<CalendarTimelineSlot>();
        var index = 0;
        for (var cursor = start; cursor < end; cursor = cursor.AddMinutes(slotMinutes))
        {
            var slotEnd = cursor.AddMinutes(slotMinutes);
            if (slotEnd > end) slotEnd = end;
            var ids = items
                .Where(x => x.StartsAt < slotEnd && x.EndsAt > cursor)
                .Select(x => x.Event.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray();
            slots.Add(new CalendarTimelineSlot(
                index++,
                cursor,
                slotEnd,
                ids,
                ids.Length > 0,
                ids.Length > 1,
                at >= cursor && at < slotEnd));
        }

        var busySeconds = UnionSeconds(items.Select(x => (x.StartsAt, x.EndsAt)));
        var totalSeconds = Math.Max(0, (int)Math.Floor((end - start).TotalSeconds));
        var freeBlocks = BuildFreeBlocks(items, start, end);
        return new CalendarTimelineDay(
            day,
            zone.Id,
            start,
            end,
            slotMinutes,
            at,
            busySeconds / 60,
            Math.Max(0, totalSeconds - busySeconds) / 60,
            items,
            slots,
            freeBlocks);
    }

    private static IReadOnlyList<CalendarFreeBlock> BuildFreeBlocks(
        IReadOnlyList<CalendarTimelineItem> items,
        DateTimeOffset start,
        DateTimeOffset end)
    {
        var blocks = new List<CalendarFreeBlock>();
        var cursor = start;
        foreach (var item in items.OrderBy(x => x.StartsAt))
        {
            if (item.StartsAt > cursor)
                blocks.Add(new CalendarFreeBlock(cursor, item.StartsAt,
                    Math.Max(0, (int)Math.Floor((item.StartsAt - cursor).TotalMinutes))));
            if (item.EndsAt > cursor) cursor = item.EndsAt;
            if (cursor >= end) break;
        }
        if (cursor < end)
            blocks.Add(new CalendarFreeBlock(cursor, end, Math.Max(0, (int)Math.Floor((end - cursor).TotalMinutes))));
        return blocks.Where(x => x.Minutes > 0).ToList();
    }

    private static int UnionSeconds(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> source)
    {
        var intervals = source.OrderBy(x => x.Start).ToList();
        var total = 0;
        DateTimeOffset? start = null;
        DateTimeOffset? end = null;
        foreach (var interval in intervals)
        {
            if (start is null)
            {
                start = interval.Start;
                end = interval.End;
                continue;
            }
            if (interval.Start <= end)
            {
                if (interval.End > end) end = interval.End;
                continue;
            }
            total += Math.Max(0, (int)Math.Floor((end!.Value - start.Value).TotalSeconds));
            start = interval.Start;
            end = interval.End;
        }
        if (start is not null)
            total += Math.Max(0, (int)Math.Floor((end!.Value - start.Value).TotalSeconds));
        return total;
    }

    private static DateTimeOffset StartOfDay(DateOnly day, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(day.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new ArgumentException($"Unknown time zone '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
        catch (InvalidTimeZoneException ex)
        {
            throw new ArgumentException($"Invalid time zone '{timeZoneId}'.", nameof(timeZoneId), ex);
        }
    }
}
