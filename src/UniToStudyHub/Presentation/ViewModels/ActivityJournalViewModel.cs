using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using UniToStudyHub.Models;
using UniToStudyHub.Presentation.Commands;
using UniToStudyHub.Security;
using UniToStudyHub.Services;

namespace UniToStudyHub.Presentation.ViewModels;

public sealed record ActivityFilterOption(string Key, string Label, StudyActivityType? Type);
public sealed record QuickLogActivityOption(StudyActivityType Type, string Label);

public sealed record ActivityJournalEntry(
    StudyActivityData Activity,
    string Title,
    string TypeDisplay,
    string CourseDisplay,
    string TimeRangeDisplay,
    string DurationDisplay,
    string OriginDisplay,
    string PrivacyDisplay,
    bool HasOverlap);

public sealed record ActivityTimelineRow(
    string TimeRange,
    string RecordedDisplay,
    int ProgressPercent,
    bool HasOverlap,
    string ToolTip);

public sealed record ActivityHeatmapCellView(int Hour, string ToolTip, double Intensity);

public sealed record ActivityHeatmapDayView(
    string DayLabel,
    string TotalDisplay,
    IReadOnlyList<ActivityHeatmapCellView> Cells);

/// <summary>
/// Presentation adapter for the persisted activity ledger. The ledger remains
/// the source of truth for interval arithmetic, overlap handling and revisions.
/// </summary>
public sealed class ActivityJournalViewModel : INotifyPropertyChanged
{
    private static readonly IReadOnlyList<QuickLogActivityOption> QuickLogTypes = Array.AsReadOnly(new[]
    {
        new QuickLogActivityOption(StudyActivityType.Study, "Studio"),
        new QuickLogActivityOption(StudyActivityType.Review, "Ripasso"),
        new QuickLogActivityOption(StudyActivityType.Quiz, "Quiz"),
        new QuickLogActivityOption(StudyActivityType.Exercise, "Esercizi"),
        new QuickLogActivityOption(StudyActivityType.Reading, "Lettura"),
        new QuickLogActivityOption(StudyActivityType.Simulation, "Simulazione")
    });

    private static readonly IReadOnlyList<ActivityFilterOption> Filters = Array.AsReadOnly(new[]
    {
        new ActivityFilterOption("all", "Tutte", null),
        new ActivityFilterOption("study", "Studio", StudyActivityType.Study),
        new ActivityFilterOption("review", "Ripasso", StudyActivityType.Review),
        new ActivityFilterOption("quiz", "Quiz", StudyActivityType.Quiz),
        new ActivityFilterOption("lecture", "Lezione", StudyActivityType.Lecture),
        new ActivityFilterOption("task", "Task", StudyActivityType.Task),
        new ActivityFilterOption("mission", "Missione", StudyActivityType.Mission),
        new ActivityFilterOption("simulation", "Simulazione", StudyActivityType.Simulation),
        new ActivityFilterOption("exercise", "Esercizio", StudyActivityType.Exercise),
        new ActivityFilterOption("reading", "Lettura", StudyActivityType.Reading),
        new ActivityFilterOption("other", "Altro", StudyActivityType.Other)
    });

