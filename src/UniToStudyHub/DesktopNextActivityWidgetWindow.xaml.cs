using System.Windows;
using System.Windows.Threading;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub;

public partial class DesktopNextActivityWidgetWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _refreshTimer;

    public DesktopNextActivityWidgetWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _refreshTimer.Tick += (_, _) => _viewModel.RefreshNextActivityDisplay();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void CloseWidget_Click(object sender, RoutedEventArgs e) => Close();
}