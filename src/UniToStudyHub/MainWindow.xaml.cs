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
    private readonly DispatcherTimer _reminderTimer;
    private readonly System.Windows.Forms.NotifyIcon _reminderTrayIcon;
    private DesktopNextActivityWidgetWindow? _desktopWidgetWindow;
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _examModeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _examModeTimer.Tick += (_, _) => viewModel.RefreshExamModeDisplay();
        _examModeTimer.Start();
        _reminderTrayIcon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "UniTo Study Hub",
            Visible = true
        };
        _reminderTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _reminderTimer.Tick += async (_, _) => await CheckDueRemindersAsync(viewModel);
        Loaded += async (_, _) => await CheckDueRemindersAsync(viewModel);
        _reminderTimer.Start();
        Closed += (_, _) =>
        {
            _examModeTimer.Stop();
            _reminderTimer.Stop();
            _reminderTrayIcon.Visible = false;
            _reminderTrayIcon.Dispose();
        };
    }



    private void OpenDesktopWidget_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        if (_desktopWidgetWindow is { IsVisible: true })
        {
            _desktopWidgetWindow.Activate();
            return;
        }

        _desktopWidgetWindow = new DesktopNextActivityWidgetWindow(viewModel);
        var workArea = SystemParameters.WorkArea;
        _desktopWidgetWindow.Left = workArea.Right - _desktopWidgetWindow.Width - 18;
        _desktopWidgetWindow.Top = workArea.Bottom - _desktopWidgetWindow.Height - 18;
        _desktopWidgetWindow.Closed += (_, _) => _desktopWidgetWindow = null;
        _desktopWidgetWindow.Show();
    }

    private async Task CheckDueRemindersAsync(MainViewModel viewModel)
    {
        try
        {
            var due = await viewModel.CollectDueReminderNotificationsAsync();
            foreach (var reminder in due)
            {
                _reminderTrayIcon.BalloonTipTitle = "Promemoria universitario";
                _reminderTrayIcon.BalloonTipText = $"{reminder.Title} · {reminder.DueAt.ToLocalTime():dd/MM HH:mm}";
                _reminderTrayIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
                _reminderTrayIcon.ShowBalloonTip(8000);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Reminder notification failed: {ex}");
        }
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
