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
    private readonly DispatcherTimer _toastTimer;
    private readonly AiCredentialStore _aiCredentials;
    private readonly AiProviderPreferencesStore _aiPreferences;
    private readonly PrivacyService _privacy;
    private readonly StudyDataImportService _dataImport;

    public MainWindow(MainViewModel viewModel, AiCredentialStore aiCredentials,
        AiProviderPreferencesStore aiPreferences, PrivacyService privacy,
        StudyDataImportService dataImport)
    {
        InitializeComponent();
        _aiCredentials = aiCredentials;
        _aiPreferences = aiPreferences;
        _privacy = privacy;
        _dataImport = dataImport;
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
        Loaded += (_, _) => UpdateSectionHeading();
        _focusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusRefreshTimer.Tick += (_, _) => viewModel.Workspace.RefreshFocusSnapshot();
        _focusRefreshTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _toastTimer.Tick += (_, _) => HideToast();
        Closing += (_, _) =>
        {
            _focusRefreshTimer.Stop();
            _toastTimer.Stop();
            SystemThemeWatcher.UnWatch(this);
        };
    }

    private void ShowToast(string title, string message, bool isError = false)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => ShowToast(title, message, isError));
            return;
        }

        _toastTimer.Stop();
        ToastTitle.Text = title;
        ToastMessage.Text = message;
        ToastIcon.Text = isError ? "!" : "✓";

        ToastIconTile.SetResourceReference(
            Border.BackgroundProperty,
            isError ? "WarningSoftBrush" : "SuccessSoftBrush");
        ToastIcon.SetResourceReference(
            TextBlock.ForegroundProperty,
            isError ? "WarningBrush" : "SuccessBrush");

        ToastHost.Visibility = Visibility.Visible;
        ToastHost.Opacity = 0;
        var offset = new System.Windows.Media.TranslateTransform(0, -8);
        ToastHost.RenderTransform = offset;
        ToastHost.BeginAnimation(
            UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                0, 1, TimeSpan.FromMilliseconds(180)));
        offset.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(
                -8, 0, TimeSpan.FromMilliseconds(190)));

        _toastTimer.Interval = TimeSpan.FromSeconds(message.Length > 140 ? 7 : 5);
        _toastTimer.Start();
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        if (ToastHost.Visibility != Visibility.Visible)
            return;

        var fadeOut = new System.Windows.Media.Animation.DoubleAnimation(
            ToastHost.Opacity, 0, TimeSpan.FromMilliseconds(180));
        fadeOut.Completed += (_, _) => ToastHost.Visibility = Visibility.Collapsed;
        ToastHost.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderBox.SelectedValue as string;
        var model = AiModelBox.Text.Trim();
        var consent = AiConsentBox.IsChecked == true;
        if (provider is not ("openai" or "ollama" or "gemini"))
        {
            UiDialogService.Show(this, "Seleziona un provider valido.", "Configurazione AI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (string.IsNullOrWhiteSpace(model) || model.Length > 120)
        {
            UiDialogService.Show(this, "Inserisci un nome modello valido (massimo 120 caratteri).", "Configurazione AI",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if ((provider is "openai" or "gemini") && !string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
                _aiCredentials.SaveKey(provider, AiApiKeyBox.Password);
            if ((provider is "openai" or "gemini") && consent &&
                string.IsNullOrWhiteSpace(_aiCredentials.LoadKey(provider)) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(provider == "gemini" ? "STUDYHUB_GEMINI_API_KEY" : "STUDYHUB_OPENAI_API_KEY")))
            {
                UiDialogService.Show(this, "Per usare il provider selezionato devi inserire una chiave API. La chiave viene salvata in forma protetta da DPAPI.", "Chiave mancante",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _aiPreferences.Save(new AiProviderPreferences(provider, model, consent));
            AiApiKeyBox.Clear();
            UiDialogService.Show(this,
                "Configurazione salvata sul dispositivo. Chiudi e riapri UniToStudyHub per applicare la selezione del provider. Nessuna chiave è stata salvata nei file di configurazione.",
                "Configurazione AI salvata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Impossibile salvare la configurazione AI: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RevokeAiConsent_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderBox.SelectedValue as string;
        if (provider is not ("openai" or "ollama" or "gemini")) provider = "openai";
        var model = string.IsNullOrWhiteSpace(AiModelBox.Text) ? "gpt-4.1-mini" : AiModelBox.Text.Trim();
        var confirm = UiDialogService.Show(this,
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
            UiDialogService.Show(this, $"Impossibile revocare il consenso: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportAllData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Esporta tutti i dati personali",
            Filter = "Esportazione UniTo StudyHub (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            FileName = $"UniToStudyHub-dati-{DateTime.Today:yyyyMMdd}.json",
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bytes = await _privacy.ExportEverythingAsync();
            await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
            UiDialogService.Show(this,
                "Esportazione completata. Il file contiene dati personali e note private, ma non token Moodle né chiavi AI: conservalo in un luogo sicuro.",
                "Esportazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Esportazione non riuscita: {ex.Message}", "Errore",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ImportAllData_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Importa un'esportazione UniTo StudyHub",
            Filter = "Esportazione UniTo StudyHub (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var bytes = await System.IO.File.ReadAllBytesAsync(dialog.FileName);
            var preview = await _dataImport.PreviewAsync(bytes);
            var counts = string.Join(Environment.NewLine,
                preview.ItemCounts.Where(x => x.Value > 0).Select(x => $"• {x.Key}: {x.Value}"));
            var conflicts = string.Join(Environment.NewLine,
                preview.DuplicateCounts.Where(x => x.Value > 0).Select(x => $"• {x.Key}: {x.Value}"));
            var warnings = string.Join(Environment.NewLine, preview.Warnings);
            var summary = $"Versione: {preview.SchemaVersion}\nData esportazione: {preview.GeneratedAt?.ToLocalTime().ToString("g") ?? "non indicata"}\nRecord: {preview.TotalItems}\n\n{counts}";
            if (!string.IsNullOrWhiteSpace(conflicts))
                summary += $"\n\nConflitti con dati locali (prevarranno i dati locali in Merge):\n{conflicts}";
            if (!string.IsNullOrWhiteSpace(warnings))
                summary += $"\n\nAvvisi:\n{warnings}";

            var choice = UiDialogService.Show(this,
                summary + "\n\nSÌ = Merge (aggiungi solo record nuovi)\nNO = Replace (sostituisci tutti i dati dopo backup cifrato)\nANNULLA = non modificare nulla.",
                "Anteprima importazione · nessuna modifica ancora effettuata",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Cancel);
            if (choice == MessageBoxResult.Cancel) return;

            var strategy = choice == MessageBoxResult.Yes
                ? StudyDataImportStrategy.Merge
                : StudyDataImportStrategy.Replace;
            if (strategy == StudyDataImportStrategy.Replace)
            {
                var confirm = UiDialogService.Show(this,
                    "ATTENZIONE: Replace sostituirà i dati locali con quelli del file. Prima verrà creato un backup cifrato. Vuoi continuare?",
                    "Conferma sostituzione completa", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes) return;
            }

            var result = await _dataImport.ApplyAsync(preview, strategy);
            UiDialogService.Show(this,
                $"Importazione completata. Record aggiunti: {result.AddedRecords}. " +
                (result.SafetyBackupPath is null ? string.Empty : $"Backup di sicurezza: {System.IO.Path.GetFileName(result.SafetyBackupPath)}. ") +
                "L'app verrà riavviata per ricaricare i dati.",
                "Importazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Importazione non riuscita. I dati locali non sono stati sostituiti: {ex.Message}",
                "Errore importazione", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ResetPrivacy_Click(object sender, RoutedEventArgs e)
    {
        var scope = PrivacyResetScopeBox.SelectedIndex switch
        {
            0 => PrivacyResetScope.StudyLab,
            1 => PrivacyResetScope.Calendar,
            2 => PrivacyResetScope.Documents,
            3 => PrivacyResetScope.MoodleConnection,
            _ => throw new InvalidOperationException("Seleziona un ambito di reset valido.")
        };
        var description = scope switch
        {
            PrivacyResetScope.StudyLab => "sessioni, task, Pomodoro, flashcard, diario errori, distrazioni e check-in. Corsi e voti resteranno.",
            PrivacyResetScope.Calendar => "eventi del calendario locale.",
            PrivacyResetScope.Documents => "metadati PDF e file nella cartella Moodle gestita.",
            _ => "token Moodle, URL e riferimenti di collegamento. Corsi e voti resteranno."
        };
        var confirm = UiDialogService.Show(this,
            $"Verranno eliminati {description}\n\nOperazione non annullabile. Continuare?",
            "Conferma reset privacy", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await _privacy.ResetAsync(scope, confirmed: true,
                createSafetyBackup: PrivacySafetyBackupBox.IsChecked == true);
            UiDialogService.Show(this,
                $"Reset completato. Record rimossi: {result.DeletedRecords}. File rimossi: {result.DeletedFiles}. " +
                (result.CredentialsRemoved ? "Credenziali Moodle rimosse. " : string.Empty) +
                "L'app verrà chiusa per ricaricare i dati.",
                "Reset completato", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Reset non riuscito: {ex.Message}", "Errore privacy",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ShowPrivacyInventory_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var inventory = await _privacy.DescribeAsync();
            var message =
                $"Corsi: {inventory.Courses}\nVoti: {inventory.Grades}\nEsami carriera: {inventory.CareerExams}\n" +
                $"Eventi calendario: {inventory.CalendarEvents}\nSessioni studio: {inventory.StudySessions}\n" +
                $"Attività studio: {inventory.StudyActivities}\nNote personali: {inventory.PersonalNotes}\n" +
                $"Documenti PDF: {inventory.PdfDocuments} ({inventory.PdfPages} pagine)\nFile Moodle locali: {inventory.MoodleFiles}\n" +
                $"Flashcard: {inventory.Flashcards}\nDomande: {inventory.Questions}\nErrori registrati: {inventory.ErrorJournalEntries}\n\n" +
                $"Moodle collegato: {(inventory.MoodleLinked ? "sì" : "no")}\nCredenziali AI presenti: {(inventory.AiConfigured ? "sì" : "no")}\n" +
                $"Telemetria: {(inventory.TelemetryEnabled ? "attiva" : "disattivata")}\nProtezione database: {inventory.DatabaseProtection}";
            UiDialogService.Show(this, message, "Inventario dei dati locali",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Impossibile leggere l'inventario privacy: {ex.Message}",
                "Errore privacy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteAllPrivacyData_Click(object sender, RoutedEventArgs e)
    {
        var includeBackups = PrivacyDeleteBackupsBox.IsChecked == true;
        var backupText = includeBackups
            ? "Anche tutti i backup cifrati verranno eliminati."
            : "I backup cifrati esistenti resteranno sul dispositivo.";
        var confirm = UiDialogService.Show(this,
            "ATTENZIONE: verranno eliminati tutti i corsi, voti, eventi, note, materiali e dati di studio. Saranno rimossi token Moodle e chiavi AI e revocato il consenso AI. " +
            backupText + "\n\nQuesta operazione non è annullabile. Hai già esportato i dati che vuoi conservare?",
            "Cancellazione completa dei dati", MessageBoxButton.YesNo,
            MessageBoxImage.Stop, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await _privacy.DeleteAllAsync(includeBackups);
            UiDialogService.Show(this,
                $"Cancellazione completata. Record: {result.DeletedRecords}; backup: {result.DeletedBackups}; " +
                $"file Moodle: {result.DeletedMoodleFiles}; log: {result.DeletedLogFiles}. " +
                "L'app verrà chiusa.",
                "Dati eliminati", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Cancellazione non riuscita: {ex.Message}",
                "Errore privacy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = _privacy.PreviewDiagnosticBundle();
            var consent = UiDialogService.Show(this,
                $"Anteprima pacchetto diagnostico\n\nVersione: {preview.ApplicationVersion}\nSistema: {preview.OperatingSystem}\nRuntime: {preview.Runtime}\nLog disponibili: {preview.AvailableLogFiles}\nDimensione log: {preview.AvailableLogBytes:N0} byte\n\nNon saranno inclusi database personali, PDF, token o chiavi API. Nessun dato verrà inviato automaticamente. Vuoi creare il pacchetto?",
                "Consenso diagnostica", MessageBoxButton.YesNo,
                MessageBoxImage.Information, MessageBoxResult.No);
            if (consent != MessageBoxResult.Yes) return;

            var includeLogs = UiDialogService.Show(this,
                "Vuoi includere anche i log applicativi redatti? Puoi scegliere No per esportare solo i metadati tecnici.",
                "Includere i log?", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
            var dialog = new SaveFileDialog
            {
                Title = "Salva pacchetto diagnostico",
                Filter = "Pacchetto diagnostico ZIP (*.zip)|*.zip",
                DefaultExt = ".zip",
                AddExtension = true,
                FileName = $"UniToStudyHub-diagnostics-{DateTime.Today:yyyyMMdd}.zip",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != true) return;

            var bytes = await _privacy.CreateDiagnosticBundleAsync(userConsent: true, includeLogs);
            await System.IO.File.WriteAllBytesAsync(dialog.FileName, bytes);
            ShowToast("Diagnostica esportata", "Pacchetto creato localmente. Nessun upload è stato effettuato.");
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Creazione pacchetto non riuscita: {ex.Message}", "Errore diagnostica",
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
            ShowToast("Esportazione completata", "Il report di studio è stato esportato correttamente.");
        }
        catch (Exception ex)
        {
            UiDialogService.Show(this, $"Esportazione non riuscita: {ex.Message}", "Errore esportazione",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisconnectMoodle_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;

        var result = UiDialogService.Show(
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
        var result = UiDialogService.Show(
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
        var result = UiDialogService.Show(
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
        var result = UiDialogService.Show(
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


    private void NavigateToSection_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } ||
            !int.TryParse(tag, out var index) ||
            index < 0 || index >= MainTabs.Items.Count)
            return;

        MainTabs.SelectedIndex = index;
    }
    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles from controls inside the selected page too.
        // Only the navigation TabControl owns the global page heading.
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
            return;

        UpdateSectionHeading();
    }

    private void UpdateSectionHeading()
    {
        var heading = MainTabs.SelectedIndex switch
        {
            0 => ("La tua giornata", "Priorità e riepilogo"),
            1 => ("Le tue materie", "Corsi, esami e archivio"),
            2 => ("Calendario", "Appelli e pianificazione"),
            3 => ("Studio Lab", "Focus, attività e ripasso"),
            4 => ("Accademia", "Apprendimento e strumenti AI"),
            5 => ("Progressi", "Statistiche e andamento"),
            6 => ("Impostazioni", "Profilo, integrazioni e privacy"),
            _ => ("UniToStudyHub", "Il tuo spazio universitario")
        };

        if (MainTabs.Template?.FindName("CurrentSectionTitle", MainTabs) is not TextBlock title ||
            MainTabs.Template.FindName("CurrentSectionSubtitle", MainTabs) is not TextBlock subtitle)
            return;

        title.Text = heading.Item1;
        subtitle.Text = heading.Item2;
    }

}
