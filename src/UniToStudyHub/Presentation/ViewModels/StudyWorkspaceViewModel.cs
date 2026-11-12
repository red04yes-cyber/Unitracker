using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Infrastructure.Data;
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
    private readonly PomodoroService _pomodoro;
    private readonly NotificationCenterService _notificationCenter;
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
    private readonly StudyPlannerService _planner;
    private readonly SyllabusTrackerService _syllabus;
    private readonly QuestionBankService _questionBank;
    private readonly ExamReadinessService _readiness;
    private readonly StudyDataExportService _exports;
    private readonly HashSet<string> _verifiedBackups = new(StringComparer.OrdinalIgnoreCase);
    private StudyData _data = new();
    private Course? _selectedCourse;
    private Course? _selectedFocusCourse;
    private Course? _selectedTaskCourse;
    private string _focusPresetInput = "25/5 · classico";
    private int _focusBreakMinutes = 5;
    private DateTimeOffset? _breakEndsAt;
    private bool _breakCompleted;
    private StudyTaskData? _selectedTask;
    private FlashcardData? _selectedDueFlashcard;
    private ErrorJournalEntryData? _selectedErrorEntry;
    private DistractionData? _selectedDistraction;
    private PdfDocumentData? _selectedPdf;
    private BackupInfo? _selectedBackup;
    private string _plannerAvailabilityInput = string.Empty;
    private DateTime _plannerFromInput = DateTime.Today;
    private DateTime _plannerToInput = DateTime.Today.AddDays(13);
    private string _plannerStatus = "Imposta le tue fasce reali di disponibilità per creare un'anteprima.";
    private StudyPlanPreview? _currentPlanPreview;
    private SyllabusTopicData? _selectedSyllabusTopic;
    private QuestionBankItemData? _selectedQuestion;
    private string _syllabusTitleInput = string.Empty;
    private string _syllabusMinutesInput = "30";
    private string _questionTextInput = string.Empty;
    private string _questionOptionsInput = string.Empty;
    private string _questionCorrectAnswerInput = string.Empty;
    private string _questionAnswerInput = string.Empty;
    private string _questionStatus = "Le risposte aperte richiedono revisione manuale.";
    private string _readinessStatus = "Seleziona una materia per leggere la preparazione.";
    private string _exportStatus = string.Empty;
    private readonly List<QuestionBankItemData> _simulationQuestions = new();
    private int _simulationIndex;
    private int _simulationCorrect;
    private int _simulationAutoMarked;
    private int _simulationManualReview;
    private bool _simulationActive;
    private string _simulationStatus = "Avvia una simulazione per esercitarti con i quesiti della materia selezionata.";
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
    public event Action<StudyData>? DataReloaded;

    public ObservableCollection<StudyTaskData> ActiveTasks { get; } = new();
    public ObservableCollection<FlashcardData> DueFlashcards { get; } = new();
    public ObservableCollection<ErrorJournalEntryData> OpenErrorEntries { get; } = new();
    public ObservableCollection<DistractionData> OpenDistractions { get; } = new();
    public ObservableCollection<EnergyCheckInData> RecentEnergyCheckIns { get; } = new();
    public ObservableCollection<PdfSearchHit> PdfSearchHits { get; } = new();
    public ObservableCollection<BackupInfo> BackupItems { get; } = new();
    public ObservableCollection<CourseProgressRow> CourseProgressRows { get; } = new();
    public ObservableCollection<SyllabusTopicData> SyllabusTopics { get; } = new();
    public ObservableCollection<QuestionBankItemData> QuestionBankItems { get; } = new();
    public ObservableCollection<DailyProgressRow> DailyProgressRows { get; } = new();
    public ObservableCollection<StudyNotificationItem> Notifications { get; } = new();
    public bool NoNotifications => Notifications.Count == 0;
    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<PdfDocumentData> PdfDocuments => _data.PdfDocuments;

    public Course? SelectedCourse
    {
        get => _selectedCourse;
        set
        {
            if (EqualityComparer<Course?>.Default.Equals(_selectedCourse, value)) return;
            Set(ref _selectedCourse, value);
            RefreshSyllabusAndQuestions();
            RefreshReadiness();
            AddSyllabusTopicCommand?.NotifyCanExecuteChanged();
            GenerateAiPlanCommand?.NotifyCanExecuteChanged();
            GenerateOralPracticeCommand?.NotifyCanExecuteChanged();
            AddQuestionCommand?.NotifyCanExecuteChanged();
        }
    }
    public Course? SelectedFocusCourse { get => _selectedFocusCourse; set => Set(ref _selectedFocusCourse, value); }
    public Course? SelectedTaskCourse { get => _selectedTaskCourse; set => Set(ref _selectedTaskCourse, value); }
    public IReadOnlyList<string> FocusPresetOptions { get; } = new[] { "25/5 · classico", "50/10 · lungo", "90/15 · profondo" };
    public string FocusPresetInput
    {
        get => _focusPresetInput;
        set
        {
            Set(ref _focusPresetInput, value);
            var preset = value switch
            {
                "25/5 · classico" => (Work: 25, Break: 5),
                "50/10 · lungo" => (Work: 50, Break: 10),
                "90/15 · profondo" => (Work: 90, Break: 15),
                _ => (Work: 0, Break: 0)
            };
            if (preset.Work > 0)
            {
                _focusBreakMinutes = preset.Break;
                FocusMinutesInput = preset.Work.ToString(CultureInfo.InvariantCulture);
                RefreshBreakSnapshot();
            }
        }
    }
    public SyllabusTopicData? SelectedSyllabusTopic
    {
        get => _selectedSyllabusTopic;
        set { Set(ref _selectedSyllabusTopic, value); CompleteSyllabusTopicCommand?.NotifyCanExecuteChanged(); }
    }
    public QuestionBankItemData? SelectedQuestion
    {
        get => _selectedQuestion;
        set { Set(ref _selectedQuestion, value); EvaluateQuestionCommand?.NotifyCanExecuteChanged(); }
    }
    public string SyllabusTitleInput { get => _syllabusTitleInput; set => Set(ref _syllabusTitleInput, value); }
    public string SyllabusMinutesInput { get => _syllabusMinutesInput; set => Set(ref _syllabusMinutesInput, value); }
    public string QuestionTextInput { get => _questionTextInput; set => Set(ref _questionTextInput, value); }
    public string QuestionOptionsInput { get => _questionOptionsInput; set => Set(ref _questionOptionsInput, value); }
    public string QuestionCorrectAnswerInput { get => _questionCorrectAnswerInput; set => Set(ref _questionCorrectAnswerInput, value); }
    public string QuestionAnswerInput { get => _questionAnswerInput; set => Set(ref _questionAnswerInput, value); }
    public string QuestionStatus { get => _questionStatus; private set => Set(ref _questionStatus, value); }
    public string ReadinessStatus { get => _readinessStatus; private set => Set(ref _readinessStatus, value); }
    public string ExportStatus { get => _exportStatus; private set => Set(ref _exportStatus, value); }
    public string SimulationStatus { get => _simulationStatus; private set => Set(ref _simulationStatus, value); }
    public bool IsSimulationActive { get => _simulationActive; private set { Set(ref _simulationActive, value); OnPropertyChanged(nameof(SimulationQuestionDisplay)); SubmitSimulationAnswerCommand?.NotifyCanExecuteChanged(); } }
    public string SimulationQuestionDisplay => IsSimulationActive && _simulationIndex < _simulationQuestions.Count
        ? $"Quesito {_simulationIndex + 1}/{_simulationQuestions.Count} · {_simulationQuestions[_simulationIndex].Text}"
        : "Nessun quesito attivo.";
    public StudyTaskData? SelectedTask { get => _selectedTask; set { if (ReferenceEquals(_selectedTask, value)) return; _selectedTask = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSelectedTask)); CompleteTaskCommand.NotifyCanExecuteChanged(); RemoveTaskCommand.NotifyCanExecuteChanged(); } }
    public bool HasSelectedTask => SelectedTask is not null;
    public FlashcardData? SelectedDueFlashcard { get => _selectedDueFlashcard; set { Set(ref _selectedDueFlashcard, value); FlashcardRevealed = false; NotifyReviewCommands(); } }
    public ErrorJournalEntryData? SelectedErrorEntry { get => _selectedErrorEntry; set { Set(ref _selectedErrorEntry, value); ReviewErrorNeedsWorkCommand.NotifyCanExecuteChanged(); ReviewErrorRememberedCommand.NotifyCanExecuteChanged(); } }
    public DistractionData? SelectedDistraction { get => _selectedDistraction; set { Set(ref _selectedDistraction, value); ResolveDistractionCommand.NotifyCanExecuteChanged(); } }
    public PdfDocumentData? SelectedPdf { get => _selectedPdf; set { Set(ref _selectedPdf, value); PdfSearchHits.Clear(); OnPropertyChanged(nameof(HasSelectedPdf)); OnPropertyChanged(nameof(NoPdfSearchHits)); SearchPdfCommand.NotifyCanExecuteChanged(); AskPdfCommand.NotifyCanExecuteChanged(); } }
    public BackupInfo? SelectedBackup
    {
        get => _selectedBackup;
        set
        {
            Set(ref _selectedBackup, value);
            OnPropertyChanged(nameof(HasSelectedBackup));
            VerifyBackupCommand.NotifyCanExecuteChanged();
            RestoreBackupCommand.NotifyCanExecuteChanged();
        }
    }
    public bool HasSelectedBackup => SelectedBackup is not null;

    public string FocusTopicInput { get => _focusTopicInput; set => Set(ref _focusTopicInput, value); }
    public string FocusMinutesInput
    {
        get => _focusMinutesInput;
        set
        {
            Set(ref _focusMinutesInput, value);
            var matchingPreset = value switch
            {
                "25" => "25/5 · classico",
                "50" => "50/10 · lungo",
                "90" => "90/15 · profondo",
                _ => string.Empty
            };
            if (_focusPresetInput != matchingPreset)
                Set(ref _focusPresetInput, matchingPreset, nameof(FocusPresetInput));
        }
    }
    public string FocusBreakRemainingDisplay { get; private set; } = "05:00";
    public string FocusBreakStateDisplay { get; private set; } = "Pausa prevista dopo la sessione";
    public int FocusBreakProgressPercent { get; private set; }
    public string FocusRemainingDisplay { get => _focusRemainingDisplay; private set => Set(ref _focusRemainingDisplay, value); }
    public string FocusElapsedDisplay { get => _focusElapsedDisplay; private set => Set(ref _focusElapsedDisplay, value); }
    public string FocusStateDisplay { get => _focusStateDisplay; private set => Set(ref _focusStateDisplay, value); }
    public int FocusProgressPercent { get => _focusProgressPercent; private set => Set(ref _focusProgressPercent, value); }
    public bool HasActiveFocusSession => _pomodoro.GetActive(_data) is not null;
    public bool CanPauseFocus => _pomodoro.GetActive(_data)?.Status == PomodoroTimerStatus.Running;
    public bool CanResumeFocus => _pomodoro.GetActive(_data)?.Status == PomodoroTimerStatus.Paused;
    public bool CanFinishFocus => _pomodoro.GetActive(_data)?.Status == PomodoroTimerStatus.Running;

    public IReadOnlyList<int> TaskPriorityOptions { get; } = new[] { 1, 2, 3, 4, 5 };
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
    public string DatabaseProtectionDisplay
    {
        get
        {
            var status = StudyHubDatabase.DescribeProtection();
            return status.EncryptedAtRest
                ? $"Database cifrato · {status.Algorithm}"
                : status.Recommendation;
        }
    }
    public ProgressSnapshot? CurrentProgress => _progressSnapshot;
    public string PlannerAvailabilityInput { get => _plannerAvailabilityInput; set => Set(ref _plannerAvailabilityInput, value); }
    public DateTime PlannerFromInput { get => _plannerFromInput; set => Set(ref _plannerFromInput, value.Date); }
    public DateTime PlannerToInput { get => _plannerToInput; set => Set(ref _plannerToInput, value.Date); }
    public string PlannerStatus { get => _plannerStatus; private set => Set(ref _plannerStatus, value); }
    public StudyPlanPreview? CurrentPlanPreview { get => _currentPlanPreview; private set { Set(ref _currentPlanPreview, value); OnPropertyChanged(nameof(HasPlanPreview)); OnPropertyChanged(nameof(PlanPreviewSummary)); ApplyStudyPlanCommand?.NotifyCanExecuteChanged(); } }
    public bool HasPlanPreview => CurrentPlanPreview is not null;
    public string PlanPreviewSummary => CurrentPlanPreview is { } plan
        ? $"{plan.ScheduledBlockCount} blocchi · {plan.ScheduledMinutes} minuti · {plan.UnscheduledTasks.Count} task non pianificati"
        : "Nessuna anteprima disponibile.";
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
    public bool NoBackups => BackupItems.Count == 0;
    public bool NoActiveTasks => ActiveTasks.Count == 0;
    public bool NoDueFlashcards => DueFlashcards.Count == 0;
    public bool NoOpenErrorEntries => OpenErrorEntries.Count == 0;
    public bool NoOpenDistractions => OpenDistractions.Count == 0;
    public bool NoPdfDocuments => PdfDocuments.Count == 0;
    public bool NoPdfSearchHits => PdfSearchHits.Count == 0;
    public string PdfDocumentSummary => SelectedPdf is null
        ? "Seleziona un documento."
        : $"{SelectedPdf.PageCount} pagine · {(SelectedPdf.HasExtractableText ? "Testo estraibile" : "OCR richiesto")}";

    public AsyncRelayCommand StartFocusCommand { get; }
    public AsyncRelayCommand PauseFocusCommand { get; }
    public AsyncRelayCommand ResumeFocusCommand { get; }
    public AsyncRelayCommand FinishFocusCommand { get; }
    public AsyncRelayCommand CancelFocusCommand { get; }
    public AsyncRelayCommand StartNextFocusCommand { get; }
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
    public AsyncRelayCommand GenerateAiPlanCommand { get; }
    public AsyncRelayCommand GenerateOralPracticeCommand { get; }
    public AsyncRelayCommand SaveAcademicProfileCommand { get; }
    public AsyncRelayCommand CreateBackupCommand { get; }
    public AsyncRelayCommand VerifyBackupCommand { get; }
    public AsyncRelayCommand RestoreBackupCommand { get; }
    public AsyncRelayCommand PreviewStudyPlanCommand { get; }
    public AsyncRelayCommand ApplyStudyPlanCommand { get; }
    public AsyncRelayCommand AddSyllabusTopicCommand { get; }
    public AsyncRelayCommand CompleteSyllabusTopicCommand { get; }
    public AsyncRelayCommand AddQuestionCommand { get; }
    public AsyncRelayCommand EvaluateQuestionCommand { get; }
    public AsyncRelayCommand StartSimulationCommand { get; }
    public AsyncRelayCommand SubmitSimulationAnswerCommand { get; }

    public StudyWorkspaceViewModel(
        IStudyDataStore store,
        AcademicProfileService academicProfile,
        FocusSessionService focus,
        PomodoroService pomodoro,
        NotificationCenterService notificationCenter,
        StudyTaskService tasks,
        ErrorJournalService errors,
        DistractionService distractions,
        EnergyService energy,
        FlashcardService flashcards,
        ProgressService progress,
        PdfImportService pdfImport,
        PdfIntelligenceService pdfIntelligence,
        AiStudyService aiStudy,
        BackupService backups,
        StudyPlannerService planner,
        SyllabusTrackerService syllabus,
        QuestionBankService questionBank,
        ExamReadinessService readiness,
        StudyDataExportService exports)
    {
        _store = store;
        _academicProfile = academicProfile;
        _focus = focus;
        _pomodoro = pomodoro;
        _notificationCenter = notificationCenter;
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
        _planner = planner;
        _syllabus = syllabus;
        _questionBank = questionBank;
        _readiness = readiness;
        _exports = exports;

        StartFocusCommand = new AsyncRelayCommand(StartFocusAsync, () => _pomodoro.GetActive(_data) is null);
        PauseFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("pause"), () => CanPauseFocus);
        ResumeFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("resume"), () => CanResumeFocus);
        FinishFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("finish"), () => CanFinishFocus);
        CancelFocusCommand = new AsyncRelayCommand(() => ChangeFocusAsync("cancel"), () => _data.PomodoroTimer is not null);
        StartNextFocusCommand = new AsyncRelayCommand(StartNextFocusAsync, () => _data.PomodoroTimer is { Status: PomodoroTimerStatus.Completed, Phase: not PomodoroPhase.Focus });
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
        GenerateAiPlanCommand = new AsyncRelayCommand(GenerateAiPlanAsync, () => SelectedCourse is not null);
        GenerateOralPracticeCommand = new AsyncRelayCommand(GenerateOralPracticeAsync, () => SelectedCourse is not null);
        SaveAcademicProfileCommand = new AsyncRelayCommand(SaveAcademicProfileAsync);
        CreateBackupCommand = new AsyncRelayCommand(CreateBackupAsync);
        VerifyBackupCommand = new AsyncRelayCommand(VerifyBackupAsync, () => SelectedBackup is not null);
        RestoreBackupCommand = new AsyncRelayCommand(RestoreBackupAsync, () => SelectedBackup is not null);
        PreviewStudyPlanCommand = new AsyncRelayCommand(PreviewStudyPlanAsync);
        ApplyStudyPlanCommand = new AsyncRelayCommand(ApplyStudyPlanAsync, () => HasPlanPreview);
        AddSyllabusTopicCommand = new AsyncRelayCommand(AddSyllabusTopicAsync, () => SelectedCourse is not null);
        CompleteSyllabusTopicCommand = new AsyncRelayCommand(CompleteSyllabusTopicAsync, () => SelectedSyllabusTopic is not null);
        AddQuestionCommand = new AsyncRelayCommand(AddQuestionAsync, () => SelectedCourse is not null);
        EvaluateQuestionCommand = new AsyncRelayCommand(EvaluateQuestionAsync, () => SelectedQuestion is not null);
        StartSimulationCommand = new AsyncRelayCommand(StartSimulationAsync, () => QuestionBankItems.Count > 0);
        SubmitSimulationAnswerCommand = new AsyncRelayCommand(SubmitSimulationAnswerAsync, () => IsSimulationActive);
    }

    public void Initialize(StudyData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (SelectedFocusCourse is null || !_data.Courses.Contains(SelectedFocusCourse)) SelectedFocusCourse = _data.Courses.FirstOrDefault();
        if (SelectedTaskCourse is null || !_data.Courses.Contains(SelectedTaskCourse)) SelectedTaskCourse = _data.Courses.FirstOrDefault();
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
        var timer = _data.PomodoroTimer;
        if (timer is null)
        {
            FocusStateDisplay = "Pronto";
            FocusRemainingDisplay = "—";
            FocusElapsedDisplay = "00:00";
            FocusProgressPercent = 0;
            FocusBreakRemainingDisplay = FormatClock((_data.PomodoroSettings?.ShortBreakMinutes ?? 5) * 60);
            FocusBreakStateDisplay = "Pausa prevista dopo il focus";
            FocusBreakProgressPercent = 0;
        }
        else
        {
            var snapshot = _pomodoro.Snapshot(_data);
            var duration = timer.Phase switch
            {
                PomodoroPhase.Focus => _data.PomodoroSettings.FocusMinutes,
                PomodoroPhase.ShortBreak => _data.PomodoroSettings.ShortBreakMinutes,
                PomodoroPhase.LongBreak => _data.PomodoroSettings.LongBreakMinutes,
                _ => _data.PomodoroSettings.FocusMinutes
            };
            FocusRemainingDisplay = FormatClock(snapshot.RemainingSeconds);
            FocusElapsedDisplay = FormatClock(snapshot.ElapsedSeconds);
            FocusProgressPercent = duration <= 0 ? 0 :
                Math.Clamp((int)Math.Round(snapshot.ElapsedSeconds * 100d / (duration * 60d)), 0, 100);
            FocusStateDisplay = timer.Status switch
            {
                PomodoroTimerStatus.Paused => timer.Phase == PomodoroPhase.Focus ? "Focus in pausa" : "Pausa in sospeso",
                PomodoroTimerStatus.Completed => "Pausa completata · pronto per il prossimo focus",
                _ when timer.Phase == PomodoroPhase.Focus => "Focus in corso",
                _ when timer.Phase == PomodoroPhase.ShortBreak => "Pausa breve",
                _ => "Pausa lunga"
            };
            var isBreak = timer.Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak;
            FocusBreakStateDisplay = isBreak
                ? timer.Phase == PomodoroPhase.LongBreak ? "Pausa lunga in corso" : "Pausa breve in corso"
                : $"Prossima pausa · {_data.PomodoroSettings.ShortBreakMinutes} min";
            FocusBreakRemainingDisplay = isBreak ? FormatClock(snapshot.RemainingSeconds) :
                timer.Status == PomodoroTimerStatus.Completed ? "00:00" :
                FormatClock(_data.PomodoroSettings.ShortBreakMinutes * 60);
            FocusBreakProgressPercent = isBreak ? FocusProgressPercent :
                timer.Status == PomodoroTimerStatus.Completed ? 100 : 0;
        }
        OnPropertyChanged(nameof(FocusBreakRemainingDisplay));
        OnPropertyChanged(nameof(FocusBreakStateDisplay));
        OnPropertyChanged(nameof(FocusBreakProgressPercent));
        RefreshNotifications();
        NotifyFocusState();
    }

    public async Task RefreshFocusSnapshotAsync()
    {
        try
        {
            var reconciled = _pomodoro.Reconcile(_data);
            if (reconciled.HasTransitions)
            {
                foreach (var transition in reconciled.Transitions)
                {
                    var title = transition.Phase == PomodoroPhase.Focus
                        ? "Focus completato"
                        : transition.Phase == PomodoroPhase.ShortBreak ? "Pausa breve terminata" : "Pausa lunga terminata";
                    Notifications.Insert(0, new StudyNotificationItem(
                        $"pomodoro:{transition.Phase}:{transition.At:O}", title,
                        transition.Phase == PomodoroPhase.Focus
                            ? "Hai completato un blocco di concentrazione. È iniziata la pausa."
                            : "La pausa è terminata. Puoi avviare il prossimo focus quando sei pronto.",
                        transition.At, "Pomodoro"));
                }
                await SaveAsync();
            }
            RefreshFocusSnapshot();
        }
        catch (Exception ex)
        {
            WorkspaceStatus = $"Aggiornamento timer non riuscito: {ex.Message}";
        }
    }

    private void RefreshNotifications()
    {
        var retainedPomodoro = Notifications.Where(x => x.Kind == "Pomodoro");
        var desired = retainedPomodoro
            .Concat(_notificationCenter.GetUpcoming(_data))
            .OrderBy(x => x.OccursAt)
            .ToArray();
        if (!Notifications.SequenceEqual(desired))
        {
            Notifications.Clear();
            foreach (var item in desired)
                Notifications.Add(item);
        }
        OnPropertyChanged(nameof(NoNotifications));
    }

    private void RefreshBreakSnapshot()
    {
        if (_focus.GetActive(_data) is not null)
        {
            FocusBreakRemainingDisplay = FormatClock(_focusBreakMinutes * 60);
            FocusBreakStateDisplay = $"Pausa prevista · {_focusBreakMinutes} min";
            FocusBreakProgressPercent = 0;
            OnPropertyChanged(nameof(FocusBreakRemainingDisplay));
            OnPropertyChanged(nameof(FocusBreakStateDisplay));
            OnPropertyChanged(nameof(FocusBreakProgressPercent));
            return;
        }

        if (_breakEndsAt is DateTimeOffset endsAt)
        {
            var totalSeconds = _focusBreakMinutes * 60;
            var remainingSeconds = Math.Max(0, (int)Math.Ceiling((endsAt - DateTimeOffset.Now).TotalSeconds));
            if (remainingSeconds == 0)
            {
                _breakEndsAt = null;
                _breakCompleted = true;
            }
            else
            {
                FocusBreakRemainingDisplay = FormatClock(remainingSeconds);
                FocusBreakStateDisplay = "Pausa in corso";
                FocusBreakProgressPercent = Math.Clamp((int)Math.Round((totalSeconds - remainingSeconds) * 100d / totalSeconds), 0, 100);
                OnPropertyChanged(nameof(FocusBreakRemainingDisplay));
                OnPropertyChanged(nameof(FocusBreakStateDisplay));
                OnPropertyChanged(nameof(FocusBreakProgressPercent));
                return;
            }
        }

        FocusBreakRemainingDisplay = _breakCompleted ? "00:00" : FormatClock(_focusBreakMinutes * 60);
        FocusBreakStateDisplay = _breakCompleted ? "Pausa completata" : "Pausa prevista dopo la sessione";
        FocusBreakProgressPercent = _breakCompleted ? 100 : 0;
        OnPropertyChanged(nameof(FocusBreakRemainingDisplay));
        OnPropertyChanged(nameof(FocusBreakStateDisplay));
        OnPropertyChanged(nameof(FocusBreakProgressPercent));
    }

    public async Task ImportPdfAsync(string path)
    {
        try
        {
            var result = await _pdfImport.ImportAsync(path, _data, SelectedCourse?.Id);
            await _store.SaveAsync(_data);
            SelectedPdf = result.Document;
            OnPropertyChanged(nameof(NoPdfDocuments));
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
            _breakEndsAt = null;
            _breakCompleted = false;
            _pomodoro.UpdateSettings(_data, minutes, _focusBreakMinutes, Math.Max(10, _focusBreakMinutes * 3), 4);
            _pomodoro.Start(_data, SelectedFocusCourse?.Id, FocusTopicInput);
            await SaveAsync();
            FocusTopicInput = string.Empty;
            RefreshFocusSnapshot();
        }
        catch (Exception ex) { WorkspaceStatus = $"Sessione non avviata: {ex.Message}"; }
    }

    private async Task ChangeFocusAsync(string action)
    {
        if (_data.PomodoroTimer is null) return;
        try
        {
            switch (action)
            {
                case "pause": _pomodoro.Pause(_data); break;
                case "resume": _pomodoro.Resume(_data); break;
                case "finish": _pomodoro.CompletePhase(_data); break;
                case "cancel": _pomodoro.Reset(_data); break;
            }
            await SaveAsync();
            RefreshFocusSnapshot();
            RefreshProgress();
            RefreshCollections();
        }
        catch (Exception ex) { WorkspaceStatus = $"Sessione non aggiornata: {ex.Message}"; }
    }

    private async Task StartNextFocusAsync()
    {
        try
        {
            _pomodoro.StartNextFocus(_data);
            await SaveAsync();
            RefreshFocusSnapshot();
            WorkspaceStatus = "Nuovo blocco di concentrazione avviato.";
        }
        catch (Exception ex) { WorkspaceStatus = $"Impossibile avviare il prossimo focus: {ex.Message}"; }
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
                CourseId = SelectedTaskCourse?.Id ?? string.Empty
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
        OnPropertyChanged(nameof(NoPdfSearchHits));
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

    private async Task GenerateAiPlanAsync()
    {
        if (SelectedCourse is null) return;
        var course = SelectedCourse;
        var topics = _data.SyllabusTopics.Where(x => x.CourseId == course.Id).ToList();
        var tasks = _data.StudyTasks.Where(x => x.CourseId == course.Id &&
            x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress).ToList();
        var context = $"""
            Corso: {course.Name} ({course.Code})
            Data appello registrata: {(course.AppealDate is null ? "non indicata" : course.AppealDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))}
            Preferenze di studio: {_data.StudyPreferences}
            Ore settimanali disponibili dichiarate: {_data.WeeklyStudyHours}
            Limite giornaliero: {_data.MaxDailyStudyMinutes} minuti
            Syllabus:
            {string.Join("\n", topics.Select(x => $"- {x.Title} | stato: {x.Status} | stima: {x.EstimatedMinutes} min"))}
            Attività aperte:
            {string.Join("\n", tasks.Select(x => $"- {x.Title} | priorità: {x.Priority}/5 | stima: {x.EstimatedMinutes} min | scadenza: {(x.DueAt is null ? "non indicata" : x.DueAt.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))}"))}
            """;
        try
        {
            var response = await _aiStudy.AskAsync(
                "Crea un piano di studio concreto per le prossime due settimane. Ordina gli argomenti per priorità, distribuisci il carico entro i limiti dichiarati, proponi sessioni e obiettivi verificabili. Non creare scadenze non presenti nei dati.",
                context, maxTokens: 2200);
            AiStatusMessage = response.Status switch
            {
                AiStatus.NotConfigured => "AI non configurata: scegli OpenAI o Ollama e autorizza esplicitamente l'invio.",
                AiStatus.Configured => $"Piano proposto da {response.Provider}. È una proposta: controlla date e carico prima di applicarla.",
                AiStatus.Refused => response.Error ?? "Richiesta non autorizzata o limite giornaliero raggiunto.",
                _ => response.Error ?? "Errore durante la generazione del piano."
            };
            AiResponseText = response.Status == AiStatus.Configured ? response.Content : string.Empty;
        }
        catch (Exception ex)
        {
            AiStatusMessage = $"Piano AI non generato: {ex.Message}";
            AiResponseText = string.Empty;
        }
    }

    private async Task GenerateOralPracticeAsync()
    {
        if (SelectedCourse is null) return;
        var course = SelectedCourse;
        var topics = _data.SyllabusTopics.Where(x => x.CourseId == course.Id).ToList();
        var documents = _data.PdfDocuments
            .Where(x => x.CourseId == course.Id)
            .Select(x => x.FileName)
            .ToList();
        var context = $"""
            Corso: {course.Name} ({course.Code})
            Syllabus:
            {string.Join("\n", topics.Select(x => $"- {x.Title} | {x.Description} | stato: {x.Status}"))}
            Materiali PDF importati (nomi, non contenuto):
            {string.Join("\n", documents.Select(x => $"- {x}"))}
            Banca domande esistente:
            {string.Join("\n", _data.Questions.Where(x => x.CourseId == course.Id).Take(20).Select(x => $"- {x.Text}"))}
            """;
        try
        {
            var response = await _aiStudy.AskAsync(
                "Prepara una simulazione orale con cinque domande progressive sugli argomenti del syllabus. Per ogni domanda indica cosa dovrebbe contenere una risposta completa e una breve griglia di valutazione. Non presentare le domande come ufficiali e non inventare contenuti assenti dal syllabus.",
                context, maxTokens: 2200);
            AiStatusMessage = response.Status switch
            {
                AiStatus.NotConfigured => "AI non configurata: scegli OpenAI o Ollama e autorizza esplicitamente l'invio.",
                AiStatus.Configured => $"Simulazione generata da {response.Provider}. Le domande sono esercizi, non quesiti ufficiali.",
                AiStatus.Refused => response.Error ?? "Richiesta non autorizzata o limite giornaliero raggiunto.",
                _ => response.Error ?? "Errore durante la simulazione orale."
            };
            AiResponseText = response.Status == AiStatus.Configured ? response.Content : string.Empty;
        }
        catch (Exception ex)
        {
            AiStatusMessage = $"Simulazione non generata: {ex.Message}";
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

    private async Task PreviewStudyPlanAsync()
    {
        try
        {
            if (PlannerToInput < PlannerFromInput)
                throw new ArgumentException("La data finale deve essere successiva o uguale alla data iniziale.");

            var availability = ParseDailyAvailability(PlannerAvailabilityInput);
            CurrentPlanPreview = _planner.CreatePreview(_data, new StudyPlannerOptions
            {
                From = DateOnly.FromDateTime(PlannerFromInput),
                To = DateOnly.FromDateTime(PlannerToInput),
                TimeZoneId = TimeZoneInfo.Local.Id,
                WeeklyAvailability = availability,
                WeeklyStudyMinutes = _data.WeeklyStudyHours * 60,
                MaxDailyStudyMinutes = _data.MaxDailyStudyMinutes,
                PreferredBlockMinutes = 50,
                MinimumBlockMinutes = 15,
                BreakMinutes = 10,
                MaxBlocksPerDay = 4,
                ReplaceExistingGeneratedBlocks = true
            });
            PlannerStatus = $"Anteprima pronta: {PlanPreviewSummary}. I blocchi non sono ancora nel calendario.";
        }
        catch (Exception ex)
        {
            CurrentPlanPreview = null;
            PlannerStatus = $"Anteprima non disponibile: {ex.Message}";
        }
    }

    private async Task ApplyStudyPlanAsync()
    {
        if (CurrentPlanPreview is null) return;
        try
        {
            var result = await _planner.ApplyAndSaveAsync(_data, CurrentPlanPreview, _store);
            RefreshCollections();
            RefreshProgress();
            DataReloaded?.Invoke(_data);
            PlannerStatus = $"Piano salvato nel calendario: {result.Added} blocchi aggiunti, {result.Conflicts} conflitti, {result.Skipped} ignorati.";
            CurrentPlanPreview = null;
        }
        catch (Exception ex)
        {
            PlannerStatus = $"Piano non salvato: {ex.Message}";
        }
    }

    private async Task AddSyllabusTopicAsync()
    {
        if (SelectedCourse is null) return;
        if (!int.TryParse(SyllabusMinutesInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var minutes) || minutes < 0)
        {
            WorkspaceStatus = "Inserisci una stima in minuti valida per l'argomento.";
            return;
        }

        SyllabusTopicData? created = null;
        try
        {
            created = _syllabus.Add(_data, SelectedCourse.Id, SyllabusTitleInput, estimatedMinutes: minutes);
            await SaveAsync();
            SyllabusTitleInput = string.Empty;
            RefreshSyllabusAndQuestions();
            SelectedSyllabusTopic = SyllabusTopics.FirstOrDefault(x => x.Id == created.Id);
            RefreshReadiness();
            WorkspaceStatus = "Argomento aggiunto al syllabus.";
        }
        catch (Exception ex)
        {
            if (created is not null) _data.SyllabusTopics.Remove(created);
            WorkspaceStatus = $"Argomento non salvato: {ex.Message}";
        }
    }

    private async Task CompleteSyllabusTopicAsync()
    {
        if (SelectedSyllabusTopic is null) return;
        var topic = SelectedSyllabusTopic;
        var previousStatus = topic.Status;
        var previousStudiedAt = topic.LastStudiedAt;
        try
        {
            _syllabus.UpdateStatus(_data, topic.Id, SyllabusTopicStatus.Completed);
            await SaveAsync();
            RefreshSyllabusAndQuestions();
            SelectedSyllabusTopic = SyllabusTopics.FirstOrDefault(x => x.Id == topic.Id);
            RefreshReadiness();
            WorkspaceStatus = "Argomento contrassegnato come completato.";
        }
        catch (Exception ex)
        {
            topic.Status = previousStatus;
            topic.LastStudiedAt = previousStudiedAt;
            WorkspaceStatus = $"Aggiornamento syllabus non salvato: {ex.Message}";
        }
    }

    private async Task AddQuestionAsync()
    {
        if (SelectedCourse is null) return;
        var options = QuestionOptionsInput.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var question = new QuestionBankItemData
        {
            CourseId = SelectedCourse.Id,
            SyllabusTopicId = SelectedSyllabusTopic?.CourseId == SelectedCourse.Id ? SelectedSyllabusTopic.Id : string.Empty,
            Text = QuestionTextInput,
            Type = options.Length >= 2 ? QuestionType.Closed : QuestionType.Open,
            Options = new ObservableCollection<string>(options),
            CorrectAnswer = QuestionCorrectAnswerInput.Trim(),
            Source = "Manuale"
        };
        var added = false;
        try
        {
            _questionBank.Add(_data, question);
            added = true;
            await SaveAsync();
            QuestionTextInput = string.Empty;
            QuestionOptionsInput = string.Empty;
            QuestionCorrectAnswerInput = string.Empty;
            RefreshSyllabusAndQuestions();
            SelectedQuestion = QuestionBankItems.FirstOrDefault(x => x.Id == question.Id);
            QuestionStatus = question.Type == QuestionType.Closed
                ? "Quesito a scelta chiusa salvato. Puoi provarlo e verificare la risposta."
                : "Quesito aperto salvato. La correzione richiede revisione manuale.";
        }
        catch (Exception ex)
        {
            if (added) _data.Questions.Remove(question);
            QuestionStatus = $"Quesito non salvato: {ex.Message}";
        }
    }

    private Task EvaluateQuestionAsync()
    {
        if (SelectedQuestion is null) return Task.CompletedTask;
        var evaluation = _questionBank.Evaluate(SelectedQuestion, QuestionAnswerInput);
        QuestionStatus = evaluation.RequiresManualReview
            ? evaluation.Feedback
            : evaluation.IsCorrect == true
                ? "Risposta corretta."
                : "Risposta da correggere. " + evaluation.Feedback;
        return Task.CompletedTask;
    }

    private Task StartSimulationAsync()
    {
        _simulationQuestions.Clear();
        var random = Random.Shared;
        _simulationQuestions.AddRange(QuestionBankItems.OrderBy(_ => random.Next()).Take(10));
        _simulationIndex = 0;
        _simulationCorrect = 0;
        _simulationAutoMarked = 0;
        _simulationManualReview = 0;
        QuestionAnswerInput = string.Empty;
        IsSimulationActive = _simulationQuestions.Count > 0;
        SimulationStatus = IsSimulationActive
            ? $"Simulazione avviata: {_simulationQuestions.Count} quesiti. Le risposte aperte sono segnalate per revisione."
            : "Non ci sono domande per questa materia.";
        OnPropertyChanged(nameof(SimulationQuestionDisplay));
        return Task.CompletedTask;
    }

    private Task SubmitSimulationAnswerAsync()
    {
        if (!IsSimulationActive || _simulationIndex >= _simulationQuestions.Count)
            return Task.CompletedTask;
        var question = _simulationQuestions[_simulationIndex];
        var evaluation = _questionBank.Evaluate(question, QuestionAnswerInput);
        if (evaluation.RequiresManualReview)
            _simulationManualReview++;
        else
        {
            _simulationAutoMarked++;
            if (evaluation.IsCorrect == true) _simulationCorrect++;
        }

        _simulationIndex++;
        QuestionAnswerInput = string.Empty;
        if (_simulationIndex >= _simulationQuestions.Count)
        {
            IsSimulationActive = false;
            SimulationStatus = $"Simulazione conclusa: {_simulationCorrect}/{_simulationAutoMarked} corrette tra le risposte chiuse; {_simulationManualReview} aperte da rivedere manualmente. Il tentativo non viene registrato come esame ufficiale.";
        }
        else
        {
            SimulationStatus = $"Risposta acquisita. Restano {_simulationQuestions.Count - _simulationIndex} quesiti.";
            OnPropertyChanged(nameof(SimulationQuestionDisplay));
        }
        return Task.CompletedTask;
    }

    private void RefreshSyllabusAndQuestions()
    {
        SyllabusTopics.Clear();
        QuestionBankItems.Clear();
        if (SelectedCourse is not null)
        {
            foreach (var item in _syllabus.GetTopics(_data, SelectedCourse.Id))
                SyllabusTopics.Add(item);
            foreach (var item in _questionBank.GetForCourse(_data, SelectedCourse.Id))
                QuestionBankItems.Add(item);
        }
        if (SelectedSyllabusTopic is null || !SyllabusTopics.Any(x => x.Id == SelectedSyllabusTopic.Id))
            SelectedSyllabusTopic = SyllabusTopics.FirstOrDefault();
        if (SelectedQuestion is null || !QuestionBankItems.Any(x => x.Id == SelectedQuestion.Id))
            SelectedQuestion = QuestionBankItems.FirstOrDefault();
        OnPropertyChanged(nameof(SyllabusTopics));
        OnPropertyChanged(nameof(QuestionBankItems));
        AddSyllabusTopicCommand?.NotifyCanExecuteChanged();
        AddQuestionCommand?.NotifyCanExecuteChanged();
        CompleteSyllabusTopicCommand?.NotifyCanExecuteChanged();
        EvaluateQuestionCommand?.NotifyCanExecuteChanged();
        StartSimulationCommand?.NotifyCanExecuteChanged();
    }

    private void RefreshReadiness()
    {
        if (SelectedCourse is null)
        {
            ReadinessStatus = "Seleziona una materia per leggere la preparazione.";
            return;
        }

        var result = _readiness.Calculate(_data, SelectedCourse.Id);
        var score = result.Score is null ? "non disponibile" : $"{result.Score:0.##}%";
        var missing = result.MissingData.Count == 0 ? "Nessun dato mancante." : string.Join(" · ", result.MissingData);
        ReadinessStatus = $"{SelectedCourse.Name}: {score} · {result.Coverage.CompletedTopics}/{result.Coverage.TotalTopics} argomenti · {missing}";
    }

    private static IReadOnlyList<StudyAvailabilityWindow> ParseDailyAvailability(string input)
    {
        var intervals = new List<(TimeOnly Start, TimeOnly End)>();
        foreach (var part in (input ?? string.Empty).Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split('-', StringSplitOptions.TrimEntries);
            if (pieces.Length != 2 ||
                !TimeOnly.TryParseExact(pieces[0], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) ||
                !TimeOnly.TryParseExact(pieces[1], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) ||
                end <= start)
                throw new ArgumentException("Fascia non valida. Usa HH:mm-HH:mm, ad esempio 09:00-12:00;15:00-18:00.");
            intervals.Add((start, end));
        }
        if (intervals.Count == 0)
            throw new ArgumentException("Inserisci almeno una fascia giornaliera reale (HH:mm-HH:mm).");

        var result = new List<StudyAvailabilityWindow>();
        foreach (var day in Enum.GetValues<DayOfWeek>())
            foreach (var interval in intervals)
                result.Add(new StudyAvailabilityWindow(day, interval.Start, interval.End));
        return result;
    }

    private async Task RestoreBackupAsync()
    {
        if (SelectedBackup is null) return;
        var path = SelectedBackup.FilePath;
        try
        {
            // BackupService verifies/decrypts the source and creates a safety
            // backup before replacing the live SQLite file.
            await _backups.RestoreAsync(path);
            var restored = await _store.LoadAsync();
            Initialize(restored);
            DataReloaded?.Invoke(restored);
            SelectedBackup = BackupItems.FirstOrDefault(x =>
                string.Equals(x.FilePath, path, StringComparison.OrdinalIgnoreCase));
            BackupStatus = "Ripristino completato. Database ricaricato; backup di sicurezza creato prima della sostituzione.";
        }
        catch (Exception ex)
        {
            BackupStatus = $"Ripristino non riuscito: {ex.Message}";
        }
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
        OnPropertyChanged(nameof(NoActiveTasks));
        OnPropertyChanged(nameof(NoDueFlashcards));
        OnPropertyChanged(nameof(NoOpenErrorEntries));
        OnPropertyChanged(nameof(NoOpenDistractions));
        OnPropertyChanged(nameof(NoPdfDocuments));
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
        DailyProgressRows.Clear();
        var dailyTotals = new List<(DateTime Date, int Minutes)>();
        for (var offset = 6; offset >= 0; offset--)
        {
            var date = DateTime.Today.AddDays(-offset).Date;
            var seconds = _data.StudySessions
                .Where(x => x.StartedAt.LocalDateTime.Date == date && x.State != StudySessionState.Cancelled)
                .Sum(x => Math.Max(0, x.AccumulatedFocusSeconds));
            dailyTotals.Add((date, (int)Math.Round(seconds / 60d)));
        }
        var maxDaily = dailyTotals.Count == 0 ? 0 : dailyTotals.Max(x => x.Minutes);
        foreach (var total in dailyTotals)
            DailyProgressRows.Add(new DailyProgressRow(
                total.Date.ToString("ddd dd/MM", CultureInfo.GetCultureInfo("it-IT")),
                total.Minutes,
                maxDaily == 0 ? 0 : (int)Math.Round(total.Minutes * 100d / maxDaily)));
        OnPropertyChanged(nameof(DailyProgressRows));
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

    public async Task ExportProgressAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Seleziona un percorso di destinazione.", nameof(filePath));
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var from = today.AddDays(-6);
            var bundle = _exports.Export(
                _data,
                new StudyExportRequest(from, today, TimeZoneInfo.Local.Id, IncludePrivateNotes: false),
                StudyExportFormat.Xlsx);
            var file = bundle.Files.Single();
            await File.WriteAllBytesAsync(filePath, file.Content, cancellationToken);
            ExportStatus = $"Esportazione completata: {filePath}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Esportazione non riuscita: {ex.Message}";
            throw;
        }
    }

    private void ReloadBackups()
    {
        BackupItems.Clear();
        foreach (var backup in _backups.ListBackups())
            BackupItems.Add(backup with { IntegrityVerified = _verifiedBackups.Contains(backup.FilePath) });
        OnPropertyChanged(nameof(HasBackups));
        OnPropertyChanged(nameof(NoBackups));
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
        StartNextFocusCommand?.NotifyCanExecuteChanged();
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

public sealed record DailyProgressRow(string DayLabel, int FocusMinutes, int FocusPercent);

