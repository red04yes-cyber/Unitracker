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
    private readonly CalendarEventStore _calendarStore = new();
    public StudyWorkspaceViewModel Workspace { get; }
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

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Course> Courses => _data.Courses;
    public ObservableCollection<Grade> Grades => _data.Grades;
    public ObservableCollection<Exam> Exams => _data.Exams;
    public ObservableCollection<Announcement> Announcements => _data.Announcements;
    public ObservableCollection<Course> VisibleCourses { get; } = new();
    public ObservableCollection<CalendarEvent> CalendarEvents => _data.CalendarEvents;
    public ObservableCollection<CalendarEvent> VisibleCalendarEvents { get; } = new();
    public ObservableCollection<CalendarEvent> SelectedDayEvents { get; } = new();
    public ObservableCollection<CalendarDayCell> CalendarDayCells { get; } = new();
    private DateTime _calendarDate = DateTime.Today;
    private DateTime _selectedCalendarDate = DateTime.Today;
    private string _calendarViewMode = "Month";
    private CalendarDayCell? _selectedCalendarDay;
    private DateTime? _calendarEventDate = DateTime.Today;
    private string _calendarEventTitle = string.Empty;
    private string _calendarEventCategory = CalendarEventCategory.Personal;
    private string _calendarEventStartTime = "09:00";
    private string _calendarEventEndTime = "10:00";
    private bool _calendarEventAllDay;
    private CalendarEvent? _selectedCalendarEvent;
    public DateTime CalendarDate { get => _calendarDate; set { if (_calendarDate == value) return; Set(ref _calendarDate, value); RefreshCalendar(); } }
    public DateTime SelectedCalendarDate
    {
        get => _selectedCalendarDate;
        set
        {
            if (_selectedCalendarDate == value.Date) return;
            _selectedCalendarDate = value.Date;
            OnPropertyChanged();
            RefreshSelectedDayEvents();
        }
    }
    public string CalendarViewMode
    {
        get => _calendarViewMode;
        set
        {
            if (string.Equals(_calendarViewMode, value, StringComparison.Ordinal)) return;
            _calendarViewMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMonthCalendar));
            OnPropertyChanged(nameof(IsWeekCalendar));
            OnPropertyChanged(nameof(IsDayCalendar));
            RefreshCalendar();
        }
    }
    public CalendarDayCell? SelectedCalendarDay
    {
        get => _selectedCalendarDay;
        set
        {
            if (!SetCalendarDay(value)) return;
            if (value is not null) SelectedCalendarDate = value.Date;
        }
    }
    private bool SetCalendarDay(CalendarDayCell? value)
    {
        if (ReferenceEquals(_selectedCalendarDay, value)) return false;
        _selectedCalendarDay = value;
        OnPropertyChanged(nameof(SelectedCalendarDay));
        return true;
    }
    public string SelectedDayDisplay => SelectedCalendarDate.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"));
    public int CalendarGridColumns => CalendarViewMode == "Day" ? 1 : 7;
    public bool ShowCalendarWeekHeader => CalendarViewMode == "Month";
    public string CalendarAgendaTitle => CalendarViewMode == "Week" ? "Agenda settimanale" : "Agenda del giorno";
    public bool IsMonthCalendar => CalendarViewMode == "Month";
    public bool IsWeekCalendar => CalendarViewMode == "Week";
    public bool IsDayCalendar => CalendarViewMode == "Day";
    public DateTime? CalendarEventDate { get => _calendarEventDate; set => Set(ref _calendarEventDate, value); }
    public string CalendarEventTitle { get => _calendarEventTitle; set => Set(ref _calendarEventTitle, value); }
    public string CalendarEventCategoryInput { get => _calendarEventCategory; set => Set(ref _calendarEventCategory, value); }
    public string CalendarEventStartTime { get => _calendarEventStartTime; set => Set(ref _calendarEventStartTime, value); }
    public string CalendarEventEndTime { get => _calendarEventEndTime; set => Set(ref _calendarEventEndTime, value); }
    public bool CalendarEventAllDay { get => _calendarEventAllDay; set => Set(ref _calendarEventAllDay, value); }
    public CalendarEvent? SelectedCalendarEvent { get => _selectedCalendarEvent; set { Set(ref _selectedCalendarEvent, value); if (value is not null) { CalendarEventTitle = value.Title; CalendarEventDate = value.StartsAt.LocalDateTime.Date; CalendarEventCategoryInput = value.Category; CalendarEventStartTime = value.StartsAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture); CalendarEventEndTime = value.EndsAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture); CalendarEventAllDay = value.IsAllDay; } DeleteCalendarEventCommand?.NotifyCanExecuteChanged(); } }
    public string CalendarPeriodDisplay
    {
        get
        {
            var culture = CultureInfo.GetCultureInfo("it-IT");
            return CalendarViewMode switch
            {
                "Day" => CalendarDate.ToString("dddd d MMMM yyyy", culture),
                "Week" => GetWeekStart(CalendarDate).ToString("d MMM", culture) + " – " + GetWeekStart(CalendarDate).AddDays(6).ToString("d MMM yyyy", culture),
                _ => CalendarDate.ToString("MMMM yyyy", culture)
            };
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
    public AsyncRelayCommand AddCalendarEventCommand { get; }
    public AsyncRelayCommand DeleteCalendarEventCommand { get; }
    public AsyncRelayCommand PreviousCalendarPeriodCommand { get; }
    public AsyncRelayCommand NextCalendarPeriodCommand { get; }
    public AsyncRelayCommand TodayCalendarCommand { get; }
    public AsyncRelayCommand NewCalendarEventCommand { get; }

    public MainViewModel(IStudyDataStore store, MoodleApplicationService moodle, CareerCalculatorService calculator, StudyWorkspaceViewModel workspace)
    {
        _store = store;
        _moodle = moodle;
        _calculator = calculator;
        Workspace = workspace;
        AddCourseCommand = new AsyncRelayCommand(AddCourseAsync);
        AddGradeCommand = new AsyncRelayCommand(AddGradeAsync, () => SelectedGradeCourse is not null);
        PlanExamsCommand = new AsyncRelayCommand(PlanExamsAsync);
        ConnectMoodleCommand = new AsyncRelayCommand(ConnectMoodleAsync);
        SyncMoodleCommand = new AsyncRelayCommand(SyncMoodleAsync);
        AddCalendarEventCommand = new AsyncRelayCommand(AddCalendarEventAsync);
        DeleteCalendarEventCommand = new AsyncRelayCommand(DeleteCalendarEventAsync, () => SelectedCalendarEvent is not null);
        PreviousCalendarPeriodCommand = new AsyncRelayCommand(() => ChangeCalendarPeriodAsync(-1));
        NextCalendarPeriodCommand = new AsyncRelayCommand(() => ChangeCalendarPeriodAsync(1));
        TodayCalendarCommand = new AsyncRelayCommand(() => SetCalendarTodayAsync());
        NewCalendarEventCommand = new AsyncRelayCommand(NewCalendarEventAsync);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _data = await _store.LoadAsync(cancellationToken);

        // The calendar branch used a separate JSON file. Import it once into
        // the SQLite source of truth, then park the old file so a later delete
        // cannot resurrect events.
        var legacyCalendar = await _calendarStore.LoadAsync(cancellationToken);
        if (legacyCalendar.Count > 0 && _data.CalendarEvents.Count == 0)
        {
            foreach (var calendarEvent in legacyCalendar) _data.CalendarEvents.Add(calendarEvent);
            await SaveAsync(cancellationToken);
            await _calendarStore.MarkMigratedAsync(cancellationToken);
        }
        MoodleUrlInput = _data.MoodleBaseUrl;
        RefreshCollections();
        RefreshVisibleCourses();
        RefreshCareerSummary();
        RefreshExamSummary();
        Workspace.Initialize(_data);
        RefreshCalendar();
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

    private async Task AddCalendarEventAsync()
    {
        if (string.IsNullOrWhiteSpace(CalendarEventTitle) || CalendarEventDate is null)
        {
            StatusMessage = "Inserisci un titolo e una data per l'evento.";
            return;
        }
        try
        {
            var day = CalendarEventDate.Value.Date;
            var startOffset = TimeSpan.Zero;
            var endOffset = TimeSpan.FromHours(1);
            if (!CalendarEventAllDay)
            {
                if (!TimeOnly.TryParse(CalendarEventStartTime, CultureInfo.CurrentCulture, out var startTime) ||
                    !TimeOnly.TryParse(CalendarEventEndTime, CultureInfo.CurrentCulture, out var endTime))
                { StatusMessage = "Inserisci orari validi (HH:mm)."; return; }
                startOffset = startTime.ToTimeSpan();
                endOffset = endTime.ToTimeSpan();
            }
            var startLocal = day.Add(startOffset);
            var endLocal = CalendarEventAllDay ? day.AddDays(1) : day.Add(endOffset);
            var start = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal));
            var end = new DateTimeOffset(endLocal, TimeZoneInfo.Local.GetUtcOffset(endLocal));
            if (SelectedCalendarEvent is { } selected)
            {
                var oldTitle = selected.Title;
                var oldCategory = selected.Category;
                var oldStart = selected.StartsAt;
                var oldEnd = selected.EndsAt;
                var oldAllDay = selected.IsAllDay;
                var oldUpdatedAt = selected.UpdatedAt;
                selected.Title = CalendarEventTitle.Trim();
                selected.Category = CalendarEventCategoryInput;
                selected.StartsAt = start;
                selected.EndsAt = end;
                selected.IsAllDay = CalendarEventAllDay;
                selected.UpdatedAt = DateTimeOffset.Now;
                try { CalendarService.Validate(selected); await SaveAsync(); }
                catch { selected.Title = oldTitle; selected.Category = oldCategory; selected.StartsAt = oldStart; selected.EndsAt = oldEnd; selected.IsAllDay = oldAllDay; selected.UpdatedAt = oldUpdatedAt; throw; }
                SelectedCalendarEvent = null;
                StatusMessage = "Modifiche salvate.";
            }
            else
            {
                var item = CalendarService.CreateManualEvent(CalendarEventTitle, CalendarEventCategoryInput, start, end, CalendarEventAllDay);
                CalendarEvents.Add(item);
                try { await SaveAsync(); }
                catch { CalendarEvents.Remove(item); throw; }
                StatusMessage = "Evento aggiunto al calendario.";
            }
            CalendarEventTitle = string.Empty;
            CalendarEventAllDay = false;
            CalendarEventStartTime = "09:00";
            CalendarEventEndTime = "10:00";
            CalendarDate = day;
            RefreshCalendar();
        }
        catch (Exception ex) { StatusMessage = $"Evento non salvato: {ex.Message}"; }
    }

    private async Task NewCalendarEventAsync()
    {
        SelectedCalendarEvent = null;
        CalendarEventTitle = string.Empty;
        CalendarEventDate = SelectedCalendarDate;
        CalendarEventCategoryInput = CalendarEventCategory.Personal;
        CalendarEventStartTime = "09:00";
        CalendarEventEndTime = "10:00";
        CalendarEventAllDay = false;
        await Task.CompletedTask;
    }

    private async Task DeleteCalendarEventAsync()
    {
        if (SelectedCalendarEvent is null) return;
        var item = SelectedCalendarEvent;
        CalendarEvents.Remove(item);
        try
        {
            await SaveAsync();
            SelectedCalendarEvent = null;
            RefreshCalendar();
            StatusMessage = "Evento eliminato.";
        }
        catch (Exception ex)
        {
            CalendarEvents.Add(item);
            StatusMessage = $"Eliminazione non riuscita: {ex.Message}";
        }
    }

    private async Task ChangeCalendarPeriodAsync(int direction)
    {
        CalendarDate = CalendarViewMode switch
        {
            "Day" => CalendarDate.AddDays(direction),
            "Week" => CalendarDate.AddDays(direction * 7),
            _ => CalendarDate.AddMonths(direction)
        };
        await Task.CompletedTask;
    }

    private async Task SetCalendarTodayAsync()
    {
        CalendarDate = DateTime.Today;
        CalendarEventDate = DateTime.Today;
        await Task.CompletedTask;
    }

    private void RefreshCalendar()
    {
        CalendarDayCells.Clear();
        DateTime gridStart;
        DateTime visibleStart;
        DateTime visibleEnd;
        int dayCount;

        if (CalendarViewMode == "Day")
        {
            gridStart = CalendarDate.Date;
            visibleStart = gridStart;
            visibleEnd = visibleStart.AddDays(1);
            dayCount = 1;
        }
        else if (CalendarViewMode == "Week")
        {
            gridStart = GetWeekStart(CalendarDate);
            visibleStart = gridStart;
            visibleEnd = visibleStart.AddDays(7);
            dayCount = 7;
        }
        else
        {
            var monthStart = new DateTime(CalendarDate.Year, CalendarDate.Month, 1);
            var mondayOffset = ((int)monthStart.DayOfWeek + 6) % 7;
            gridStart = monthStart.AddDays(-mondayOffset);
            visibleStart = monthStart;
            visibleEnd = monthStart.AddMonths(1);
            dayCount = 42;
        }

        for (var index = 0; index < dayCount; index++)
        {
            var date = gridStart.AddDays(index);
            var dayStart = new DateTimeOffset(date, TimeZoneInfo.Local.GetUtcOffset(date));
            var dayEndDate = date.AddDays(1);
            var dayEnd = new DateTimeOffset(dayEndDate, TimeZoneInfo.Local.GetUtcOffset(dayEndDate));
            var dayEvents = CalendarService.GetEventsInRange(CalendarEvents, dayStart, dayEnd)
                .Where(item => !item.IsCancelled)
                .OrderBy(item => item.StartsAt)
                .ToList();
            CalendarDayCells.Add(new CalendarDayCell(
                date,
                CalendarViewMode != "Month" || date.Month == CalendarDate.Month,
                dayEvents));
        }

        if (CalendarViewMode == "Month"
            ? SelectedCalendarDate.Month != CalendarDate.Month || SelectedCalendarDate.Year != CalendarDate.Year
            : SelectedCalendarDate.Date < gridStart || SelectedCalendarDate.Date >= gridStart.AddDays(dayCount))
            SelectedCalendarDate = CalendarDate.Date;
        SetCalendarDay(CalendarDayCells.FirstOrDefault(x => x.Date == SelectedCalendarDate));

        VisibleCalendarEvents.Clear();
        var rangeStart = new DateTimeOffset(visibleStart, TimeZoneInfo.Local.GetUtcOffset(visibleStart));
        var rangeEnd = new DateTimeOffset(visibleEnd, TimeZoneInfo.Local.GetUtcOffset(visibleEnd));
        foreach (var item in CalendarService.GetEventsInRange(CalendarEvents, rangeStart, rangeEnd)
                     .OrderBy(x => x.StartsAt))
            VisibleCalendarEvents.Add(item);

        RefreshSelectedDayEvents();
        OnPropertyChanged(nameof(CalendarPeriodDisplay));
        OnPropertyChanged(nameof(SelectedDayDisplay));
        OnPropertyChanged(nameof(CalendarGridColumns));
        OnPropertyChanged(nameof(ShowCalendarWeekHeader));
        OnPropertyChanged(nameof(CalendarAgendaTitle));
        OnPropertyChanged(nameof(IsMonthCalendar));
        OnPropertyChanged(nameof(IsWeekCalendar));
        OnPropertyChanged(nameof(IsDayCalendar));
    }

    private void RefreshSelectedDayEvents()
    {
        SelectedDayEvents.Clear();
        var dayStart = SelectedCalendarDate.Date;
        var nextDay = dayStart.AddDays(1);
        var start = new DateTimeOffset(dayStart, TimeZoneInfo.Local.GetUtcOffset(dayStart));
        var end = new DateTimeOffset(nextDay, TimeZoneInfo.Local.GetUtcOffset(nextDay));
        foreach (var item in CalendarService.GetEventsInRange(CalendarEvents, start, end).OrderBy(x => x.StartsAt))
            SelectedDayEvents.Add(item);
        OnPropertyChanged(nameof(SelectedDayDisplay));
    }

    private static DateTime GetWeekStart(DateTime date) =>
        date.Date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

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