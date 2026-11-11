using System.Windows;
using System.Windows.Controls;

namespace UniToStudyHub.Views;

public partial class ProgressView : UserControl
{
    public event RoutedEventHandler? ExportProgress_ClickRequested;
    private void ExportProgress_Click(object sender, RoutedEventArgs e) => ExportProgress_ClickRequested?.Invoke(sender, e);

    public ProgressView()
    {
        InitializeComponent();
    }
}
