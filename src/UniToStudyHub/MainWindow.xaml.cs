using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;
using Wpf.Ui.Appearance;

namespace UniToStudyHub;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _focusRefreshTimer;
    private readonly AiCredentialStore _aiCredentials;
    private readonly AiProviderPreferencesStore _aiPreferences;

    public MainWindow(MainViewModel viewModel, AiCredentialStore aiCredentials,
        AiProviderPreferencesStore aiPreferences)
    {
        InitializeComponent();
        _aiCredentials = aiCredentials;
        _aiPreferences = aiPreferences;
        var savedAi = _aiPreferences.Load();
        if (savedAi is not null)
        {
            AiProviderBox.SelectedValue = savedAi.Provider;
            AiModelBox.Text = savedAi.Model;
            AiConsentBox.IsChecked = savedAi.ConsentGranted;
        }
        else
        {
            AiProviderBox.SelectedValue = "openai";
            AiModelBox.Text = "gpt-4.1-mini";
        }
        SystemThemeWatcher.Watch(this, Wpf.Ui.Controls.WindowBackdropType.Mica);
        DataContext = viewModel;
        _focusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusRefreshTimer.Tick += (_, _) => viewModel.Workspace.RefreshFocusSnapshot();
        _focusRefreshTimer.Start();
        Closing += (_, _) =>
        {
            _focusRefreshTimer.Stop();
            SystemThemeWatcher.UnWatch(this);
        };
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderBox.SelectedValue as string;
        var model = AiModelBox.Text.Trim();
        var consent = AiConsentBox.IsChecked == true;
        if (provider is not ("openai" or "ollama"))
        {
            MessageBox.Show(this, "Seleziona un provider valido.", "Configurazione AI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(model) || model.Length > 120)
        {
            MessageBox.Show(this, "Inserisci un nome modello valido (massimo 120 caratteri).", "Configurazione AI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (provider == "openai" && !string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
                _aiCredentials.SaveKey("openai", AiApiKeyBox.Password);
            if (provider == "openai" && consent &&
                string.IsNullOrWhiteSpace(_aiCredentials.LoadKey("openai")) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STUDYHUB_OPENAI_API_KEY")))
            {
                MessageBox.Show(this, "Per usare OpenAI devi inserire una chiave API. La chiave viene salvata in forma protetta da DPAPI.", "Chiave mancante",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _aiPreferences.Save(new AiProviderPreferences(provider, model, consent));
            AiApiKeyBox.Clear();
            MessageBox.Show(this,
                "Configurazione salvata sul dispositivo. Chiudi e riapri UniToStudyHub per applicare la selezione del provider. Nessuna chiave è stata salvata nei file di configurazione.",
                "Configurazione AI salvata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile salvare la configurazione AI: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RevokeAiConsent_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderBox.SelectedValue as string;
        if (provider is not ("openai" or "ollama")) provider = "openai";
        var model = string.IsNullOrWhiteSpace(AiModelBox.Text) ? "gpt-4.1-mini" : AiModelBox.Text.Trim();
        var confirm = MessageBox.Show(this,
            "Il consenso AI verrà revocato e l'app si chiuderà per interrompere immediatamente la sessione del provider. Le chiavi protette resteranno salvate, ma non verranno usate senza un nuovo consenso.",
            "Revoca consenso AI", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;
        try
        {
            _aiPreferences.Save(new AiProviderPreferences(provider, model, false));
            AiConsentBox.IsChecked = false;
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile revocare il consenso: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ConnectMoodle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        await viewModel.ConnectMoodleAsync();
        if (viewModel.IsMoodleConnected)
            MoodleTokenBox.Clear();
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

    private async void ExportProgress_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        var dialog = new SaveFileDialog
        {
            Title = "Esporta report di studio",
            Filter = "Cartella di lavoro Excel (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"UniToStudyHub-progressi-{DateTime.Today:yyyyMMdd}.xlsx",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await viewModel.Workspace.ExportProgressAsync(dialog.FileName);
            MessageBox.Show(this, "Report esportato correttamente.", "Esportazione completata",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Esportazione non riuscita: {ex.Message}", "Errore esportazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisconnectMoodle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var result = MessageBox.Show(
            "Disconnettere Moodle? Il token salvato verrà eliminato e i riferimenti di collegamento rimossi. Corsi, voti, eventi importati e storico locale saranno conservati.",
            "Conferma disconnessione Moodle",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        viewModel.DisconnectMoodleCommand.Execute(null);
        MoodleTokenBox.Clear();
    }

    private void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.Workspace.SelectedBackup is null)
            return;

        var backupName = System.IO.Path.GetFileName(viewModel.Workspace.SelectedBackup.FilePath);
        var result = MessageBox.Show(
            $"Ripristinare il backup \"{backupName}\"? I dati correnti verranno sostituiti. Prima sarà creata una copia di sicurezza automatica.",
            "Conferma ripristino backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            viewModel.Workspace.RestoreBackupCommand.Execute(null);
    }

    private void DeleteCalendarEvent_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedCalendarEvent is null)
            return;

        var title = viewModel.SelectedCalendarEvent.Title;
        var result = MessageBox.Show(
            $"Vuoi eliminare l'evento \"{title}\"?",
            "Conferma eliminazione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            viewModel.DeleteCalendarEventCommand.Execute(null);
    }

    private void RemoveStudyTask_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.Workspace.SelectedTask is null)
            return;

        var title = viewModel.Workspace.SelectedTask.Title;
        var result = MessageBox.Show(
            $"Vuoi rimuovere l'attività \"{title}\"?",
            "Conferma rimozione",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (result == MessageBoxResult.Yes)
            viewModel.Workspace.RemoveTaskCommand.Execute(null);
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