    private readonly IStudyDataStore _store;
    private readonly ActivityLedgerService _ledger;
    private StudyData? _data;
    private DateTime _selectedDay = DateTime.Today;
    private DateTime _entryDateInput = DateTime.Today;
    private int _slotMinutes = 30;
    private ActivityFilterOption _selectedActivityFilter = Filters[0];
    private string _titleInput = string.Empty;
    private string _startTimeInput = "09:00";
    private string _endTimeInput = "10:00";
    private string _noteInput = string.Empty;
    private bool _isPrivate = true;
    private string _deleteReasonInput = "Voce inserita per errore";
    private string _statusMessage = "Le registrazioni manuali e quelle del timer vengono raccolte nello stesso diario.";
    private Course? _selectedCourse;
    private SyllabusTopicData? _selectedSyllabusTopic;
    private StudyTaskData? _selectedTask;
    private QuickLogActivityOption _selectedQuickLogType = QuickLogTypes[0];
    private ActivityJournalEntry? _selectedEntry;
    private string _recordedDisplay = "0 min";
    private string _unregisteredDisplay = "24 h 00 min";
    private string _overlapDisplay = "0 min";
    private string _activityCountDisplay = "0";
    private string _overlapStatus = "Nessuna sovrapposizione rilevata.";
    private string _heatmapRangeDisplay = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<Course> Courses { get; } = new();
    public ObservableCollection<SyllabusTopicData> SyllabusTopics { get; } = new();
    public ObservableCollection<StudyTaskData> AvailableTasks { get; } = new();
    public ObservableCollection<ActivityJournalEntry> Entries { get; } = new();
    public ObservableCollection<ActivityTimelineRow> TimelineSlots { get; } = new();
    public ObservableCollection<ActivityHeatmapDayView> HeatmapDays { get; } = new();

    public IReadOnlyList<int> SlotSizeOptions { get; } = Array.AsReadOnly(new[] { 15, 30, 60 });
    public IReadOnlyList<QuickLogActivityOption> QuickLogTypeOptions => QuickLogTypes;
    public IReadOnlyList<ActivityFilterOption> ActivityFilterOptions => Filters;

    public DateTime SelectedDay
    {
        get => _selectedDay;
        set
        {
            var date = value.Date;
            if (!Set(ref _selectedDay, date)) return;
            EntryDateInput = date;
            Refresh();
            OnPropertyChanged(nameof(SelectedDayLabel));
        }
    }

    public string SelectedDayLabel =>
        SelectedDay.ToString("dddd d MMMM yyyy", CultureInfo.GetCultureInfo("it-IT"));

    public DateTime EntryDateInput
    {
        get => _entryDateInput;
        set => Set(ref _entryDateInput, value.Date);
    }

    public int SlotMinutes
    {
        get => _slotMinutes;
        set
        {
            if (!SlotSizeOptions.Contains(value) || !Set(ref _slotMinutes, value)) return;
            Refresh();
        }
    }

    public ActivityFilterOption SelectedActivityFilter
    {
        get => _selectedActivityFilter;
        set
        {
            if (value is null || !Set(ref _selectedActivityFilter, value)) return;
            Refresh();
        }
    }

    public string TitleInput
    {
        get => _titleInput;
        set
        {
            if (!Set(ref _titleInput, value ?? string.Empty)) return;
            AddQuickLogCommand?.NotifyCanExecuteChanged();
        }
    }

    public string StartTimeInput
    {
        get => _startTimeInput;
        set => Set(ref _startTimeInput, value ?? string.Empty);
    }

    public string EndTimeInput
    {
        get => _endTimeInput;
        set => Set(ref _endTimeInput, value ?? string.Empty);
    }

    public string NoteInput
    {
        get => _noteInput;
        set => Set(ref _noteInput, value ?? string.Empty);
    }

    public bool IsPrivate
    {
        get => _isPrivate;
        set => Set(ref _isPrivate, value);
    }

