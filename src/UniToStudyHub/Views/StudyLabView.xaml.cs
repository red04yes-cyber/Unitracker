using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class StudyLabView : UserControl
{
    public event RoutedEventHandler? RemoveStudyTask_ClickRequested;
    private void RemoveStudyTask_Click(object sender, RoutedEventArgs e) => RemoveStudyTask_ClickRequested?.Invoke(sender, e);

    public StudyLabView()
    {
        InitializeComponent();
    }
}
