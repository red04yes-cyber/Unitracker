namespace UniToStudyHub.Presentation.ViewModels;

public sealed class CalendarDayCell
{
    public CalendarDayCell(DateTime date, bool isCurrentPeriod, int eventCount)
    {
        Date = date.Date;
        IsCurrentPeriod = isCurrentPeriod;
        EventCount = eventCount;
    }

    public DateTime Date { get; }
    public string DayNumber => Date.Day.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DayName => Date.ToString("ddd", System.Globalization.CultureInfo.GetCultureInfo("it-IT"));
    public bool IsCurrentPeriod { get; }
    public bool IsToday => Date == DateTime.Today;
    public int EventCount { get; }
    public string EventSummary => EventCount switch
    {
        0 => "—",
        1 => "1 evento",
        _ => $"{EventCount} eventi"
    };
}
