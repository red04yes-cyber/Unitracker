using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using UniToStudyHub.Models;
using UniToStudyHub.Presentation.Commands;
using UniToStudyHub.Services;

namespace UniToStudyHub.Presentation.ViewModels;

/// <summary>
/// Presentation adapter for the persisted Study Lab, PDF, progress and backup
/// services. It owns no business rules or parallel persistence.
/// </summary>
public sealed class StudyWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IStudyDataStore _store;
    private readonly FocusSessionService _focus;
    private readonly StudyTaskService _tasks;
    private readonly ErrorJournalService _errors;
    private readonly FlashcardService _flashcards;
    private readonly ProgressService _progress;
    private readonly PdfImportService _pdfImport;
    private readonly PdfIntelligenceService _pdfIntelligence;
    private readonly AiStudyService _aiStudy;
    private readonly BackupService _backups;
    private StudyData _data = new();
    private Course? _selectedCourse;
    private StudyTaskData? _selectedTask;
    private FlashcardData? _selectedDueFlashcard;
    private ErrorJournalEntryData? _selectedErrorEntry;
    private PdfDocumentData? _selectedPdf;
    private BackupInfo? _selectedBackup;
    private string _focusTopicInput = string.Empty;
    private string _focusMinutesInput = "25";
    private string _focusRemainingDisplay = "25:00";
    private string _focusElapsedDisplay = "00:00";
    private string _focusStateDisplay = "Pronto";
    private int _focusProgressPercent;
    private string _taskTitleInput = string.Empty;
    private string _taskMinutesInput = "30";
    private string _flashcardFrontInput = string.Empty;
    private string _flashcardBackInput = string.Empty;
    private string _errorTopicInput = string.Empty;
    private string _errorQuestionInput = string.Empty;
    private string _errorIncorrectInput = string.Empty;
    private string _errorCorrectInput = string.Empty;
    private string _errorCauseInput = string.Empty;
    private bool _flashcardRevealed;
    private string _pdfSearchQuery = string.Empty;
    private string _pdfQuestionInput = string.Empty;
    private string _pdfStatus = "Importa un PDF testuale per iniziare.";
    private string _aiStatus = "Provider AI non configurato: nessuna richiesta remota verrà effettuata.";
    private string _aiResponseText = string.Empty;
    private string _backupStatus = "I backup locali non sono ancora stati verificati.";
    private string _workspaceStatus = string.Empty;
    private ProgressSnapshot? _progressSnapshot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StudyTaskData> ActiveTasks { get; } = new();
    public ObservableCollection<FlashcardData> DueFlashcards { get; } = new();
    public ObservableCollection<ErrorJournalEntryData> OpenErrorEntries { get; } = new();
    public ObservableCollection<PdfSearchHit> PdfSearchHits { get; } = new();
    public ObservableCollection<BackupInfo> BackupItems { get; } = new();
    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<PdfDocumentData> PdfDocuments => _data.PdfDocuments;

    public Course? SelectedCourse { get => _selectedCourse; set => Set(ref _selectedCourse, value); }
    public StudyTaskData? SelectedTask { get => _selectedTask; set { Set(ref _selectedTask, value); CompleteTaskCommand.NotifyCanExecuteChanged(); RemoveTaskCommand.NotifyCanExecuteChanged(); } }
    public FlashcardData? SelectedDueFlashcard { get => _selectedDueFlashcard; set { Set(ref _selectedDueFlashcard, value); FlashcardRevealed = false; NotifyReviewCommands(); } }
    public ErrorJournalEntryData? SelectedErrorEntry { get => _selectedErrorEntry; set { Set(ref _selectedErrorEntry, value); ReviewErrorNeedsWorkCommand.NotifyCanExecuteChanged(); ReviewErrorRememberedCommand.NotifyCanExecuteChanged(); } }
    public PdfDocumentData? SelectedPdf { get => _selectedPdf; set { Set(ref _selectedPdf, value); PdfSearchHits.Clear(); OnPropertyChanged(nameof(HasSelectedPdf)); SearchPdfCommand.NotifyCanExecuteChanged(); AskPdfCommand.NotifyCanExecuteChanged(); } }
    public BackupInfo? SelectedBackup { get => _selectedBackup; set { Set(ref _selectedBackup, value); VerifyBackupCommand.NotifyCanExecuteChanged(); } }

    public string FocusTopicInput { get => _focusTopicInput; set => Set(ref _focusTopicInput, value); }
    public string FocusMinutesInput { get => _focusMinutesInput; set => Set(ref _focusMinutesInput, value); }
    public string FocusRemainingDisplay { get => _focusRemainingDisplay; private set => Set(ref _focusRemainingDisplay, value); }
    public string FocusElapsedDisplay { get => _focusElapsedDisplay; private set => Set(ref _focusElapsedDisplay, value); }
    public string FocusStateDisplay { get => _focusStateDisplay; private set => Set(ref _focusStateDisplay, value); }
    public int FocusProgressPercent { get => _focusProgressPercent; private set => Set(ref _focusProgressPercent, value); }
    public bool HasActiveFocusSession => _focus.GetActive(_data) is not null;
    public bool CanPauseFocus => _focus.GetActive(_data)?.State == StudySessionState.Running;
    public bool CanResumeFocus => _focus.GetActive(_data)?.State == StudySessionState.Paused;
    public bool CanFinishFocus => HasActiveFocusSession;

    public string TaskTitleInput { get => _taskTitleInput; set => Set(ref _taskTitleInput, value); }
    public string TaskMinutesInput { get => _taskMinutesInput; set => Set(ref _taskMinutesInput, value); }
    public string FlashcardFrontInput { get => _flashcardFrontInput; set => Set(ref _flashcardFrontInput, value); }
    public string FlashcardBackInput { get => _flashcardBackInput; set => Set(ref _flashcardBackInput, value); }
    public string ErrorTopicInput { get => _errorTopicInput; set => Set(ref _errorTopicInput, value); }
    public string ErrorQuestionInput { get => _errorQuestionInput; set => Set(ref _errorQuestionInput, value); }
    public string ErrorIncorrectInput { get => _errorIncorrectInput; set => Set(ref _errorIncorrectInput, value); }
    public string ErrorCorrectInput { get => _errorCorrectInput; set => Set(ref _errorCorrectInput, value); }
    public string ErrorCauseInput { get => _errorCauseInput; set => Set(ref _errorCauseInput, value); }
    public bool FlashcardRevealed { get => _flashcardRevealed; set => Set(ref _flashcardRevealed, value); }
    public string PdfSearchQuery { get => _pdfSearchQuery; set => Set(ref _pdfSearchQuery, value); }
    public string PdfQuestionInput { get => _pdfQuestionInput; set => Set(ref _pdfQuestionInput, value); }
    public string PdfStatus { get => _pdfStatus; private set => Set(ref _pdfStatus, value); }
    public string AiStatusMessage { get => _aiStatus; private set => Set(ref _aiStatus, value); }
    public string AiResponseText { get => _aiResponseText; private set => Set(ref _aiResponseText, value); }
    public string BackupStatus { get => _backupStatus; private set => Set(ref _backupStatus, value); }
    public string WorkspaceStatus { get => _workspaceStatus; private set => Set(ref _workspaceStatus, value); }
    public string BackupDirectory => _backups.BackupDirectory;
    public ProgressSnapshot? CurrentProgress => _progressSnapshot;
    public bool HasProgressData => _progressSnapshot is { } p && (p.FocusMinutes > 0 || p.CompletedSessions > 0 || p.CompletedTasks > 0);
    public string ProgressPeriodDisplay => _progressSnapshot is { } p
        ? $"{p.From:dd/MM} – {p.To:dd/MM/yyyy}"
        : "Ultimi sette giorni";
    public string FocusMinutesDisplay => HasProgressData && _progressSnapshot is { } p ? $"{p.FocusMinutes} min" : "Dati insufficienti";
    public string CompletedSessionsDisplay => HasProgressData && _progressSnapshot is { } p ? p.CompletedSessions.ToString(CultureInfo.CurrentCulture) : "Dati insufficienti";
    public string CompletedTasksDisplay => HasProgressData && _progressSnapshot is { } p ? p.CompletedTasks.ToString(CultureInfo.CurrentCulture) : "Dati insufficienti";
    public string DueFlashcardsDisplay => HasProgressData && _progressSnapshot is { } p ? p.DueFlashcards.ToString(CultureInfo.CurrentCulture) : "Dati insufficienti";
    public string OpenErrorsDisplay => HasProgressData && _progressSnapshot is { } p ? p.OpenErrors.ToString(CultureInfo.CurrentCulture) : "Dati insufficienti";
    public string FocusStreakDisplay => _progressSnapshot is { } p ? $"{p.FocusStreakDays} giorni" : "—";
    public string WeeklyGoalDisplay => _progressSnapshot is { WeeklyGoalConfigured: true } p
        ? $"{p.FocusMinutes} / {p.WeeklyGoalMinutes} min"
        : "Obiettivo settimanale non configurato";
    public bool HasSelectedPdf => SelectedPdf is not null;
    public bool HasDueFlashcards => DueFlashcards.Count > 0;
    public bool HasActiveTasks => ActiveTasks.Count > 0;
    public bool HasBackups => BackupItems.Count > 0;
    public string PdfDocumentSummary => SelectedPdf is null
        ? "Seleziona un documento."
        : $"{SelectedPdf.PageCount} pagine · {(SelectedPdf.HasExtractableText ? "Testo estraibile" : "OCR richiesto")}";

    public AsyncRelayCommand StartFocusCommand { get; }
    public AsyncRelayCommand PauseFocusCommand { get; }
    public AsyncRelayCommand ResumeFocusCommand { get; }
    public AsyncRelayCommand FinishFocusCommand { get; }
    public AsyncRelayCommand CancelFocusCommand { get; }
    public AsyncRelayCommand AddTaskCommand { get; }
    public AsyncRelayCommand CompleteTaskCommand { get; }
    public AsyncRelayCommand RemoveTaskCommand { get; }
    public AsyncRelayCommand AddFlashcardCommand { get; }
    public AsyncRelayCommand AddErrorEntryCommand { get; }
    public AsyncRelayCommand ReviewErrorNeedsWorkCommand { get; }
    public AsyncRelayCommand ReviewErrorRememberedCommand { get; }
    public AsyncRelayCommand ReviewAgainCommand { get; }
    public AsyncRelayCommand ReviewHardCommand { get; }
    public AsyncRelayCommand ReviewGoodCommand { get; }
    public AsyncRelayCommand ReviewEasyCommand { get; }
    public AsyncRelayCommand SearchPdfCommand { get; }
    public AsyncRelayCommand AskPdfCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand VerifyBackupCommand { get; }

    public StudyWorkspaceViewModel(
        IStudyDataStore store,
        FocusSessionService focus,
        StudyTaskService tasks,
        ErrorJournalService errors,
        FlashcardService flashcards,
        ProgressService progress,
        PdfImportService pdfImport,
        PdfIntelligenceService pdfIntelligence,
        AiStudyService aiStudy,
        BackupService backups)
    {
        _store = store;
        _focus = focus;
        _tasks = tasks;
        _errors = errors;
        _flashcards = flashcards;
        _progress = progress;
        _pdfImport = pdfImport;
        _pdfIntelligence = pdfIntelligence;
        _aiStudy = aiStudy;
        _backups = backups;

        StartFocusCommand = new AsyncRelayCommand(StartFocusAsync, () => !HasActiveFocusSession);
        PauseFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("pause"), () => CanPauseFocus);
        ResumeFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("resume"), () => CanResumeFocus);
        FinishFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("finish"), () => CanFinishFocus);
        CancelFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("cancel"), () => CanFinishFocus);
        AddTaskCommand = new AsyncRelayCommand(AddTaskAsync);
        CompleteTaskCommand = new AsyncRelayCommand(CompleteTaskAsync, () => SelectedTask is not null);
        RemoveTaskCommand = new AsyncRelayCommand(RemoveTaskAsync, () => SelectedTask is not null);
        AddFlashcardCommand = new AsyncRelayCommand(AddFlashcardAsync);
        AddErrorEntryCommand = new AsyncRelayCommand(AddErrorEntryAsync);
        ReviewErrorNeedsWorkCommand = new AsyncRelayCommand(() => ReviewErrorAsync(false), () => SelectedErrorEntry is not null);
        ReviewErrorRememberedCommand = new AsyncRelayCommand(() => ReviewErrorAsync(true), () => SelectedErrorEntry is not null);
        ReviewAgainCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Again), () => SelectedDueFlashcard is not null);
        ReviewHardCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Hard), () => SelectedDueFlashcard is not null);
        ReviewGoodCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Good), () => SelectedDueFlashcard is not null);
        ReviewEasyCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Easy), () => SelectedDueFlashcard is not null);
        SearchPdfCommand = new AsyncRelayCommand(SearchPdfAsync, () => SelectedPdf is not null);
        AskPdfCommand = new AsyncRelayCommand(AskPdfAsync, () => SelectedPdf is not null);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        VerifyBackupCommand = new AsyncRelayCommand(VerifyBackupAsync, () => SelectedBackup is not null);
    }

    public void Initialize(StudyData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        RefreshCollections();
        RefreshFocusSnapshot();
        RefreshProgress();
        ReloadBackups();
        OnPropertyChanged(nameof(Courses));
        OnPropertyChanged(nameof(PdfDocuments));
        OnPropertyChanged(nameof(BackupDirectory));
    }

    public void RefreshFocusSnapshot()
    {
        var active = _focus.GetActive(_data);
        if (active is null)
        {
            FocusStateDisplay = "Pronto";
            FocusRemainingDisplay = "—";
            FocusElapsedDisplay = "00:00";
            FocusProgressPercent = 0;
        }
        else
        {
            var snapshot = _focus.Snapshot(_data, active.Id);
            FocusStateDisplay = snapshot.State == StudySessionState.Running ? "In corso" : "In pausa";
            FocusRemainingDisplay = FormatClock(snapshot.RemainingSeconds);
            FocusElapsedDisplay = FormatClock(snapshot.ElapsedSeconds);
            FocusProgressPercent = active.PlannedMinutes <= 0 ? 0 :
                Math.Clamp((int)Math.Round(snapshot.ElapsedSeconds * 100d / (active.PlannedMinutes * 60d)), 0, 100);
        }
        NotifyFocusState();
    }

    public async Task ImportPdfAsync(string path)
    {
        try
        {
            var result = await _pdfImport.ImportAsync(path, _data, SelectedCourse?.Id);
            await _store.SaveAsync(_data);
            SelectedPdf = result.Document;
            PdfStatus = result.WasDuplicate
                ? "Documento già presente: non è stata creata una copia."
                : result.RequiresOcr
                    ? "Importato, ma non contiene testo estraibile. Per la ricerca serve OCR."
                    : $"Importato correttamente · {result.Document.PageCount} pagine.";
            if (result.Warnings.Count > 0) PdfStatus += " " + string.Join(" ", result.Warnings);
            OnPropertyChanged(nameof(PdfDocuments));
        }
        catch (Exception ex)
        {
            PdfStatus = $"Importazione non riuscita: {ex.Message}";
        }
    }

    private async Task StartFocusAsync()
    {
        if (!int.TryParse(FocusMinutesInput, out var minutes) || minutes is < 1 or > 1440)
        {
            WorkspaceStatus = "La durata deve essere compresa tra 1 e 1440 minuti.";
            return;
        }
        try
        {
            _focus.Start(_data, SelectedCourse?.Id, FocusTopicInput, minutes);
            await SaveAsync();
            FocusTopicInput = string.Empty;
            RefreshFocusSnapshot();
        }
        catch (Exception ex) { WorkspaceStatus = $"Sessione non avviata: {ex.Message}"; }
    }

    private async Task ChangeFocusAsync(string action)
    {
        var active = _focus.GetActive(_data);
        if (active is null) return;
        try
        {
            switch (action)
            {
                case "pause": _focus.Pause(_data, active.Id); break;
                case "resume": _focus.Resume(_data, active.Id); break;
                case "finish": _focus.Finish(_data, active.Id); break;
                case "cancel": _focus.Cancel(_data, active.Id); break;
            }
            await SaveAsync();
            RefreshFocusSnapshot();
            RefreshProgress();
            RefreshCollections();
        }
        catch (Exception ex) { WorkspaceStatus = $"Sessione non aggiornata: {ex.Message}"; }
    }

    private async Task AddTaskAsync()
    {
        if (string.IsNullOrWhiteSpace(TaskTitleInput) ||
            !int.TryParse(TaskMinutesInput, out var minutes) || minutes is < 0 or > 14400)
            return;
        try
        {
            _tasks.Add(_data, new StudyTaskData
            {
                Title = TaskTitleInput.Trim(),
                EstimatedMinutes = minutes,
                CourseId = SelectedCourse?.Id ?? string.Empty
            });
            await SaveAsync();
            TaskTitleInput = string.Empty;
            RefreshCollections();
            RefreshProgress();
        }
        catch (Exception ex) { WorkspaceStatus = $"Task non salvato: {ex.Message}"; }
    }

    private async Task CompleteTaskAsync()
    {
        if (SelectedTask is null) return;
        try { _tasks.Complete(_data, SelectedTask.Id); await SaveAsync(); SelectedTask = null; RefreshCollections(); RefreshProgress(); }
        catch (Exception ex) { WorkspaceStatus = $"Task non completato: {ex.Message}"; }
    }

    private async Task RemoveTaskAsync()
    {
        if (SelectedTask is null) return;
        try { _tasks.Remove(_data, SelectedTask.Id); await SaveAsync(); SelectedTask = null; RefreshCollections(); }
        catch (Exception ex) { WorkspaceStatus = $"Task non rimosso: {ex.Message}"; }
    }

    private async Task AddFlashcardAsync()
    {
        if (string.IsNullOrWhiteSpace(FlashcardFrontInput) || string.IsNullOrWhiteSpace(FlashcardBackInput)) return;
        try
        {
            _flashcards.Add(_data, new FlashcardData
            {
                Front = FlashcardFrontInput.Trim(),
                Back = FlashcardBackInput.Trim(),
                CourseId = SelectedCourse?.Id ?? string.Empty
            });
            await SaveAsync();
            FlashcardFrontInput = string.Empty;
            FlashcardBackInput = string.Empty;
            RefreshCollections();
            RefreshProgress();
        }
        catch (Exception ex) { WorkspaceStatus = $"Flashcard non salvata: {ex.Message}"; }
    }

    private async Task AddErrorEntryAsync()
    {
        if (string.IsNullOrWhiteSpace(ErrorQuestionInput) || string.IsNullOrWhiteSpace(ErrorCorrectInput)) return;
        try
        {
            _errors.Add(_data, new ErrorJournalEntryData
            {
                CourseId = SelectedCourse?.Id ?? string.Empty,
                Topic = ErrorTopicInput.Trim(),
                Question = ErrorQuestionInput.Trim(),
                IncorrectAnswer = ErrorIncorrectInput.Trim(),
                CorrectAnswer = ErrorCorrectInput.Trim(),
                RootCause = ErrorCauseInput.Trim()
            });
            await SaveAsync();
            ErrorTopicInput = string.Empty;
            ErrorQuestionInput = string.Empty;
            ErrorIncorrectInput = string.Empty;
            ErrorCorrectInput = string.Empty;
            ErrorCauseInput = string.Empty;
            RefreshCollections();
        }
        catch (Exception ex) { WorkspaceStatus = $"Errore non registrato: {ex.Message}"; }
    }

    private async Task ReviewErrorAsync(bool recalled)
    {
        if (SelectedErrorEntry is null) return;
        try
        {
            _errors.Review(_data, SelectedErrorEntry.Id, recalled);
            await SaveAsync();
            SelectedErrorEntry = null;
            RefreshCollections();
            RefreshProgress();
        }
        catch (Exception ex) { WorkspaceStatus = $"Ripasso errore non salvato: {ex.Message}"; }
    }

    private async Task ReviewFlashcardAsync(RecallRating rating)
    {
        if (SelectedDueFlashcard is null) return;
        try
        {
            _flashcards.Review(_data, SelectedDueFlashcard.Id, rating);
            await SaveAsync();
            SelectedDueFlashcard = null;
            RefreshCollections();
            RefreshProgress();
        }
        catch (Exception ex) { WorkspaceStatus = $"Ripasso non salvato: {ex.Message}"; }
    }

    private async Task SearchPdfAsync()
    {
        if (SelectedPdf is null) return;
        PdfSearchHits.Clear();
        foreach (var hit in _pdfIntelligence.Search(SelectedPdf, PdfSearchQuery))
            PdfSearchHits.Add(hit);
        PdfStatus = PdfSearchHits.Count == 0
            ? "Nessun risultato nel testo estraibile del documento."
            : $"{PdfSearchHits.Count} risultati trovati. Le pagine sono riferimenti al testo importato.";
        await Task.CompletedTask;
    }

    private async Task AskPdfAsync()
    {
        if (SelectedPdf is null || string.IsNullOrWhiteSpace(PdfQuestionInput)) return;
        try
        {
            var response = await _aiStudy.AnswerFromDocumentAsync(SelectedPdf, PdfQuestionInput);
            AiStatusMessage = response.Status switch
            {
                AiStatus.NotConfigured => "AI non configurata. Nessun contenuto è stato inviato e non è stata generata una risposta.",
                AiStatus.Configured => $"Risposta da {response.Provider}. Verifica sempre i passaggi nel testo originale.",
                AiStatus.Refused => "Richiesta non elaborata dal provider.",
                _ => response.Error ?? "Errore del provider AI."
            };
            AiResponseText = response.Status == AiStatus.Configured ? response.Content : string.Empty;
        }
        catch (Exception ex)
        {
            AiStatusMessage = $"Richiesta non completata: {ex.Message}";
            AiResponseText = string.Empty;
        }
    }

    private async Task CreateBackupAsync()
    {
        try
        {
            var created = await _backups.CreateBackupAsync();
            BackupStatus = created.IntegrityVerified
                ? $"Backup creato e verificato · {created.FilePath}"
                : $"Backup creato, ma l'integrità non è stata verificata · {created.FilePath}";
            ReloadBackups();
            SelectedBackup = BackupItems.FirstOrDefault(x => x.FilePath == created.FilePath);
        }
        catch (Exception ex) { BackupStatus = $"Backup non creato: {ex.Message}"; }
    }

    private async Task VerifyBackupAsync()
    {
        if (SelectedBackup is null) return;
        try
        {
            var verified = await _backups.VerifyBackupAsync(SelectedBackup.FilePath);
            BackupStatus = verified
                ? $"Integrità SQLite verificata · {SelectedBackup.FilePath}"
                : $"Verifica non superata · {SelectedBackup.FilePath}";
            ReloadBackups();
            SelectedBackup = BackupItems.FirstOrDefault(x => x.FilePath == SelectedBackup?.FilePath);
        }
        catch (Exception ex) { BackupStatus = $"Verifica non riuscita: {ex.Message}"; }
    }

    private async Task SaveAsync()
    {
        await _store.SaveAsync(_data);
        RefreshProgress();
    }

    private void RefreshCollections()
    {
        ActiveTasks.Clear();
        foreach (var item in _data.StudyTasks
                     .Where(x => x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress)
                     .OrderBy(x => x.DueAt ?? DateTimeOffset.MaxValue)
                     .ThenByDescending(x => x.Priority))
            ActiveTasks.Add(item);
        DueFlashcards.Clear();
        foreach (var item in _flashcards.GetDue(_data)) DueFlashcards.Add(item);
        OpenErrorEntries.Clear();
        foreach (var item in _data.ErrorJournalEntries.Where(x => x.Status != ErrorReviewStatus.Resolved).OrderBy(x => x.NextReviewAt ?? DateTimeOffset.MaxValue))
            OpenErrorEntries.Add(item);
        OnPropertyChanged(nameof(HasActiveTasks));
        OnPropertyChanged(nameof(HasDueFlashcards));
        OnPropertyChanged(nameof(PdfDocuments));
        OnPropertyChanged(nameof(PdfDocumentSummary));
        if (SelectedDueFlashcard is null || !DueFlashcards.Any(x => x.Id == SelectedDueFlashcard.Id))
            SelectedDueFlashcard = DueFlashcards.FirstOrDefault();
    }

    private void RefreshProgress()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        _progressSnapshot = _progress.Calculate(_data, monday, today);
        OnPropertyChanged(nameof(CurrentProgress));
        OnPropertyChanged(nameof(HasProgressData));
        OnPropertyChanged(nameof(ProgressPeriodDisplay));
        OnPropertyChanged(nameof(FocusMinutesDisplay));
        OnPropertyChanged(nameof(CompletedSessionsDisplay));
        OnPropertyChanged(nameof(CompletedTasksDisplay));
        OnPropertyChanged(nameof(DueFlashcardsDisplay));
        OnPropertyChanged(nameof(OpenErrorsDisplay));
        OnPropertyChanged(nameof(FocusStreakDisplay));
        OnPropertyChanged(nameof(WeeklyGoalDisplay));
    }

    private void ReloadBackups()
    {
        BackupItems.Clear();
        foreach (var backup in _backups.ListBackups()) BackupItems.Add(backup);
        OnPropertyChanged(nameof(HasBackups));
    }

    private void NotifyFocusState()
    {
        OnPropertyChanged(nameof(HasActiveFocusSession));
        OnPropertyChanged(nameof(CanPauseFocus));
        OnPropertyChanged(nameof(CanResumeFocus));
        OnPropertyChanged(nameof(CanFinishFocus));
        StartFocusCommand?.NotifyCanExecuteChanged();
        PauseFocusCommand?.NotifyCanExecuteChanged();
        ResumeFocusCommand?.NotifyCanExecuteChanged();
        FinishFocusCommand?.NotifyCanExecuteChanged();
        CancelFocusCommand?.NotifyCanExecuteChanged();
    }

    private void NotifyReviewCommands()
    {
        ReviewAgainCommand?.NotifyCanExecuteChanged();
        ReviewHardCommand?.NotifyCanExecuteChanged();
        ReviewGoodCommand?.NotifyCanExecuteChanged();
        ReviewEasyCommand?.NotifyCanExecuteChanged();
    }

    private static string FormatClock(int seconds) =>
        TimeSpan.FromSeconds(Math.Max(0, seconds)).ToString(seconds >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture);

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
