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
    private readonly AcademicProfileService _academicProfile;
    private readonly FocusSessionService _focus;
    private readonly StudyTaskService _tasks;
    private readonly ErrorJournalService _errors;
    private readonly DistractionService _distractions;
    private readonly EnergyService _energy;
    private readonly FlashcardService _flashcards;
    private readonly ProgressService _progress;
    private readonly PdfImportService _pdfImport;
    private readonly PdfIntelligenceService _pdfIntelligence;
    private readonly AiStudyService _aiStudy;
    private readonly BackupService _backups;
    private readonly HashSet<string> _verifiedBackups = new(StringComparer.OrdinalIgnoreCase);
    private StudyData _data = new();
    private Course? _selectedCourse;
    private StudyTaskData? _selectedTask;
    private FlashcardData? _selectedDueFlashcard;
    private ErrorJournalEntryData? _selectedErrorEntry;
    private DistractionData? _selectedDistraction;
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
    private DateTime? _taskDueDateInput;
    private int _taskPriorityInput = 3;
    private string _flashcardFrontInput = string.Empty;
    private string _flashcardBackInput = string.Empty;
    private string _errorTopicInput = string.Empty;
    private string _errorQuestionInput = string.Empty;
    private string _errorIncorrectInput = string.Empty;
    private string _errorCorrectInput = string.Empty;
    private string _errorCauseInput = string.Empty;
    private string _distractionInput = string.Empty;
    private string _energyNoteInput = string.Empty;
    private int _energyLevelInput = 3;
    private int _selfFocusLevelInput = 3;
    private bool _flashcardRevealed;
    private string _pdfSearchQuery = string.Empty;
    private string _pdfQuestionInput = string.Empty;
    private string _pdfStatus = "Importa un PDF testuale per iniziare.";
    private string _aiStatus = "Provider AI non configurato: nessuna richiesta remota verrà effettuata.";
    private string _aiResponseText = string.Empty;
    private string _backupStatus = "I backup locali non sono ancora stati verificati.";
    private string _backupManifestSummary = "Seleziona e verifica un backup per leggere il manifest.";
    private string _workspaceStatus = string.Empty;
    private string _degreeProgramNameInput = string.Empty;
    private string _degreeProgramCodeInput = string.Empty;
    private string _enrollmentYearInput = string.Empty;
    private string _currentAcademicYearInput = string.Empty;
    private string _degreeTotalCfuInput = string.Empty;
    private DateTime _graduationTargetInput = new(2027, 10, 1);
    private string _studyPreferencesInput = string.Empty;
    private string _weeklyStudyHoursInput = "20";
    private string _maxDailyStudyMinutesInput = "240";
    private ProgressSnapshot? _progressSnapshot;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<StudyTaskData> ActiveTasks { get; } = new();
    public ObservableCollection<FlashcardData> DueFlashcards { get; } = new();
    public ObservableCollection<ErrorJournalEntryData> OpenErrorEntries { get; } = new();
    public ObservableCollection<DistractionData> OpenDistractions { get; } = new();
    public ObservableCollection<EnergyCheckInData> RecentEnergyCheckIns { get; } = new();
    public ObservableCollection<PdfSearchHit> PdfSearchHits { get; } = new();
    public ObservableCollection<BackupInfo> BackupItems { get; } = new();
    public ObservableCollection<CourseProgressRow> CourseProgressRows { get; } = new();
    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<PdfDocumentData> PdfDocuments => _data.PdfDocuments;

    public Course? SelectedCourse { get => _selectedCourse; set => Set(ref _selectedCourse, value); }
    public StudyTaskData? SelectedTask { get => _selectedTask; set { if (!Set(ref _selectedTask, value)) return; OnPropertyChanged(nameof(HasSelectedTask)); CompleteTaskCommand.NotifyCanExecuteChanged(); RemoveTaskCommand.NotifyCanExecuteChanged(); } }
    public bool HasSelectedTask => SelectedTask is not null;
    public FlashcardData? SelectedDueFlashcard { get => _selectedDueFlashcard; set { Set(ref _selectedDueFlashcard, value); FlashcardRevealed = false; NotifyReviewCommands(); } }
    public ErrorJournalEntryData? SelectedErrorEntry { get => _selectedErrorEntry; set { Set(ref _selectedErrorEntry, value); ReviewErrorNeedsWorkCommand.NotifyCanExecuteChanged(); ReviewErrorRememberedCommand.NotifyCanExecuteChanged(); } }
    public DistractionData? SelectedDistraction { get => _selectedDistraction; set { Set(ref _selectedDistraction, value); ResolveDistractionCommand.NotifyCanExecuteChanged(); } }
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
    public DateTime? TaskDueDateInput { get => _taskDueDateInput; set => Set(ref _taskDueDateInput, value); }
    public int TaskPriorityInput { get => _taskPriorityInput; set => Set(ref _taskPriorityInput, value); }
    public string FlashcardFrontInput { get => _flashcardFrontInput; set => Set(ref _flashcardFrontInput, value); }
    public string FlashcardBackInput { get => _flashcardBackInput; set => Set(ref _flashcardBackInput, value); }
    public string ErrorTopicInput { get => _errorTopicInput; set => Set(ref _errorTopicInput, value); }
    public string ErrorQuestionInput { get => _errorQuestionInput; set => Set(ref _errorQuestionInput, value); }
    public string ErrorIncorrectInput { get => _errorIncorrectInput; set => Set(ref _errorIncorrectInput, value); }
    public string ErrorCorrectInput { get => _errorCorrectInput; set => Set(ref _errorCorrectInput, value); }
    public string ErrorCauseInput { get => _errorCauseInput; set => Set(ref _errorCauseInput, value); }
    public string DistractionInput { get => _distractionInput; set => Set(ref _distractionInput, value); }
    public string EnergyNoteInput { get => _energyNoteInput; set => Set(ref _energyNoteInput, value); }
    public int EnergyLevelInput { get => _energyLevelInput; set => Set(ref _energyLevelInput, value); }
    public int SelfFocusLevelInput { get => _selfFocusLevelInput; set => Set(ref _selfFocusLevelInput, value); }
    public bool FlashcardRevealed { get => _flashcardRevealed; set => Set(ref _flashcardRevealed, value); }
    public string PdfSearchQuery { get => _pdfSearchQuery; set => Set(ref _pdfSearchQuery, value); }
    public string PdfQuestionInput { get => _pdfQuestionInput; set => Set(ref _pdfQuestionInput, value); }
    public string PdfStatus { get => _pdfStatus; private set => Set(ref _pdfStatus, value); }
    public string AiStatusMessage { get => _aiStatus; private set => Set(ref _aiStatus, value); }
    public string AiResponseText { get => _aiResponseText; private set => Set(ref _aiResponseText, value); }
    public string BackupStatus { get => _backupStatus; private set => Set(ref _backupStatus, value); }
    public string BackupManifestSummary { get => _backupManifestSummary; private set => Set(ref _backupManifestSummary, value); }
    public string DegreeProgramNameInput { get => _degreeProgramNameInput; set => Set(ref _degreeProgramNameInput, value); }
    public string DegreeProgramCodeInput { get => _degreeProgramCodeInput; set => Set(ref _degreeProgramCodeInput, value); }
    public string EnrollmentYearInput { get => _enrollmentYearInput; set => Set(ref _enrollmentYearInput, value); }
    public string CurrentAcademicYearInput { get => _currentAcademicYearInput; set => Set(ref _currentAcademicYearInput, value); }
    public string DegreeTotalCfuInput { get => _degreeTotalCfuInput; set => Set(ref _degreeTotalCfuInput, value); }
    public DateTime GraduationTargetInput { get => _graduationTargetInput; set => Set(ref _graduationTargetInput, value); }
    public string StudyPreferencesInput { get => _studyPreferencesInput; set => Set(ref _studyPreferencesInput, value); }
    public string WeeklyStudyHoursInput { get => _weeklyStudyHoursInput; set => Set(ref _weeklyStudyHoursInput, value); }
    public string MaxDailyStudyMinutesInput { get => _maxDailyStudyMinutesInput; set => Set(ref _maxDailyStudyMinutesInput, value); }
    public string WorkspaceStatus { get => _workspaceStatus; private set => Set(ref _workspaceStatus, value); }
    public string BackupDirectory => _backups.BackupDirectory;
    public ProgressSnapshot? CurrentProgress => _progressSnapshot;
    public bool HasProgressData => _progressSnapshot is { } p && (p.FocusMinutes > 0 || p.CompletedSessions > 0 || p.CompletedTasks > 0 || p.DueFlashcards > 0 || p.OpenErrors > 0);
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
    public int WeeklyGoalProgressPercent => _progressSnapshot is { WeeklyGoalConfigured: true } p
        ? Math.Clamp((int)Math.Round(p.FocusMinutes * 100d / p.WeeklyGoalMinutes), 0, 100)
        : 0;
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
    public AsyncRelayCommand RevealFlashcardCommand { get; }
    public AsyncRelayCommand AddErrorEntryCommand { get; }
    public AsyncRelayCommand ReviewErrorNeedsWorkCommand { get; }
    public AsyncRelayCommand ReviewErrorRememberedCommand { get; }
    public AsyncRelayCommand AddEnergyCheckInCommand { get; }
    public AsyncRelayCommand AddDistractionCommand { get; }
    public AsyncRelayCommand ResolveDistractionCommand { get; }
    public AsyncRelayCommand ReviewAgainCommand { get; }
    public AsyncRelayCommand ReviewHardCommand { get; }
    public AsyncRelayCommand ReviewGoodCommand { get; }
    public AsyncRelayCommand ReviewEasyCommand { get; }
    public AsyncRelayCommand SearchPdfCommand { get; }
    public AsyncRelayCommand AskPdfCommand { get; }
    public AsyncRelayCommand SaveAcademicProfileCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand VerifyBackupCommand { get; }

    public StudyWorkspaceViewModel(
        IStudyDataStore store,
        AcademicProfileService academicProfile,
        FocusSessionService focus,
        StudyTaskService tasks,
        ErrorJournalService errors,
        DistractionService distractions,
        EnergyService energy,
        FlashcardService flashcards,
        ProgressService progress,
        PdfImportService pdfImport,
        PdfIntelligenceService pdfIntelligence,
        AiStudyService aiStudy,
        BackupService backups)
    {
        _store = store;
        _academicProfile = academicProfile;
        _focus = focus;
        _tasks = tasks;
        _errors = errors;
        _distractions = distractions;
        _energy = energy;
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
        RevealFlashcardCommand = new AsyncRelayCommand(RevealFlashcardAsync, () => SelectedDueFlashcard is not null);
        AddErrorEntryCommand = new AsyncRelayCommand(AddErrorEntryAsync);
        ReviewErrorNeedsWorkCommand = new AsyncRelayCommand(() => ReviewErrorAsync(false), () => SelectedErrorEntry is not null);
        ReviewErrorRememberedCommand = new AsyncRelayCommand(() => ReviewErrorAsync(true), () => SelectedErrorEntry is not null);
        AddEnergyCheckInCommand = new AsyncRelayCommand(AddEnergyCheckInAsync);
        AddDistractionCommand = new AsyncRelayCommand(AddDistractionAsync);
        ResolveDistractionCommand = new AsyncRelayCommand(ResolveDistractionAsync, () => SelectedDistraction is not null);
        ReviewAgainCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Again), () => SelectedDueFlashcard is not null);
        ReviewHardCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Hard), () => SelectedDueFlashcard is not null);
        ReviewGoodCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Good), () => SelectedDueFlashcard is not null);
        ReviewEasyCommand = new AsyncRelayCommand(() => ReviewFlashcardAsync(RecallRating.Easy), () => SelectedDueFlashcard is not null);
        SearchPdfCommand = new AsyncRelayCommand(SearchPdfAsync, () => SelectedPdf is not null);
        AskPdfCommand = new AsyncRelayCommand(AskPdfAsync, () => SelectedPdf is not null);
        SaveAcademicProfileCommand = new AsyncRelayCommand(SaveAcademicProfileAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        VerifyBackupCommand = new AsyncRelayCommand(VerifyBackupAsync, () => SelectedBackup is not null);
    }

    public void Initialize(StudyData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        LoadAcademicProfile();
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
            FocusStateDisplay = snapshot.IsExpired ? "Tempo previsto raggiunto" : snapshot.State == StudySessionState.Running ? "In corso" : "In pausa";
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

    private void LoadAcademicProfile()
    {
        DegreeProgramNameInput = _data.DegreeProgramName;
        DegreeProgramCodeInput = _data.DegreeProgramCode;
        EnrollmentYearInput = _data.EnrollmentYear?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        CurrentAcademicYearInput = _data.CurrentAcademicYear?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
        DegreeTotalCfuInput = _data.DegreeTotalCfu == 0 ? string.Empty : _data.DegreeTotalCfu.ToString(CultureInfo.CurrentCulture);
        GraduationTargetInput = _data.GraduationTarget;
        StudyPreferencesInput = _data.StudyPreferences;
        WeeklyStudyHoursInput = _data.WeeklyStudyHours.ToString(CultureInfo.CurrentCulture);
        MaxDailyStudyMinutesInput = _data.MaxDailyStudyMinutes.ToString(CultureInfo.CurrentCulture);
    }

    private async Task SaveAcademicProfileAsync()
    {
        if (!TryOptionalInt(EnrollmentYearInput, out var enrollmentYear) ||
            !TryOptionalInt(CurrentAcademicYearInput, out var academicYear) ||
            !int.TryParse(DegreeTotalCfuInput, out var totalCfu) && !string.IsNullOrWhiteSpace(DegreeTotalCfuInput) ||
            !int.TryParse(WeeklyStudyHoursInput, out var weeklyHours) ||
            !int.TryParse(MaxDailyStudyMinutesInput, out var dailyMinutes))
        {
            WorkspaceStatus = "Controlla gli anni, i CFU e i limiti di studio inseriti.";
            return;
        }

        var previous = new AcademicProfileUpdate(
            _data.DegreeProgramName, _data.DegreeProgramCode, _data.EnrollmentYear,
            _data.CurrentAcademicYear, _data.DegreeTotalCfu, _data.GraduationTarget,
            _data.StudyPreferences, _data.WeeklyStudyHours, _data.MaxDailyStudyMinutes);
        var update = new AcademicProfileUpdate(
            DegreeProgramNameInput, DegreeProgramCodeInput, enrollmentYear, academicYear,
            totalCfu, GraduationTargetInput, StudyPreferencesInput, weeklyHours, dailyMinutes);
        try
        {
            _academicProfile.Update(_data, update);
            await _store.SaveAsync(_data);
            WorkspaceStatus = "Profilo universitario salvato.";
        }
        catch (Exception ex)
        {
            _academicProfile.Update(_data, previous);
            LoadAcademicProfile();
            WorkspaceStatus = $"Profilo non salvato: {ex.Message}";
        }
    }

    private static bool TryOptionalInt(string value, out int? result)
    {
        if (string.IsNullOrWhiteSpace(value)) { result = null; return true; }
        if (int.TryParse(value, out var parsed)) { result = parsed; return true; }
        result = null;
        return false;
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
            !int.TryParse(TaskMinutesInput, out var minutes) || minutes is < 0 or > 14400 ||
            TaskPriorityInput is < 1 or > 5)
        {
            WorkspaceStatus = "Inserisci un titolo, minuti validi e una priorità da 1 a 5.";
            return;
        }
        try
        {
            _tasks.Add(_data, new StudyTaskData
            {
                Title = TaskTitleInput.Trim(),
                EstimatedMinutes = minutes,
                DueAt = TaskDueDateInput is DateTime dueDate ? new DateTimeOffset(dueDate, TimeZoneInfo.Local.GetUtcOffset(dueDate)) : null,
                Priority = TaskPriorityInput,
                CourseId = SelectedCourse?.Id ?? string.Empty
            });
            await SaveAsync();
            TaskTitleInput = string.Empty;
            TaskDueDateInput = null;
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

    private Task RevealFlashcardAsync()
    {
        FlashcardRevealed = !FlashcardRevealed;
        return Task.CompletedTask;
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

    private async Task AddEnergyCheckInAsync()
    {
        try
        {
            _energy.Add(_data, EnergyLevelInput, SelfFocusLevelInput, EnergyNoteInput);
            await SaveAsync();
            EnergyNoteInput = string.Empty;
            RefreshCollections();
            WorkspaceStatus = "Check-in salvato.";
        }
        catch (Exception ex) { WorkspaceStatus = $"Check-in non salvato: {ex.Message}"; }
    }

    private async Task AddDistractionAsync()
    {
        if (string.IsNullOrWhiteSpace(DistractionInput)) return;
        try
        {
            var active = _focus.GetActive(_data);
            _distractions.Add(_data, DistractionInput, active?.Id);
            await SaveAsync();
            DistractionInput = string.Empty;
            RefreshCollections();
            WorkspaceStatus = "Distrazione annotata.";
        }
        catch (Exception ex) { WorkspaceStatus = $"Annotazione non salvata: {ex.Message}"; }
    }

    private async Task ResolveDistractionAsync()
    {
        if (SelectedDistraction is null) return;
        try
        {
            _distractions.Resolve(_data, SelectedDistraction.Id);
            await SaveAsync();
            SelectedDistraction = null;
            RefreshCollections();
        }
        catch (Exception ex) { WorkspaceStatus = $"Annotazione non aggiornata: {ex.Message}"; }
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
            if (created.IntegrityVerified) _verifiedBackups.Add(created.FilePath);
            var manifest = await _backups.ReadManifestAsync(created.FilePath);
            BackupManifestSummary = manifest is null ? "Manifest non disponibile." : $"Formato v{manifest.FormatVersion} · SHA-256 {manifest.Sha256}";
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
            var path = SelectedBackup.FilePath;
            var verified = await _backups.VerifyBackupAsync(path);
            if (verified) _verifiedBackups.Add(path); else _verifiedBackups.Remove(path);
            var manifest = verified ? await _backups.ReadManifestAsync(path) : null;
            BackupManifestSummary = manifest is null ? "Manifest assente o backup non verificato." : $"Formato v{manifest.FormatVersion} · SHA-256 {manifest.Sha256}";
            BackupStatus = verified
                ? $"Integrità SQLite verificata · {SelectedBackup.FilePath}"
                : $"Verifica non superata · {SelectedBackup.FilePath}";
            ReloadBackups();
            SelectedBackup = BackupItems.FirstOrDefault(x => x.FilePath == path);
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
        OpenDistractions.Clear();
        foreach (var item in _data.Distractions.Where(x => x.ResolvedAt is null).OrderByDescending(x => x.CreatedAt))
            OpenDistractions.Add(item);
        RecentEnergyCheckIns.Clear();
        foreach (var item in _data.EnergyCheckIns.OrderByDescending(x => x.RecordedAt).Take(5))
            RecentEnergyCheckIns.Add(item);
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
        CourseProgressRows.Clear();
        var maxFocusMinutes = _progressSnapshot.Courses.Count == 0 ? 0 : _progressSnapshot.Courses.Max(x => x.FocusMinutes);
        foreach (var course in _progressSnapshot.Courses)
        {
            var percentage = maxFocusMinutes == 0 ? 0 : (int)Math.Round(course.FocusMinutes * 100d / maxFocusMinutes);
            CourseProgressRows.Add(new CourseProgressRow(course.CourseName, $"{course.FocusMinutes} min", percentage));
        }
        OnPropertyChanged(nameof(CourseProgressRows));
        OnPropertyChanged(nameof(WeeklyGoalProgressPercent));
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
        foreach (var backup in _backups.ListBackups())
            BackupItems.Add(backup with { IntegrityVerified = _verifiedBackups.Contains(backup.FilePath) });
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
        RevealFlashcardCommand?.NotifyCanExecuteChanged();
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
