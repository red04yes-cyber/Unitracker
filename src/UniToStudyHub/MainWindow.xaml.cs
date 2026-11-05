using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _focusRefreshTimer;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _focusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusRefreshTimer.Tick += (_, _) => viewModel.Workspace.RefreshFocusSnapshot();
        _focusRefreshTimer.Start();
        Closed += (_, _) => _focusRefreshTimer.Stop();
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

    private async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importa materiale didattico",
            Filter = "Documenti PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true && DataContext is MainViewModel viewModel)
            await viewModel.Workspace.ImportPdfAsync(dialog.FileName);
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
