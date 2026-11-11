using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class CalendarView : UserControl
{
    public event RoutedEventHandler? DeleteCalendarEvent_ClickRequested;
    private void DeleteCalendarEvent_Click(object sender, RoutedEventArgs e) => DeleteCalendarEvent_ClickRequested?.Invoke(sender, e);

    public CalendarView()
    {
        InitializeComponent();
    }
}
