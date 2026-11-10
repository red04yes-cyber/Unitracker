using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
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

    private void ExportAttendanceCsv_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new SaveFileDialog
        {
            Title = "Esporta registro lezioni",
            Filter = "File CSV (*.csv)|*.csv",
            FileName = "presenze-lezioni.csv",
            DefaultExt = ".csv",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, viewModel.ExportAttendanceCsv(), new System.Text.UTF8Encoding(true));
            MessageBox.Show(this, "Registro lezioni esportato correttamente.", "Esportazione completata",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Esportazione non riuscita: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