    public string DeleteReasonInput
    {
        get => _deleteReasonInput;
        set
        {
            if (!Set(ref _deleteReasonInput, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(CanDeleteSelectedEntry));
            DeleteSelectedActivityCommand?.NotifyCanExecuteChanged();
        }
    }

    public Course? SelectedCourse
    {
        get => _selectedCourse;
        set
        {
            if (!Set(ref _selectedCourse, value)) return;
            RefreshLinkedOptions();
        }
    }

    public SyllabusTopicData? SelectedSyllabusTopic
    {
        get => _selectedSyllabusTopic;
        set => Set(ref _selectedSyllabusTopic, value);
    }

    public StudyTaskData? SelectedTask
    {
        get => _selectedTask;
        set
        {
            if (value is not null && !string.IsNullOrWhiteSpace(value.CourseId))
            {
                var matchingCourse = Courses.FirstOrDefault(x =>
                    string.Equals(x.Id, value.CourseId, StringComparison.Ordinal));
                if (matchingCourse is not null && !ReferenceEquals(SelectedCourse, matchingCourse))
                    SelectedCourse = matchingCourse;
            }
            Set(ref _selectedTask, value);
        }
    }

    public QuickLogActivityOption SelectedQuickLogType
    {
        get => _selectedQuickLogType;
        set => Set(ref _selectedQuickLogType, value ?? QuickLogTypes[0]);
    }

    public ActivityJournalEntry? SelectedEntry
    {
        get => _selectedEntry;
        set
        {
            if (!Set(ref _selectedEntry, value)) return;
            OnPropertyChanged(nameof(CanDeleteSelectedEntry));
            DeleteSelectedActivityCommand?.NotifyCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string RecordedDisplay { get => _recordedDisplay; private set => Set(ref _recordedDisplay, value); }
    public string UnregisteredDisplay { get => _unregisteredDisplay; private set => Set(ref _unregisteredDisplay, value); }
    public string OverlapDisplay { get => _overlapDisplay; private set => Set(ref _overlapDisplay, value); }
    public string ActivityCountDisplay { get => _activityCountDisplay; private set => Set(ref _activityCountDisplay, value); }
    public string OverlapStatus { get => _overlapStatus; private set => Set(ref _overlapStatus, value); }
    public string HeatmapRangeDisplay { get => _heatmapRangeDisplay; private set => Set(ref _heatmapRangeDisplay, value); }

    public bool NoEntries => Entries.Count == 0;
    public string EmptyEntriesMessage => SelectedActivityFilter.Type is null
        ? "Nessuna attività registrata per il giorno selezionato."
        : "Nessuna attività corrisponde al filtro selezionato per questo giorno.";
    public bool HasOverlaps => !string.Equals(OverlapDisplay, "0 min", StringComparison.Ordinal);
    public bool CanDeleteSelectedEntry =>
        SelectedEntry?.Activity.Origin == StudyActivityOrigin.Manual &&
        !string.IsNullOrWhiteSpace(DeleteReasonInput);

    public AsyncRelayCommand PreviousDayCommand { get; }
    public AsyncRelayCommand NextDayCommand { get; }
    public AsyncRelayCommand TodayCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand AddQuickLogCommand { get; }
    public AsyncRelayCommand DeleteSelectedActivityCommand { get; }

    public ActivityJournalViewModel(IStudyDataStore store, ActivityLedgerService ledger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        PreviousDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(-1));
        NextDayCommand = new AsyncRelayCommand(() => ChangeDayAsync(1));
        TodayCommand = new AsyncRelayCommand(SetTodayAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        AddQuickLogCommand = new AsyncRelayCommand(AddQuickLogAsync, () => _data is not null && !string.IsNullOrWhiteSpace(TitleInput));
        DeleteSelectedActivityCommand = new AsyncRelayCommand(DeleteSelectedActivityAsync, () => CanDeleteSelectedEntry);
    }

    public void Initialize(StudyData data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        Courses.Clear();
        foreach (var course in data.Courses.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase))
            Courses.Add(course);

        var selectedCourseId = SelectedCourse?.Id;
        SelectedCourse = Courses.FirstOrDefault(x => x.Id == selectedCourseId) ?? Courses.FirstOrDefault();

        EntryDateInput = SelectedDay;
        Refresh();
        AddQuickLogCommand.NotifyCanExecuteChanged();
    }

    public bool Refresh()
    {
        if (_data is null) return false;
        try
        {
            RefreshCourses();
            RefreshLinkedOptions();
            var day = DateOnly.FromDateTime(SelectedDay);
            var overlaps = _ledger.FindOverlaps(_data, day, timeZoneId: TimeZoneInfo.Local.Id);
            var overlappingIds = overlaps
                .SelectMany(x => new[] { x.FirstActivityId, x.SecondActivityId })
                .ToHashSet(StringComparer.Ordinal);
            RefreshTimeline(overlaps.Count);
            RefreshHeatmap();
            RefreshEntries(overlappingIds);
            OnPropertyChanged(nameof(NoEntries));
            OnPropertyChanged(nameof(HasOverlaps));
            return true;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Diario non aggiornato: {SecurityRedaction.ForUser(ex)}";
            return false;
        }
    }

    public async Task AddQuickLogAsync()
    {
        if (_data is null)
        {
            StatusMessage = "Il diario non è ancora pronto.";
            return;
        }

        if (string.IsNullOrWhiteSpace(TitleInput))
        {
            StatusMessage = "Inserisci una descrizione per l'attività.";
            return;
        }

        if (!TimeOnly.TryParseExact(StartTimeInput.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var startTime) ||
            !TimeOnly.TryParseExact(EndTimeInput.Trim(), "HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var endTime) ||
            endTime <= startTime)
        {
            StatusMessage = "Inserisci orari validi nel formato HH:mm; la fine deve essere successiva all'inizio nello stesso giorno.";
            return;
        }

        var zone = TimeZoneInfo.Local;
        var startLocal = DateTime.SpecifyKind(EntryDateInput.Date.Add(startTime.ToTimeSpan()), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(EntryDateInput.Date.Add(endTime.ToTimeSpan()), DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(startLocal) || zone.IsInvalidTime(endLocal))
        {
            StatusMessage = "Uno degli orari non esiste a causa del cambio dell'ora legale.";
            return;
        }
        if (zone.IsAmbiguousTime(startLocal) || zone.IsAmbiguousTime(endLocal))
        {
            StatusMessage = "Uno degli orari è ambiguo a causa del cambio dell'ora solare; scegli un orario non ambiguo.";
            return;
        }

        var startedAt = new DateTimeOffset(startLocal, zone.GetUtcOffset(startLocal));
        var endedAt = new DateTimeOffset(endLocal, zone.GetUtcOffset(endLocal));
        if (endedAt <= startedAt)
        {
            StatusMessage = "L'intervallo selezionato non è valido nel fuso orario locale.";
            return;
        }

        var previousRevisionIds = _data.ActivityRevisions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        StudyActivityData? activity = null;
        try
        {
            activity = _ledger.AddQuickLog(
                _data,
                SelectedQuickLogType.Type,
                TitleInput.Trim(),
                startedAt,
                endedAt,
                courseId: SelectedCourse?.Id,
                syllabusTopicId: SelectedSyllabusTopic?.Id,
                taskId: SelectedTask?.Id,
                note: NoteInput,
                isPrivate: IsPrivate);

            await _store.SaveAsync(_data);
            TitleInput = string.Empty;
            NoteInput = string.Empty;
            SelectedSyllabusTopic = null;
            SelectedTask = null;
            StatusMessage = IsPrivate
                ? "Attività registrata. La nota è privata e viene esclusa dagli export standard; titolo e orari restano esportabili."
                : "Attività registrata nel diario.";
            if (SelectedDay.Date != EntryDateInput.Date)
                SelectedDay = EntryDateInput.Date;
            else
                Refresh();
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                _data.StudyActivities.Remove(activity);
                foreach (var revision in _data.ActivityRevisions
                             .Where(x => x.ActivityId == activity.Id && !previousRevisionIds.Contains(x.Id))
                             .ToList())
                    _data.ActivityRevisions.Remove(revision);
            }
            StatusMessage = $"Attività non registrata: {SecurityRedaction.ForUser(ex)}";
        }
    }

    public async Task DeleteSelectedActivityAsync()
    {
        if (_data is null || SelectedEntry?.Activity is not { Origin: StudyActivityOrigin.Manual } activity)
            return;
        if (string.IsNullOrWhiteSpace(DeleteReasonInput))
        {
            StatusMessage = "Indica il motivo della correzione.";
            return;
        }

        var previousDeletedAt = activity.DeletedAt;
        var previousUpdatedAt = activity.UpdatedAt;
        var previousRevision = activity.Revision;
        var previousRevisionIds = _data.ActivityRevisions.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        try
        {
            _ledger.SoftDelete(_data, activity.Id, DeleteReasonInput.Trim());
            await _store.SaveAsync(_data);
            SelectedEntry = null;
            StatusMessage = "Voce rimossa dal diario; la revisione resta nello storico locale.";
            Refresh();
        }
        catch (Exception ex)
        {
            activity.DeletedAt = previousDeletedAt;
            activity.UpdatedAt = previousUpdatedAt;
            activity.Revision = previousRevision;
            foreach (var revision in _data.ActivityRevisions
                         .Where(x => x.ActivityId == activity.Id && !previousRevisionIds.Contains(x.Id))
                         .ToList())
                _data.ActivityRevisions.Remove(revision);
            StatusMessage = $"Voce non rimossa: {SecurityRedaction.ForUser(ex)}";
        }
    }

    private Task ChangeDayAsync(int offset)
    {
        try
        {
            SelectedDay = SelectedDay.AddDays(offset);
        }
        catch (ArgumentOutOfRangeException)
        {
            StatusMessage = "Limite del calendario raggiunto.";
        }
        return Task.CompletedTask;
    }

    private Task SetTodayAsync()
    {
        SelectedDay = DateTime.Today;
        return Task.CompletedTask;
    }

    private Task RefreshAsync()
    {
        if (Refresh())
            StatusMessage = "Diario aggiornato con le sessioni e le attività salvate.";
        return Task.CompletedTask;
    }

    private void RefreshTimeline(int overlapCount)
    {
        if (_data is null) return;
        var day = DateOnly.FromDateTime(SelectedDay);
        var timeline = _ledger.BuildTimeline(_data, day, SlotMinutes, timeZoneId: TimeZoneInfo.Local.Id);
        TimelineSlots.Clear();
        foreach (var slot in timeline.Slots)
        {
            var duration = Math.Max(1, (int)Math.Floor((slot.EndsAt - slot.StartsAt).TotalSeconds));
            var endLabel = slot.EndsAt.Date > slot.StartsAt.Date ? "24:00" : slot.EndsAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
            var recordedMinutes = (int)Math.Ceiling(slot.RecordedSeconds / 60d);
            var activityCount = slot.ActivityIds.Count;
            TimelineSlots.Add(new ActivityTimelineRow(
                $"{slot.StartsAt.ToLocalTime():HH:mm}–{endLabel}",
                $"{recordedMinutes} min",
                Math.Clamp((int)Math.Round(slot.RecordedSeconds * 100d / duration), 0, 100),
                slot.HasOverlap,
                slot.RecordedSeconds == 0
                    ? "Nessuna attività registrata in questo intervallo."
                    : $"{recordedMinutes} min registrati · {activityCount} attività{(slot.HasOverlap ? " · sovrapposizione" : string.Empty)}"));
        }

        RecordedDisplay = FormatMinutes(timeline.RecordedSeconds);
        UnregisteredDisplay = FormatMinutes(timeline.UnregisteredSeconds);
        OverlapDisplay = FormatMinutes(timeline.OverlapSeconds);
        OverlapStatus = overlapCount == 0
            ? "Nessuna sovrapposizione rilevata: gli intervalli registrati non si accavallano."
            : $"Rilevate {overlapCount} sovrapposizioni. Il tempo sovrapposto viene conteggiato una sola volta.";
    }

    private void RefreshHeatmap()
    {
        if (_data is null) return;
        var to = DateOnly.FromDateTime(SelectedDay);
        var from = to.DayNumber >= 6 ? to.AddDays(-6) : DateOnly.MinValue;
        var heatmap = _ledger.BuildHeatmap(_data, from, to, timeZoneId: TimeZoneInfo.Local.Id);
        var maximum = heatmap.Cells.Count == 0 ? 0 : heatmap.Cells.Max(x => x.RecordedSeconds);
        var culture = CultureInfo.GetCultureInfo("it-IT");
        HeatmapDays.Clear();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            var byHour = heatmap.Cells.Where(x => x.Day == day).ToDictionary(x => x.Hour);
            var cells = new List<ActivityHeatmapCellView>(24);
            var dailySeconds = 0;
            for (var hour = 0; hour < 24; hour++)
            {
                byHour.TryGetValue(hour, out var cell);
                var seconds = cell?.RecordedSeconds ?? 0;
                var count = cell?.ActivityCount ?? 0;
                dailySeconds += seconds;
                var intensity = seconds == 0 || maximum == 0
                    ? 0.06
                    : 0.2 + 0.8 * seconds / maximum;
                cells.Add(new ActivityHeatmapCellView(
                    hour,
                    $"{hour:00}:00 · {(int)Math.Ceiling(seconds / 60d)} min · {count} attività",
                    intensity));
            }

            HeatmapDays.Add(new ActivityHeatmapDayView(
                day.ToDateTime(TimeOnly.MinValue).ToString("ddd dd/MM", culture),
                FormatMinutes(dailySeconds),
                cells));
        }

        HeatmapRangeDisplay = $"Ultimi 7 giorni · {from.ToString("dd/MM", culture)}–{to.ToString("dd/MM", culture)}";
    }

    private void RefreshEntries(IReadOnlySet<string> overlappingIds)
    {
        if (_data is null) return;
        var day = DateOnly.FromDateTime(SelectedDay);
        var selectedId = SelectedEntry?.Activity.Id;
        var filter = SelectedActivityFilter.Type;
        var now = DateTimeOffset.Now;
        var diary = _ledger.GetDiary(_data, day, day, includePrivate: true, timeZoneId: TimeZoneInfo.Local.Id)
            .Where(x => x.Status != StudyActivityStatus.Planned)
            .ToList();
        var courseNames = _data.Courses
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().Name, StringComparer.Ordinal);
        var sessions = _data.StudySessions
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);

        var entries = diary
            .Where(x => filter is null || x.Type == filter)
            .OrderByDescending(x => x.StartedAt)
            .Select(activity =>
            {
                var courseName = courseNames.TryGetValue(activity.CourseId ?? string.Empty, out var resolvedCourseName)
                    ? resolvedCourseName
                    : "Senza materia";
                StudySessionData? session = null;
                if (!string.IsNullOrWhiteSpace(activity.SessionId) &&
                    sessions.TryGetValue(activity.SessionId, out var resolvedSession))
                    session = resolvedSession;
                var end = activity.EndedAt?.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)
                          ?? (session?.State == StudySessionState.Paused ? "in pausa" : "in corso");
                var seconds = session is null
                    ? activity.EffectiveSeconds
                    : FocusSessionService.ElapsedSeconds(session, now);

                return new ActivityJournalEntry(
                    activity,
                    activity.Title,
                    TypeLabel(activity.Type),
                    courseName,
                    $"{activity.StartedAt.ToLocalTime():HH:mm}–{end}",
                    FormatMinutes(seconds),
                    OriginLabel(activity.Origin),
                    activity.IsPrivate ? "Nota privata" : "Nota esportabile",
                    overlappingIds.Contains(activity.Id));
            })
            .ToList();

