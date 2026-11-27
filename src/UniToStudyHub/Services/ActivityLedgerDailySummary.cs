using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>Unique recorded seconds and overlap seconds for one local calendar day.</summary>
public readonly record struct ActivityDailySummary(int RecordedSeconds, int OverlapSeconds);

public sealed partial class ActivityLedgerService
{
    /// <summary>
    /// Splits recorded intervals into local calendar days after a single ledger scan.
    /// Each interval is clipped to the requested range and split at the selected
    /// time-zone's day boundaries, preserving overlap accounting across midnight
    /// and daylight-saving transitions.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, ActivityDailySummary> SummarizeByDay(
        StudyData data,
        DateOnly from,
        DateOnly to,
        DateTimeOffset? now = null,
        string? timeZoneId = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (to < from)
            throw new ArgumentOutOfRangeException(nameof(to), "The end date must not precede the start date.");

        var zone = ResolveTimeZone(timeZoneId);
        var rangeStart = StartOfDay(from, zone);
        var rangeEnd = StartOfDay(to.AddDays(1), zone);
        var intervalsByDay = new Dictionary<DateOnly, List<(DateTimeOffset Start, DateTimeOffset End)>>();

        foreach (var interval in GetRecordedIntervals(data, now ?? DateTimeOffset.Now))
        {
            if (interval.End <= rangeStart || interval.Start >= rangeEnd) continue;

            var cursor = interval.Start < rangeStart ? rangeStart : interval.Start;
            var intervalEnd = interval.End > rangeEnd ? rangeEnd : interval.End;
            while (cursor < intervalEnd)
            {
                var day = LocalDate(cursor, zone);
                if (day < from) day = from;

                var dayEnd = day <= to ? StartOfDay(day.AddDays(1), zone) : rangeEnd;
                while (day <= to && dayEnd <= cursor)
                {
                    // Some time-zone databases contain skipped civil dates.
                    // Advance to the next real boundary rather than looping.
                    day = day.AddDays(1);
                    if (day <= to) dayEnd = StartOfDay(day.AddDays(1), zone);
                }

                if (day > to) break;
                if (dayEnd <= cursor)
                    throw new InvalidOperationException("The time-zone day boundary did not advance.");

                var segmentEnd = dayEnd < intervalEnd ? dayEnd : intervalEnd;
                if (!intervalsByDay.TryGetValue(day, out var dailyIntervals))
                {
                    dailyIntervals = new List<(DateTimeOffset Start, DateTimeOffset End)>();
                    intervalsByDay.Add(day, dailyIntervals);
                }

                dailyIntervals.Add((cursor, segmentEnd));
                cursor = segmentEnd;
            }
        }

        return intervalsByDay.ToDictionary(
            item => item.Key,
            item => new ActivityDailySummary(
                UnionSeconds(item.Value),
                OverlapSeconds(item.Value)));
    }
}
