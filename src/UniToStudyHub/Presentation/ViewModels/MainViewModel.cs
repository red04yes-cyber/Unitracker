using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Models;
using UniToStudyHub.Presentation.Commands;
using UniToStudyHub.Services;

namespace UniToStudyHub.Presentation.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IStudyDataStore _store;
    private readonly MoodleApplicationService _moodle;
    private readonly CareerCalculatorService _calculator;
    private readonly StudyProductivityService _productivity;
    private StudyData _data = new();
    private Course? _selectedGradeCourse;
    private string _courseNameInput = string.Empty;
    private string _courseCfuInput = "6";
    private string _gradeValueInput = "28";
    private string _moodleUrlInput = string.Empty;
    private string _moodleToken = string.Empty;
    private string _moodleStatus = "Non collegato";
    private string _averageDisplay = "—";
    private string _arithmeticAverageDisplay = "—";
    private string _baseGradeDisplay = "Voto di partenza: —";
    private string _creditsDisplay = "CFU completati: 0";
    private string _statusMessage = string.Empty;
    private string _courseSearchText = string.Empty;
    private string _courseFilter = "active";
    private Course? _selectedProductivityCourse;
    private string _dailyGoalInput = "60";
    private string _weeklyGoalInput = "600";
    private string _focusMinutesInput = "30";
    private string _noteTopicInput = string.Empty;
    private string _noteContentInput = string.Empty;
    private string _attendanceTitleInput = string.Empty;
    private string _attendanceDurationInput = "60";
    private string _attendanceDateInput = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    private string _attendanceNotesInput = string.Empty;
    private string _productivitySummary = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<Grade> Grades => _data.Grades;
    public ObservableCollection<Exam> Exams => _data.Exams;
    public ObservableCollection<Announcement> Announcements => _data.Announcements;
    public ObservableCollection<Course> VisibleCourses { get; } = new();
    public ObservableCollection<QuickStudyNoteData> QuickNotes { get; } = new();
    public ObservableCollection<LectureAttendanceData> AttendanceRecords { get; } = new();
    public Course? SelectedProductivityCourse { get => _selectedProductivityCourse; set => Set(ref _selectedProductivityCourse, value); }
    public string DailyGoalInput { get => _dailyGoalInput; set => Set(ref _dailyGoalInput, value); }
    public string WeeklyGoalInput { get => _weeklyGoalInput; set => Set(ref _weeklyGoalInput, value); }
    public string FocusMinutesInput { get => _focusMinutesInput; set => Set(ref _focusMinutesInput, value); }
    public string NoteTopicInput { get => _noteTopicInput; set => Set(ref _noteTopicInput, value); }
    public string NoteContentInput { get => _noteContentInput; set => Set(ref _noteContentInput, value); }
    public string AttendanceTitleInput { get => _attendanceTitleInput; set => Set(ref _attendanceTitleInput, value); }
    public string AttendanceDurationInput { get => _attendanceDurationInput; set => Set(ref _attendanceDurationInput, value); }
    public string AttendanceDateInput { get => _attendanceDateInput; set => Set(ref _attendanceDateInput, value); }
    public string AttendanceNotesInput { get => _attendanceNotesInput; set => Set(ref _attendanceNotesInput, value); }
    public string ProductivitySummary { get => _productivitySummary; private set => Set(ref _productivitySummary, value); }
    public string CurrentStreakDisplay => _productivity.GetStreak(_data, DateOnly.FromDateTime(DateTime.Today)).CurrentStreakDays.ToString(CultureInfo.CurrentCulture);
    public string WeeklyFocusDisplay
    {
        get
        {
            var snapshot = _productivity.GetStreak(_data, DateOnly.FromDateTime(DateTime.Today));
            return $"{snapshot.MinutesThisWeek} / {snapshot.WeeklyGoalMinutes} min";
        }
    }

    public string CourseNameInput { get => _courseNameInput; set => Set(ref _courseNameInput, value); }
    public string CourseCfuInput { get => _courseCfuInput; set => Set(ref _courseCfuInput, value); }
    public string GradeValueInput { get => _gradeValueInput; set => Set(ref _gradeValueInput, value); }
    public Course? SelectedGradeCourse { get => _selectedGradeCourse; set => Set(ref _selectedGradeCourse, value); }
    public string MoodleUrlInput { get => _moodleUrlInput; set => Set(ref _moodleUrlInput, value); }
    public string MoodleToken { get => _moodleToken; set => Set(ref _moodleToken, value); }
    public string MoodleStatus { get => _moodleStatus; private set => Set(ref _moodleStatus, value); }
    public string AverageDisplay { get => _averageDisplay; private set => Set(ref _averageDisplay, value); }
    public string ArithmeticAverageDisplay { get => _arithmeticAverageDisplay; private set => Set(ref _arithmeticAverageDisplay, value); }
    public string ProjectedBaseGradeValue => GradeCalculator.Format(GradeCalculator.ProjectedBase(_data.Grades, _calculator));
    public string BaseGradeDisplay { get => _baseGradeDisplay; private set => Set(ref _baseGradeDisplay, value); }
    public string CreditsDisplay { get => _creditsDisplay; private set => Set(ref _creditsDisplay, value); }
    public string StatusMessage { get => _statusMessage; private set => Set(ref _statusMessage, value); }

    public string CourseSearchText
    {
        get => _courseSearchText;
        set
        {
            if (string.Equals(_courseSearchText, value, StringComparison.Ordinal)) return;
            _courseSearchText = value;
            OnPropertyChanged();
            RefreshVisibleCourses();
        }
    }

    public string CourseFilter
    {
        get => _courseFilter;
        set
        {
            if (string.Equals(_courseFilter, value, StringComparison.Ordinal)) return;
            _courseFilter = value;
            OnPropertyChanged();
            RefreshVisibleCourses();
        }
    }

    public string AcquiredCreditsValue => GradeCalculator.AcquiredCfu(_data.Grades, _calculator).ToString(CultureInfo.CurrentCulture);
    public string ActiveCoursesCount => Courses.Count(x => !x.Completed).ToString(CultureInfo.CurrentCulture);
    public string ArchivedCoursesCount => Courses.Count(x => x.Completed).ToString(CultureInfo.CurrentCulture);
    public string AllCoursesCount => Courses.Count.ToString(CultureInfo.CurrentCulture);
    public bool NoVisibleCourses => VisibleCourses.Count == 0;
    public bool NoPlannedExams => Exams.Count == 0;

    public string CourseEmptyMessage
    {
        get
        {
            if (Courses.Count == 0) return "Non hai ancora aggiunto materie. Inizia inserendo il tuo primo insegnamento.";
            if (!string.IsNullOrWhiteSpace(CourseSearchText)) return "Nessuna materia corrisponde alla ricerca.";
            return CourseFilter switch
            {
                "archive" => "Non ci sono ancora materie concluse nell'archivio.",
                "all" => "Non ci sono materie da mostrare.",
                _ => "Non ci sono materie attive. Puoi controllare l'archivio o aggiungerne una."
            };
        }
    }

    public string NextExamNameDisplay
    {
        get
        {
            var next = GetNextPlannedExam();
            return next?.Name ?? "Nessun esame pianificato";
        }
    }

    public string NextExamDateDisplay
    {
        get
        {
            var next = GetNextPlannedExam();
            return next?.PlannedDate?.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"))
                   ?? "Le date appariranno qui quando pianificate.";
        }
    }

    public string LatestGradeDisplay
    {
        get
        {
            var latest = Grades.OrderByDescending(x => x.Date).FirstOrDefault();
            return latest is null
                ? "Non hai ancora registrato voti."
                : $"{latest.CourseName}: {latest.Value}{(latest.Honors ? " e lode" : string.Empty)} · {latest.Date.ToString("d", CultureInfo.GetCultureInfo("it-IT"))}";
        }
    }

    public AsyncRelayCommand AddCourseCommand { get; }
    public AsyncRelayCommand AddGradeCommand { get; }
    public AsyncRelayCommand PlanExamsCommand { get; }
    public AsyncRelayCommand ConnectMoodleCommand { get; }
    public AsyncRelayCommand SyncMoodleCommand { get; }
    public AsyncRelayCommand SaveStudyGoalsCommand { get; }
    public AsyncRelayCommand RecordFocusCommand { get; }
    public AsyncRelayCommand SaveQuickNoteCommand { get; }
    public AsyncRelayCommand RecordAttendanceCommand { get; }

    public MainViewModel(IStudyDataStore store, MoodleApplicationService moodle, CareerCalculatorService calculator, StudyProductivityService productivity)
    {
        _store = store;
        _moodle = moodle;
        _calculator = calculator;
        _productivity = productivity;
        AddCourseCommand = new AsyncRelayCommand(AddCourseAsync);
        AddGradeCommand = new AsyncRelayCommand(AddGradeAsync, () => SelectedGradeCourse is not null);
        PlanExamsCommand = new AsyncRelayCommand(PlanExamsAsync);
        ConnectMoodleCommand = new AsyncRelayCommand(ConnectMoodleAsync);
        SyncMoodleCommand = new AsyncRelayCommand(SyncMoodleAsync);
        SaveStudyGoalsCommand = new AsyncRelayCommand(SaveStudyGoalsAsync);
        RecordFocusCommand = new AsyncRelayCommand(RecordFocusAsync);
        SaveQuickNoteCommand = new AsyncRelayCommand(SaveQuickNoteAsync);
        RecordAttendanceCommand = new AsyncRelayCommand(RecordAttendanceAsync);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _data = await _store.LoadAsync(cancellationToken);
        MoodleUrlInput = _data.MoodleBaseUrl;
        RefreshCollections();
        RefreshVisibleCourses();
        RefreshCareerSummary();
        RefreshExamSummary();
        DailyGoalInput = _data.Productivity.DailyStudyGoalMinutes.ToString(CultureInfo.InvariantCulture);
        WeeklyGoalInput = _data.Productivity.WeeklyStudyGoalMinutes.ToString(CultureInfo.InvariantCulture);
        SelectedProductivityCourse = Courses.FirstOrDefault();
        RefreshProductivityCollections();
    }

    private async Task AddCourseAsync()
    {
        if (string.IsNullOrWhiteSpace(CourseNameInput) || !int.TryParse(CourseCfuInput, out var cfu) || cfu <= 0)
        {
            StatusMessage = "Inserisci un nome e un numero di CFU valido.";
            return;
        }

        var course = new Course { Name = CourseNameInput.Trim(), Cfu = cfu };
        Courses.Add(course);
        RefreshVisibleCourses();
        try
        {
            await SaveAsync();
            CourseNameInput = string.Empty;
            StatusMessage = "Materia aggiunta.";
        }
        catch (Exception ex)
        {
            Courses.Remove(course);
            StatusMessage = $"Salvataggio non riuscito: {ex.Message}";
        }
        RefreshCourseSummary();
        RefreshVisibleCourses();
    }

    private async Task AddGradeAsync()
    {
        if (SelectedGradeCourse is null || !int.TryParse(GradeValueInput, out var grade) || grade is < 18 or > 30)
        {
            StatusMessage = "Seleziona una materia e inserisci un voto tra 18 e 30.";
            return;
        }

        var course = SelectedGradeCourse;
        var item = new Grade
        {
            CourseId = course.Id,
            CourseName = course.Name,
            Cfu = course.Cfu,
            Value = grade,
            Date = DateTime.Today,
            CountsTowardAverage = true
        };
        Grades.Add(item);
        var previousCompleted = course.Completed;
        course.Completed = true;
        try
        {
            await SaveAsync();
            StatusMessage = "Voto registrato e materia spostata nell'archivio.";
        }
        catch (Exception ex)
        {
            Grades.Remove(item);
            course.Completed = previousCompleted;
            StatusMessage = $"Salvataggio non riuscito: {ex.Message}";
        }
        RefreshCourseSummary();
        RefreshCareerSummary();
        RefreshVisibleCourses();
    }

    private async Task PlanExamsAsync()
    {
        try
        {
            ExamPlanner.AssignIndicativeSessions(_data.Exams, targetDate: _data.GraduationTarget);
            await SaveAsync();
            OnPropertyChanged(nameof(Exams));
            RefreshExamSummary();
            StatusMessage = "Pianificazione aggiornata. Le date generate sono indicative, non appelli ufficiali.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pianificazione non riuscita: {ex.Message}";
        }
    }

    private async Task ConnectMoodleAsync()
    {
        try
        {
            var siteName = await _moodle.VerifyAndStoreTokenAsync(MoodleUrlInput, MoodleToken);
            _data.MoodleBaseUrl = MoodleUrlInput.Trim();
            await SaveAsync();
            MoodleStatus = $"Collegato: {siteName}";
            StatusMessage = "Token verificato e salvato in Windows DPAPI.";
        }
        catch (Exception ex)
        {
            MoodleStatus = ex.Message;
        }
    }

    private async Task SyncMoodleAsync()
    {
        try
        {
            var courses = await _moodle.GetEnrolledCoursesAsync(MoodleUrlInput);
            var added = 0;
            foreach (var remote in courses)
            {
                var local = Courses.FirstOrDefault(c => c.MoodleCourseId == remote.id);
                if (local is null)
                {
                    Courses.Add(new Course
                    {
                        MoodleCourseId = remote.id,
                        Name = remote.fullname,
                        Code = remote.shortname
                    });
                    added++;
                }
            }
            _data.MoodleBaseUrl = MoodleUrlInput.Trim();
            await SaveAsync();
            MoodleStatus = $"Sincronizzati {courses.Count} corsi Moodle ({added} nuovi).";
            OnPropertyChanged(nameof(Courses));
            RefreshVisibleCourses();
            RefreshCourseSummary();
        }
        catch (Exception ex)
        {
            MoodleStatus = ex.Message;
        }
    }

    private async Task SaveStudyGoalsAsync()
    {
        if (!int.TryParse(DailyGoalInput, out var daily) || !int.TryParse(WeeklyGoalInput, out var weekly)) { ProductivitySummary = "Inserisci obiettivi espressi in minuti."; return; }
        try { _productivity.SetGoals(_data, daily, weekly); await SaveAsync(); RefreshProductivitySummary(); ProductivitySummary = "Obiettivi di studio salvati."; }
        catch (Exception ex) { ProductivitySummary = $"Obiettivi non salvati: {ex.Message}"; }
    }

    private async Task RecordFocusAsync()
    {
        if (!int.TryParse(FocusMinutesInput, out var minutes)) { ProductivitySummary = "Inserisci i minuti della sessione."; return; }
        try { _productivity.RecordFocus(_data, DateOnly.FromDateTime(DateTime.Today), minutes); await SaveAsync(); RefreshProductivitySummary(); ProductivitySummary = $"Registrati {minutes} minuti di studio."; FocusMinutesInput = "30"; }
        catch (Exception ex) { ProductivitySummary = $"Sessione non registrata: {ex.Message}"; }
    }

    private async Task SaveQuickNoteAsync()
    {
        if (SelectedProductivityCourse is null) { ProductivitySummary = "Seleziona una materia per collegare la nota."; return; }
        try { _productivity.SaveQuickNote(_data, null, SelectedProductivityCourse.Id, NoteTopicInput, NoteContentInput); await SaveAsync(); RefreshProductivityCollections(); NoteTopicInput = string.Empty; NoteContentInput = string.Empty; ProductivitySummary = $"Nota salvata per {SelectedProductivityCourse.Name}."; }
        catch (Exception ex) { ProductivitySummary = $"Nota non salvata: {ex.Message}"; }
    }

    private async Task RecordAttendanceAsync()
    {
        if (SelectedProductivityCourse is null || !DateTimeOffset.TryParse(AttendanceDateInput, CultureInfo.GetCultureInfo("it-IT"), DateTimeStyles.AssumeLocal, out var startsAt) || !int.TryParse(AttendanceDurationInput, out var duration)) { ProductivitySummary = "Controlla materia, data e durata della lezione."; return; }
        try { _productivity.RecordAttendance(_data, SelectedProductivityCourse.Id, AttendanceTitleInput, startsAt, duration, true, AttendanceNotesInput); await SaveAsync(); RefreshProductivityCollections(); AttendanceTitleInput = string.Empty; AttendanceNotesInput = string.Empty; ProductivitySummary = "Presenza registrata."; }
        catch (Exception ex) { ProductivitySummary = $"Presenza non registrata: {ex.Message}"; }
    }

    private void RefreshProductivityCollections()
    {
        QuickNotes.Clear(); foreach (var note in _data.Productivity.QuickNotes.OrderByDescending(x => x.UpdatedAt)) QuickNotes.Add(note);
        AttendanceRecords.Clear(); foreach (var row in _data.Productivity.Attendance.OrderByDescending(x => x.StartsAt)) AttendanceRecords.Add(row);
        RefreshProductivitySummary();
    }

    private void RefreshProductivitySummary()
    {
        OnPropertyChanged(nameof(CurrentStreakDisplay));
        OnPropertyChanged(nameof(WeeklyFocusDisplay));
    }
    private Exam? GetNextPlannedExam() =>
        Exams.Where(x => !x.Completed && x.PlannedDate.HasValue && x.PlannedDate.Value.Date >= DateTime.Today)
             .OrderBy(x => x.PlannedDate)
             .FirstOrDefault();

    private void RefreshCourseSummary()
    {
        OnPropertyChanged(nameof(ActiveCoursesCount));
        OnPropertyChanged(nameof(ArchivedCoursesCount));
        OnPropertyChanged(nameof(AllCoursesCount));
    }

    private void RefreshExamSummary()
    {
        OnPropertyChanged(nameof(NextExamNameDisplay));
        OnPropertyChanged(nameof(NextExamDateDisplay));
        OnPropertyChanged(nameof(NoPlannedExams));
    }

    private void RefreshVisibleCourses()
    {
        IEnumerable<Course> filtered = Courses;
        filtered = CourseFilter switch
        {
            "archive" => filtered.Where(x => x.Completed),
            "all" => filtered,
            _ => filtered.Where(x => !x.Completed)
        };

        if (!string.IsNullOrWhiteSpace(CourseSearchText))
        {
            var term = CourseSearchText.Trim();
            filtered = filtered.Where(x =>
                x.Name.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                x.Code.Contains(term, StringComparison.CurrentCultureIgnoreCase));
        }

        VisibleCourses.Clear();
        foreach (var course in filtered.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            VisibleCourses.Add(course);

        OnPropertyChanged(nameof(NoVisibleCourses));
        OnPropertyChanged(nameof(CourseEmptyMessage));
    }

    private async Task SaveAsync(CancellationToken cancellationToken = default) =>
        await _store.SaveAsync(_data, cancellationToken);

    private void RefreshCollections()
    {
        OnPropertyChanged(nameof(Courses));
        OnPropertyChanged(nameof(Grades));
        OnPropertyChanged(nameof(Exams));
        OnPropertyChanged(nameof(Announcements));
    }

    private void RefreshCareerSummary()
    {
        AverageDisplay = GradeCalculator.Format(GradeCalculator.WeightedAverage(_data.Grades, _calculator));
        BaseGradeDisplay = "Voto di partenza: " + GradeCalculator.Format(GradeCalculator.ProjectedBase(_data.Grades, _calculator));
        CreditsDisplay = $"CFU completati: {GradeCalculator.AcquiredCfu(_data.Grades, _calculator)}";

        var latestGrades = _data.Grades
            .Where(x => x.Value is >= 18 and <= 30)
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CourseId) ? x.Id : x.CourseId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(x => x.Date).ThenByDescending(x => x.Id, StringComparer.Ordinal).First())
            .Where(x => x.CountsTowardAverage)
            .ToList();

        ArithmeticAverageDisplay = latestGrades.Count == 0
            ? "—"
            : GradeCalculator.Format((decimal)latestGrades.Average(x => x.Value));

        OnPropertyChanged(nameof(AcquiredCreditsValue));
        OnPropertyChanged(nameof(ProjectedBaseGradeValue));
        OnPropertyChanged(nameof(LatestGradeDisplay));
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName == nameof(SelectedGradeCourse)) AddGradeCommand?.NotifyCanExecuteChanged();
    }


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}