        SelectedEntry = null;
        Entries.Clear();
        foreach (var entry in entries) Entries.Add(entry);
        SelectedEntry = Entries.FirstOrDefault(x => x.Activity.Id == selectedId);
        OnPropertyChanged(nameof(NoEntries));
        OnPropertyChanged(nameof(EmptyEntriesMessage));
        ActivityCountDisplay = diary.Count.ToString(CultureInfo.CurrentCulture);
    }

    private void RefreshCourses()
    {
        if (_data is null) return;
        var desired = _data.Courses
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var coursesChanged = Courses.Count != desired.Count ||
                             Courses.Where((course, index) => !ReferenceEquals(course, desired[index])).Any();
        if (!coursesChanged) return;

        var selectedId = SelectedCourse?.Id;
        Courses.Clear();
        foreach (var course in desired) Courses.Add(course);

        var selected = Courses.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.Ordinal))
                       ?? Courses.FirstOrDefault();
        if (!ReferenceEquals(_selectedCourse, selected))
        {
            _selectedCourse = selected;
            OnPropertyChanged(nameof(SelectedCourse));
        }
    }

    private void RefreshLinkedOptions()
    {
        if (_data is null) return;
        var courseId = SelectedCourse?.Id;
        var desiredTopics = _data.SyllabusTopics.Where(x =>
                string.IsNullOrWhiteSpace(courseId) || string.Equals(x.CourseId, courseId, StringComparison.Ordinal))
            .OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var topicsChanged = SyllabusTopics.Count != desiredTopics.Count ||
                            SyllabusTopics.Where((topic, index) => !ReferenceEquals(topic, desiredTopics[index])).Any();
        if (topicsChanged)
        {
            SyllabusTopics.Clear();
            foreach (var topic in desiredTopics) SyllabusTopics.Add(topic);
        }
        if (SelectedSyllabusTopic is not null)
        {
            var matchingTopic = SyllabusTopics.FirstOrDefault(x => x.Id == SelectedSyllabusTopic.Id);
            if (!ReferenceEquals(matchingTopic, SelectedSyllabusTopic))
                SelectedSyllabusTopic = matchingTopic;
        }

        var desiredTasks = _data.StudyTasks.Where(x =>
                (x.Status is StudyTaskStatus.Todo or StudyTaskStatus.InProgress) &&
                (string.IsNullOrWhiteSpace(courseId) || string.IsNullOrWhiteSpace(x.CourseId) ||
                 string.Equals(x.CourseId, courseId, StringComparison.Ordinal)))
            .OrderBy(x => x.DueAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(x => x.Priority)
            .ToList();
        var tasksChanged = AvailableTasks.Count != desiredTasks.Count ||
                           AvailableTasks.Where((task, index) => !ReferenceEquals(task, desiredTasks[index])).Any();
        if (tasksChanged)
        {
            AvailableTasks.Clear();
            foreach (var task in desiredTasks) AvailableTasks.Add(task);
        }
        if (SelectedTask is not null)
        {
            var matchingTask = AvailableTasks.FirstOrDefault(x => x.Id == SelectedTask.Id);
            if (!ReferenceEquals(matchingTask, SelectedTask))
                SelectedTask = matchingTask;
        }
    }

    private static string TypeLabel(StudyActivityType type) => type switch
    {
        StudyActivityType.Study => "Studio",
        StudyActivityType.Review => "Ripasso",
        StudyActivityType.Quiz => "Quiz",
        StudyActivityType.Lecture => "Lezione",
        StudyActivityType.Task => "Task",
        StudyActivityType.Mission => "Missione",
        StudyActivityType.Simulation => "Simulazione",
        StudyActivityType.Exercise => "Esercizio",
        StudyActivityType.Reading => "Lettura",
        _ => "Altro"
    };

    private static string OriginLabel(StudyActivityOrigin origin) => origin switch
    {
        StudyActivityOrigin.Timer => "Timer",
        StudyActivityOrigin.Calendar => "Calendario",
        StudyActivityOrigin.Import => "Importazione",
        _ => "Manuale"
    };

    private static string FormatMinutes(int seconds)
    {
        var minutes = (int)Math.Ceiling(Math.Max(0, seconds) / 60d);
        return minutes >= 60
            ? $"{minutes / 60:N0} h {minutes % 60:00} min"
            : $"{minutes:N0} min";
    }

    private bool CanAddQuickLog() => _data is not null && !string.IsNullOrWhiteSpace(TitleInput);

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
