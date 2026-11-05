using UniToStudyHub.Models;

namespace UniToStudyHub.Presentation.ViewModels;

public sealed class CalendarDayCell
{
    public CalendarDayCell(DateTime date, bool isCurrentPeriod, IReadOnlyList<CalendarEvent> events)
    {
        Date = date.Date;
        IsCurrentPeriod = isCurrentPeriod;
        Events = events ?? Array.Empty<CalendarEvent>();
    }

    public DateTime Date { get; }
    public string DayNumber => Date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DayName => Date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));
    public bool IsCurrentPeriod { get; }
    public bool IsToday => Date == DateTime.Today;
    public IReadOnlyList<CalendarEvent> Events { get; }
    public int EventCount => Events.Count;
    public string EventSummary => EventCount switch
    {
        0 => "—",
        1 => "1 evento",
        _ => $"{EventCount} eventi"
    };
}
