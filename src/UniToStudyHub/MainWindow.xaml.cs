using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using CP.AnimationRx.Reactive;
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
    private readonly PrivacyService _privacy;
    private readonly StudyDataImportService _dataImport;
    private readonly UiPreferencesStore _uiPreferencesStore;
    private readonly UiPreferences _uiPreferences;
    private readonly SerialDisposable _sidebarWidthAnimation = new();
    private bool _sidebarPinned;

    private static readonly (string Title, string Subtitle)[] SectionHeaders =
    [
        ("Oggi", "Priorità di oggi e andamento"),
        ("Materie", "Esami, voti e carriera"),
        ("Calendario", "Appelli, lezioni e pianificazione"),
        ("Studio Lab", "Focus, attività, ripasso e benessere"),
        ("Accademia", "Materiali e apprendimento intelligente"),
        ("Progressi", "Statistiche e obiettivi"),
        ("Impostazioni", "Profilo, integrazioni e dati")
    ];

    public MainWindow(MainViewModel viewModel, AiCredentialStore aiCredentials,
        AiProviderPreferencesStore aiPreferences, PrivacyService privacy,
        StudyDataImportService dataImport, UiPreferencesStore uiPreferencesStore)
    {
        InitializeComponent();
        _uiPreferencesStore = uiPreferencesStore;
        _uiPreferences = _uiPreferencesStore.Load();
        _sidebarPinned = _uiPreferences.SidebarPinned;
        RestoreWindowBounds();
        Loaded += (_, _) =>
        {
            UpdateThemeToggle(ApplicationThemeManager.GetAppTheme());
            UpdateSectionHeader();
            SetSidebarExpanded(_sidebarPinned, animate: false);
            UpdateSidebarPinButton();
            WindowBackgroundManager.UpdateBackground(
                this,
                ApplicationThemeManager.GetAppTheme(),
                Wpf.Ui.Controls.WindowBackdropType.Mica);
        };
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
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
        DataContext = viewModel;
        _focusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusRefreshTimer.Tick += (_, _) => viewModel.Workspace.RefreshFocusSnapshot();
        _focusRefreshTimer.Start();
        Closing += (_, _) =>
        {
            _focusRefreshTimer.Stop();
            SaveUiPreferences();
            _sidebarWidthAnimation.Dispose();
            SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        };
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        var current = ApplicationThemeManager.GetAppTheme();
        if (current == ApplicationTheme.HighContrast)
        {
            UpdateThemeToggle(current, sender as Button);
            return;
        }

        var next = current == ApplicationTheme.Dark
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark;

        ApplicationThemeManager.Apply(next, Wpf.Ui.Controls.WindowBackdropType.Mica);
        _uiPreferences.Theme = next == ApplicationTheme.Light ? "Light" : "Dark";
        UpdateThemeToggle(next, sender as Button);
        SaveUiPreferences();
    }

    private void UpdateThemeToggle(ApplicationTheme theme, Button? source = null)
    {
        var button = source ?? FindTemplatePart<Button>("ThemeToggleButton");
        if (button is null)
            return;

        if (theme == ApplicationTheme.HighContrast)
        {
            button.Content = "Contrasto elevato";
            button.IsEnabled = false;
            return;
        }

        button.IsEnabled = true;
        button.Content = theme == ApplicationTheme.Dark
            ? "Tema chiaro"
            : "Tema scuro";
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (!string.Equals(args.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal))
            return;

        Dispatcher.BeginInvoke(new Action(() =>
            UpdateThemeToggle(ApplicationThemeManager.GetAppTheme())));
    }

    private T? FindTemplatePart<T>(string name) where T : class =>
        MainTabs.Template?.FindName(name, MainTabs) as T;

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.Source, MainTabs))
            UpdateSectionHeader();
    }

    private void UpdateSectionHeader()
    {
        var index = MainTabs.SelectedIndex;
        if (index < 0 || index >= SectionHeaders.Length)
            return;

        if (FindTemplatePart<TextBlock>("TopbarTitle") is { } title)
            title.Text = SectionHeaders[index].Title;
        if (FindTemplatePart<TextBlock>("TopbarSubtitle") is { } subtitle)
            subtitle.Text = SectionHeaders[index].Subtitle;
    }

    private void Sidebar_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_sidebarPinned)
            SetSidebarExpanded(true, animate: true);
    }

    private void Sidebar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!_sidebarPinned)
            SetSidebarExpanded(false, animate: true);
    }

    private void ToggleSidebarPin_Click(object sender, RoutedEventArgs e)
    {
        _sidebarPinned = !_sidebarPinned;
        _uiPreferences.SidebarPinned = _sidebarPinned;
        var sidebar = FindTemplatePart<Border>("SidebarSurface");
        SetSidebarExpanded(_sidebarPinned || sidebar?.IsMouseOver == true, animate: true);
        UpdateSidebarPinButton();
        SaveUiPreferences();
    }

    private void SetSidebarExpanded(bool expanded, bool animate)
    {
        var sidebar = FindTemplatePart<Border>("SidebarSurface");
        if (sidebar is null)
            return;

        var targetWidth = expanded ? 256d : 92d;
        if (animate)
        {
            _sidebarWidthAnimation.Disposable = sidebar
                .WidthTo(240, targetWidth, Ease.ExpoOut)
                .Subscribe();
        }
        else
        {
            sidebar.Width = targetWidth;
        }

        foreach (var tab in MainTabs.Items.OfType<TabItem>())
        {
            tab.Width = expanded ? 232 : 64;
            if (tab.Header is StackPanel header &&
                header.Children.Count > 1 &&
                header.Children[1] is StackPanel labels)
            {
                var label = labels.Children.OfType<TextBlock>().FirstOrDefault()?.Text;
                labels.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                tab.ToolTip = expanded ? null : label;
            }
        }

        if (FindTemplatePart<StackPanel>("SidebarBrandText") is { } brandText)
            brandText.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (FindTemplatePart<TextBlock>("SidebarSectionLabel") is { } sectionLabel)
            sectionLabel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (FindTemplatePart<StackPanel>("SidebarFooter") is { } footer)
            footer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;

        UpdateSidebarPinButton();
    }

    private void UpdateSidebarPinButton()
    {
        if (FindTemplatePart<Button>("SidebarPinButton") is not { } pinButton)
            return;

        pinButton.Content = _sidebarPinned ? "●" : "⌖";
        pinButton.ToolTip = _sidebarPinned
            ? "Sblocca la sidebar"
            : "Blocca la sidebar aperta";
        pinButton.Foreground = _sidebarPinned
            ? (System.Windows.Media.Brush)FindResource("AccentBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
    }

    private void RestoreWindowBounds()
    {
        if (_uiPreferences.WindowWidth >= MinWidth && _uiPreferences.WindowHeight >= MinHeight)
        {
            Width = Math.Min(_uiPreferences.WindowWidth,
                Math.Max(MinWidth, SystemParameters.VirtualScreenWidth));
            Height = Math.Min(_uiPreferences.WindowHeight,
                Math.Max(MinHeight, SystemParameters.VirtualScreenHeight));

            if (_uiPreferences.WindowLeft.HasValue && _uiPreferences.WindowTop.HasValue)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                var minLeft = SystemParameters.VirtualScreenLeft;
                var minTop = SystemParameters.VirtualScreenTop;
                var maxLeft = Math.Max(minLeft, minLeft + SystemParameters.VirtualScreenWidth - Width);
                var maxTop = Math.Max(minTop, minTop + SystemParameters.VirtualScreenHeight - Height);
                Left = Math.Clamp(_uiPreferences.WindowLeft.Value, minLeft, maxLeft);
                Top = Math.Clamp(_uiPreferences.WindowTop.Value, minTop, maxTop);
            }
        }

        if (_uiPreferences.IsMaximized)
            WindowState = WindowState.Maximized;
    }

    private void SaveUiPreferences()
    {
        var bounds = WindowState == WindowState.Maximized && !RestoreBounds.IsEmpty
            ? RestoreBounds
            : new Rect(Left, Top,
                ActualWidth > 0 ? ActualWidth : Width,
                ActualHeight > 0 ? ActualHeight : Height);

        if (bounds.Width > 0 && bounds.Height > 0)
        {
            _uiPreferences.WindowWidth = bounds.Width;
            _uiPreferences.WindowHeight = bounds.Height;
            _uiPreferences.WindowLeft = bounds.Left;
            _uiPreferences.WindowTop = bounds.Top;
        }

        _uiPreferences.IsMaximized = WindowState == WindowState.Maximized;
        _uiPreferences.SidebarPinned = _sidebarPinned;
        var currentTheme = ApplicationThemeManager.GetAppTheme();
        if (currentTheme is ApplicationTheme.Dark or ApplicationTheme.Light)
            _uiPreferences.Theme = currentTheme == ApplicationTheme.Light ? "Light" : "Dark";

        try
        {
            _uiPreferencesStore.Save(_uiPreferences);
        }
        catch (IOException)
        {
            // UI preferences are non-critical; a failed write must not block app shutdown.
        }
        catch (UnauthorizedAccessException)
        {
            // UI preferences are non-critical; a failed write must not block app shutdown.
        }
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e)
    {
        var provider = AiProviderBox.SelectedValue as string;
        var model = AiModelBox.Text.Trim();
        var consent = AiConsentBox.IsChecked == true;
        if (provider is not ("openai" or "ollama" or "gemini"))
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
            if ((provider is "openai" or "gemini") && !string.IsNullOrWhiteSpace(AiApiKeyBox.Password))
                _aiCredentials.SaveKey(provider, AiApiKeyBox.Password);
            if ((provider is "openai" or "gemini") && consent &&
                string.IsNullOrWhiteSpace(_aiCredentials.LoadKey(provider)) &&
                string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(provider == "gemini" ? "STUDYHUB_GEMINI_API_KEY" : "STUDYHUB_OPENAI_API_KEY")))
            {
                MessageBox.Show(this, "Per usare il provider selezionato devi inserire una chiave API. La chiave viene salvata in forma protetta da DPAPI.", "Chiave mancante",
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
        if (provider is not ("openai" or "ollama" or "gemini")) provider = "openai";
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
            MessageBox.Show(this,
                "Esportazione completata. Il file contiene dati personali e note private, ma non token Moodle né chiavi AI: conservalo in un luogo sicuro.",
                "Esportazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Esportazione non riuscita: {ex.Message}", "Errore",
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

            var choice = MessageBox.Show(this,
                summary + "\n\nSÌ = Merge (aggiungi solo record nuovi)\nNO = Replace (sostituisci tutti i dati dopo backup cifrato)\nANNULLA = non modificare nulla.",
                "Anteprima importazione · nessuna modifica ancora effettuata",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Information, MessageBoxResult.Cancel);
            if (choice == MessageBoxResult.Cancel) return;

            var strategy = choice == MessageBoxResult.Yes
                ? StudyDataImportStrategy.Merge
                : StudyDataImportStrategy.Replace;
            if (strategy == StudyDataImportStrategy.Replace)
            {
                var confirm = MessageBox.Show(this,
                    "ATTENZIONE: Replace sostituirà i dati locali con quelli del file. Prima verrà creato un backup cifrato. Vuoi continuare?",
                    "Conferma sostituzione completa", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No);
                if (confirm != MessageBoxResult.Yes) return;
            }

            var result = await _dataImport.ApplyAsync(preview, strategy);
            MessageBox.Show(this,
                $"Importazione completata. Record aggiunti: {result.AddedRecords}. " +
                (result.SafetyBackupPath is null ? string.Empty : $"Backup di sicurezza: {System.IO.Path.GetFileName(result.SafetyBackupPath)}. ") +
                "L'app verrà riavviata per ricaricare i dati.",
                "Importazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Importazione non riuscita. I dati locali non sono stati sostituiti: {ex.Message}",
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
        var confirm = MessageBox.Show(this,
            $"Verranno eliminati {description}\n\nOperazione non annullabile. Continuare?",
            "Conferma reset privacy", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await _privacy.ResetAsync(scope, confirmed: true,
                createSafetyBackup: PrivacySafetyBackupBox.IsChecked == true);
            MessageBox.Show(this,
                $"Reset completato. Record rimossi: {result.DeletedRecords}. File rimossi: {result.DeletedFiles}. " +
                (result.CredentialsRemoved ? "Credenziali Moodle rimosse. " : string.Empty) +
                "L'app verrà chiusa per ricaricare i dati.",
                "Reset completato", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Reset non riuscito: {ex.Message}", "Errore privacy",
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
            MessageBox.Show(this, message, "Inventario dei dati locali",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Impossibile leggere l'inventario privacy: {ex.Message}",
                "Errore privacy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteAllPrivacyData_Click(object sender, RoutedEventArgs e)
    {
        var includeBackups = PrivacyDeleteBackupsBox.IsChecked == true;
        var backupText = includeBackups
            ? "Anche tutti i backup cifrati verranno eliminati."
            : "I backup cifrati esistenti resteranno sul dispositivo.";
        var confirm = MessageBox.Show(this,
            "ATTENZIONE: verranno eliminati tutti i corsi, voti, eventi, note, materiali e dati di studio. Saranno rimossi token Moodle e chiavi AI e revocato il consenso AI. " +
            backupText + "\n\nQuesta operazione non è annullabile. Hai già esportato i dati che vuoi conservare?",
            "Cancellazione completa dei dati", MessageBoxButton.YesNo,
            MessageBoxImage.Stop, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var result = await _privacy.DeleteAllAsync(includeBackups);
            MessageBox.Show(this,
                $"Cancellazione completata. Record: {result.DeletedRecords}; backup: {result.DeletedBackups}; " +
                $"file Moodle: {result.DeletedMoodleFiles}; log: {result.DeletedLogFiles}. " +
                "L'app verrà chiusa.",
                "Dati eliminati", MessageBoxButton.OK, MessageBoxImage.Information);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Cancellazione non riuscita: {ex.Message}",
                "Errore privacy", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ExportDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var preview = _privacy.PreviewDiagnosticBundle();
            var consent = MessageBox.Show(this,
                $"Anteprima pacchetto diagnostico\n\nVersione: {preview.ApplicationVersion}\nSistema: {preview.OperatingSystem}\nRuntime: {preview.Runtime}\nLog disponibili: {preview.AvailableLogFiles}\nDimensione log: {preview.AvailableLogBytes:N0} byte\n\nNon saranno inclusi database personali, PDF, token o chiavi API. Nessun dato verrà inviato automaticamente. Vuoi creare il pacchetto?",
                "Consenso diagnostica", MessageBoxButton.YesNo,
                MessageBoxImage.Information, MessageBoxResult.No);
            if (consent != MessageBoxResult.Yes) return;

            var includeLogs = MessageBox.Show(this,
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
            MessageBox.Show(this, "Pacchetto diagnostico creato localmente. Nessun upload è stato effettuato.",
                "Diagnostica esportata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Creazione pacchetto non riuscita: {ex.Message}", "Errore diagnostica",
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
