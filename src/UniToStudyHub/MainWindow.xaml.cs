using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _examModeTimer;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _examModeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _examModeTimer.Tick += (_, _) => viewModel.RefreshExamModeDisplay();
        _examModeTimer.Start();
        Closed += (_, _) => _examModeTimer.Stop();
    }

    private void MoodleTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
            viewModel.MoodleToken = passwordBox.Password;
    }

    private void CourseFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton radioButton &&
            DataContext is MainViewModel viewModel &&
            radioButton.Tag is string filter)
        {
            viewModel.CourseFilter = filter;
        }
    }

    private void NavigateToSubjects_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 1;
    }

    private void NavigateToSettings_Click(object sender, RoutedEventArgs e)
    {
        MainTabs.SelectedIndex = 6;
    }
}
