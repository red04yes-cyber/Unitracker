using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using UniToStudyHub.Presentation.Navigation;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub.Presentation.Views;

/// <summary>
/// Compatibility event host for dialogs and confirmation prompts while the
/// page commands are being moved into their dedicated view models.
/// It never throws while WPF is still establishing the inherited DataContext.
/// </summary>
public class LegacyPageControl : UserControl
{
    private bool TryGetState(out MainViewModel? state)
    {
        state = (DataContext as PageViewModelBase)?.State;
        return state is not null;
    }

    protected void NavigateToSubjects_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(StudyRoute.Courses);

    protected void NavigateToStudyLab_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(StudyRoute.StudyLab);

    protected void NavigateToCalendar_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(StudyRoute.Calendar);

    protected void NavigateToSettings_Click(object sender, RoutedEventArgs e) =>
        NavigateTo(StudyRoute.Settings);

    private void NavigateTo(StudyRoute route)
    {
        if (DataContext is PageViewModelBase page)
            page.NavigateTo(route);
    }

    protected async void ConnectMoodle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state)) return;
        await state!.ConnectMoodleAsync();
        if (state.IsMoodleConnected && FindName("MoodleTokenBox") is PasswordBox tokenBox)
            tokenBox.Clear();
    }

    protected void MoodleTokenBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (TryGetState(out var state) && sender is PasswordBox passwordBox)
            state!.MoodleToken = passwordBox.Password;
    }

    protected void CourseFilter_Checked(object sender, RoutedEventArgs e)
    {
        if (TryGetState(out var state) &&
            sender is RadioButton radioButton &&
            radioButton.Tag is string filter)
            state!.CourseFilter = filter;
    }

    protected async void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state)) return;

        var dialog = new OpenFileDialog
        {
            Title = "Importa materiale didattico",
            Filter = "Documenti PDF (*.pdf)|*.pdf",
            CheckFileExists = true,
            Multiselect = false
        };

        var owner = Window.GetWindow(this);
        var selected = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (selected == true)
            await state!.Workspace.ImportPdfAsync(dialog.FileName);
    }

    protected async void ExportProgress_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state)) return;

        var dialog = new SaveFileDialog
        {
            Title = "Esporta report di studio",
            Filter = "Cartella di lavoro Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"UniToStudyHub-progressi-{DateTime.Today:yyyyMMdd}.xlsx",
            OverwritePrompt = true
        };
        var owner = Window.GetWindow(this);
        var selected = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (selected != true) return;

        try
        {
            await state!.Workspace.ExportProgressAsync(dialog.FileName);
            MessageBox.Show("Report esportato correttamente.", "Esportazione completata",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Esportazione non riuscita: {ex.Message}", "Errore esportazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected void DisconnectMoodle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state)) return;

        var result = MessageBox.Show(
            "Disconnettere Moodle? Il token salvato verrà eliminato e i riferimenti di collegamento rimossi. Corsi, voti, eventi importati e storico locale saranno conservati.",
            "Conferma disconnessione Moodle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        state!.DisconnectMoodleCommand.Execute(null);
        if (FindName("MoodleTokenBox") is PasswordBox tokenBox)
            tokenBox.Clear();
    }

    protected void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state) || state!.Workspace.SelectedBackup is null)
            return;

        var backupName = System.IO.Path.GetFileName(state.Workspace.SelectedBackup.FilePath);
        var result = MessageBox.Show(
            $"Ripristinare il backup \"{backupName}\"? I dati correnti verranno sostituiti. Prima sarà creata una copia di sicurezza automatica.",
            "Conferma ripristino backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            state.Workspace.RestoreBackupCommand.Execute(null);
    }

    protected void DeleteCalendarEvent_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state) || state!.SelectedCalendarEvent is null)
            return;

        var title = state.SelectedCalendarEvent.Title;
        var result = MessageBox.Show(
            $"Vuoi eliminare l'evento \"{title}\"?",
            "Conferma eliminazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            state.DeleteCalendarEventCommand.Execute(null);
    }

    protected void RemoveStudyTask_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetState(out var state) || state!.Workspace.SelectedTask is null)
            return;

        var title = state.Workspace.SelectedTask.Title;
        var result = MessageBox.Show(
            $"Vuoi rimuovere l'attività \"{title}\"?",
            "Conferma rimozione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            state.Workspace.RemoveTaskCommand.Execute(null);
    }
}
