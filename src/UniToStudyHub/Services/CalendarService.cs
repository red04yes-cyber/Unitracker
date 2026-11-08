using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

/// <summary>Calendar date-range and event validation rules.</summary>
public sealed class CalendarService
{
    public static DateTime StartOfWeek(DateTime date)
    {
        var localDate = date.Date;
        var daysSinceMonday = ((int)localDate.DayOfWeek + 6) % 7;
        return localDate.AddDays(-daysSinceMonday);
    }

    public static (DateTime Start, DateTime EndExclusive) GetWeekRange(DateTime date)
    {
        var start = StartOfWeek(date);
        return (start, start.AddDays(7));
    }

    public static (DateTime Start, DateTime EndExclusive) GetMonthRange(DateTime date)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        var start = StartOfWeek(first);
        var end = StartOfWeek(first.AddMonths(1));
        if (end == start)
            end = end.AddDays(7);
        return (start, end);
    }

    /// <summary>
    /// Returns events that overlap [start, endExclusive), including multi-day
    /// and all-day events. Sorting is stable by start time then title.
    /// </summary>
    public static IReadOnlyList<CalendarEvent> GetEventsInRange(
        IEnumerable<CalendarEvent> events,
        DateTimeOffset start,
        DateTimeOffset endExclusive,
        string? category = null,
        string? source = null)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (endExclusive <= start)
            throw new ArgumentOutOfRangeException(nameof(endExclusive), "The end must be after the start.");

        return events
            .Where(e => e is not null && !e.IsCancelled && e.StartsAt < endExclusive && e.EndsAt > start)
            .Where(e => string.IsNullOrWhiteSpace(category) ||
                        string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
            .Where(e => string.IsNullOrWhiteSpace(source) ||
                        string.Equals(e.Source, source, StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static void Validate(CalendarEvent calendarEvent)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);
        if (string.IsNullOrWhiteSpace(calendarEvent.Title))
            throw new ArgumentException("Il titolo dell'evento è obbligatorio.", nameof(calendarEvent));
        if (calendarEvent.EndsAt <= calendarEvent.StartsAt)
            throw new ArgumentException("La fine deve essere successiva all'inizio.", nameof(calendarEvent));
        if (string.IsNullOrWhiteSpace(calendarEvent.Category))
            throw new ArgumentException("La categoria dell'evento è obbligatoria.", nameof(calendarEvent));
        if (calendarEvent.Title.Length > 400 || (calendarEvent.Location?.Length ?? 0) > 400 ||
            (calendarEvent.Notes?.Length ?? 0) > 12000)
            throw new ArgumentException("L'evento contiene un campo troppo lungo.", nameof(calendarEvent));
        if (!string.IsNullOrWhiteSpace(calendarEvent.CourseId) && calendarEvent.CourseId.Length > 64)
            throw new ArgumentException("Il collegamento alla materia non è valido.", nameof(calendarEvent));
        if (calendarEvent.IsAllDay && calendarEvent.EndsAt.Date <= calendarEvent.StartsAt.Date)
            throw new ArgumentException("Un evento di tutta la giornata deve coprire almeno una giornata.", nameof(calendarEvent));
        if (!string.IsNullOrWhiteSpace(calendarEvent.TimeZoneId))
        {
            try { _ = TimeZoneInfo.FindSystemTimeZoneById(calendarEvent.TimeZoneId); }
            catch (TimeZoneNotFoundException) { throw new ArgumentException("Il fuso orario dell'evento non esiste.", nameof(calendarEvent)); }
            catch (InvalidTimeZoneException) { throw new ArgumentException("Il fuso orario dell'evento non è valido.", nameof(calendarEvent)); }
        }
    }

    public static CalendarEvent CreateManualEvent(
        string title,
        string category,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        bool isAllDay = false,
        string? courseId = null,
        string? courseName = null,
        string? location = null,
        string? notes = null)
    {
        var item = new CalendarEvent
        {
            Title = title.Trim(),
            Category = category,
            StartsAt = startsAt,
            EndsAt = endsAt,
            IsAllDay = isAllDay,
            CourseId = courseId,
            CourseName = courseName ?? string.Empty,
            Location = location ?? string.Empty,
            Notes = notes ?? string.Empty,
            Source = CalendarEventSource.Manual,
            Verification = CalendarEventVerification.Unverified
        };
        Validate(item);
        return item;
    }
}
