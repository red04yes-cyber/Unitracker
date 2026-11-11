using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class CoursesView : UserControl
{
    public event RoutedEventHandler? CourseFilter_CheckedRequested;
    private void CourseFilter_Checked(object sender, RoutedEventArgs e) => CourseFilter_CheckedRequested?.Invoke(sender, e);

    public CoursesView()
    {
        InitializeComponent();
    }
}
