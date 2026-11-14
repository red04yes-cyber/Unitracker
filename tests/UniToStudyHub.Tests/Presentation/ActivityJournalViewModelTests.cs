using UniToStudyHub.Models;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Presentation;

public sealed class ActivityJournalViewModelTests
{
    private static readonly DateTime Day = new(2026, 11, 14);

    private static DateTimeOffset At(int hour, int minute = 0)
    {
        var local = new DateTime(Day.Year, Day.Month, Day.Day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static StudyData Data()
    {
        var data = new StudyData();
        data.Courses.Add(new Course { Id = "analisi", Name = "Analisi", Cfu = 9 });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "limiti",
            CourseId = "analisi",
            Title = "Limiti e continuità"
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "esercizi-limiti",
            CourseId = "analisi",
            Title = "Esercizi sui limiti",
            EstimatedMinutes = 60,
            Status = StudyTaskStatus.Todo
        });
        return data;
    }

    private static ActivityJournalViewModel CreateViewModel(StudyData data, out MemoryStore store)
    {
        store = new MemoryStore(data);
        var viewModel = new ActivityJournalViewModel(store, new ActivityLedgerService());
        viewModel.Initialize(data);
        viewModel.SelectedDay = Day;
        return viewModel;
    }

    private static StudyActivityData AddActivity(
        StudyData data,
        StudyActivityType type,
        string title,
        int startHour,
        int startMinute,
        int endHour,
        int endMinute)
    {
        return new ActivityLedgerService().AddQuickLog(
            data,
            type,
            title,
            At(startHour, startMinute),
            At(endHour, endMinute),
            courseId: "analisi",
            now: At(12));
    }

    [Fact]
    public void InitializeBuildsDailyTimelineAndSevenDayHeatmap()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Ripasso limiti", 9, 0, 10, 0);
        var viewModel = CreateViewModel(data, out _);

        Assert.Equal("1 h 00 min", viewModel.RecordedDisplay);
        Assert.Equal(48, viewModel.TimelineSlots.Count);
        Assert.Equal(7, viewModel.HeatmapDays.Count);
        Assert.All(viewModel.HeatmapDays, row => Assert.Equal(24, row.Cells.Count));
        Assert.Equal(2, viewModel.TimelineSlots.Count(x => x.ProgressPercent == 100));
        Assert.Single(viewModel.Entries);
    }

    [Fact]
    public async Task QuickLogPersistsPrivateActivityAndLinksCourseTopicAndTask()
    {
        var data = Data();
        var viewModel = CreateViewModel(data, out var store);
        viewModel.SelectedCourse = data.Courses.Single();
        viewModel.SelectedSyllabusTopic = data.SyllabusTopics.Single();
        viewModel.SelectedTask = data.StudyTasks.Single();
        viewModel.TitleInput = "Ripasso dei limiti";
        viewModel.StartTimeInput = "09:00";
        viewModel.EndTimeInput = "09:45";
        viewModel.NoteInput = "Esercizi svolti sul quaderno";

        await viewModel.AddQuickLogAsync();

        var activity = Assert.Single(data.StudyActivities);
        Assert.Equal(StudyActivityType.Study, activity.Type);
        Assert.Equal(StudyActivityOrigin.Manual, activity.Origin);
        Assert.Equal("analisi", activity.CourseId);
        Assert.Equal("limiti", activity.SyllabusTopicId);
        Assert.Equal("esercizi-limiti", activity.TaskId);
        Assert.Equal(2700, activity.EffectiveSeconds);
        Assert.True(activity.IsPrivate);
        Assert.Equal(2700, new ActivityLedgerService().GetTaskProgress(data, "esercizi-limiti", At(12)).RecordedSeconds);
        Assert.Single(data.ActivityRevisions);
        Assert.Equal(1, store.SaveCount);
        Assert.Contains("nota è privata", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Nota privata", Assert.Single(viewModel.Entries).PrivacyDisplay);
    }

    [Fact]
    public async Task QuickLogRejectsInvalidOrReversedTimesWithoutChangingData()
    {
        var data = Data();
        var viewModel = CreateViewModel(data, out var store);
        viewModel.TitleInput = "Studio";
        viewModel.StartTimeInput = "10:00";
        viewModel.EndTimeInput = "09:00";

        await viewModel.AddQuickLogAsync();

        Assert.Empty(data.StudyActivities);
        Assert.Empty(data.ActivityRevisions);
        Assert.Equal(0, store.SaveCount);
        Assert.Contains("successiva", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActivityFilterShowsOnlyTheSelectedType()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Teoria", 9, 0, 10, 0);
        AddActivity(data, StudyActivityType.Review, "Ripasso", 10, 0, 10, 30);
        var viewModel = CreateViewModel(data, out _);

        viewModel.SelectedActivityFilter = viewModel.ActivityFilterOptions.Single(x => x.Type == StudyActivityType.Review);

        var entry = Assert.Single(viewModel.Entries);
        Assert.Equal("Ripasso", entry.Title);
        Assert.Equal("Ripasso", entry.TypeDisplay);
    }

    [Fact]
    public void SlotSizeChangesTimelineGranularity()
    {
        var viewModel = CreateViewModel(Data(), out _);

        Assert.Equal(48, viewModel.TimelineSlots.Count);
        viewModel.SlotMinutes = 15;
        Assert.Equal(96, viewModel.TimelineSlots.Count);
        viewModel.SlotMinutes = 60;
        Assert.Equal(24, viewModel.TimelineSlots.Count);
    }

    [Fact]
    public void SelectingAnotherDayAlsoUpdatesTheQuickLogDate()
    {
        var viewModel = CreateViewModel(Data(), out _);
        var nextDay = Day.AddDays(1);

        viewModel.SelectedDay = nextDay;

        Assert.Equal(nextDay, viewModel.EntryDateInput);
        Assert.Equal(nextDay.ToString("dddd d MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("it-IT")),
            viewModel.SelectedDayLabel);
    }

    [Fact]
    public void OverlappingActivitiesAreCountedOnceAndReported()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Prima sessione", 9, 0, 10, 0);
        AddActivity(data, StudyActivityType.Review, "Seconda sessione", 9, 30, 10, 30);
        var viewModel = CreateViewModel(data, out _);

        Assert.Equal("1 h 30 min", viewModel.RecordedDisplay);
        Assert.Equal("30 min", viewModel.OverlapDisplay);
        Assert.True(viewModel.HasOverlaps);
        Assert.Contains("conteggiato una sola volta", viewModel.OverlapStatus, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, viewModel.Entries.Count);
        Assert.All(viewModel.Entries, entry => Assert.True(entry.HasOverlap));
    }

    [Fact]
    public async Task DeletingManualActivityKeepsAnAuditableRevision()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Voce errata", 9, 0, 10, 0);
        var viewModel = CreateViewModel(data, out var store);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        viewModel.DeleteReasonInput = string.Empty;
        Assert.False(viewModel.CanDeleteSelectedEntry);

        viewModel.DeleteReasonInput = "Inserimento duplicato";
        Assert.True(viewModel.CanDeleteSelectedEntry);
        await viewModel.DeleteSelectedActivityAsync();

        Assert.True(Assert.Single(data.StudyActivities).IsDeleted);
        Assert.Equal(2, data.ActivityRevisions.Count);
        Assert.Equal(ActivityRevisionAction.Deleted, data.ActivityRevisions[^1].Action);
        Assert.Equal("Inserimento duplicato", data.ActivityRevisions[^1].Reason);
        Assert.Empty(viewModel.Entries);
        Assert.Equal(1, store.SaveCount);
    }

    [Fact]
    public async Task TimerActivityCannotBeRemovedByManualCorrection()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", "Sessione timer", 25, At(9));
        focus.Finish(data, session.Id, "Completata", At(9).AddMinutes(25));
        ledger.SyncFocusSession(data, session, At(10));

        var viewModel = CreateViewModel(data, out var store);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);

        Assert.False(viewModel.CanDeleteSelectedEntry);
        await viewModel.DeleteSelectedActivityAsync();

        Assert.False(Assert.Single(data.StudyActivities).IsDeleted);
        Assert.Equal(0, store.SaveCount);
    }

    [Fact]
    public async Task FailedQuickLogSaveRollsBackActivityAndRevision()
    {
        var data = Data();
        var viewModel = CreateViewModel(data, out var store);
        store.FailNextSave = true;
        viewModel.TitleInput = "Attività non persistita";
        viewModel.StartTimeInput = "09:00";
        viewModel.EndTimeInput = "09:30";

        await viewModel.AddQuickLogAsync();

        Assert.Empty(data.StudyActivities);
        Assert.Empty(data.ActivityRevisions);
        Assert.Equal(0, store.SaveCount);
        Assert.Contains("non registrata", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FailedSoftDeleteSaveRestoresActivityAndRevisionHistory()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Voce da conservare", 9, 0, 10, 0);
        var viewModel = CreateViewModel(data, out var store);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);
        store.FailNextSave = true;

        await viewModel.DeleteSelectedActivityAsync();

        Assert.False(Assert.Single(data.StudyActivities).IsDeleted);
        Assert.Single(data.ActivityRevisions);
        Assert.Equal(ActivityRevisionAction.Created, data.ActivityRevisions[0].Action);
        Assert.Equal(0, store.SaveCount);
        Assert.Contains("non rimossa", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SelectingTaskAlignsCourseAndRestrictsLinkedTopics()
    {
        var data = Data();
        data.Courses.Add(new Course { Id = "fisica", Name = "Fisica", Cfu = 6 });
        data.SyllabusTopics.Add(new SyllabusTopicData { Id = "moto", CourseId = "fisica", Title = "Moto" });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "task-fisica",
            CourseId = "fisica",
            Title = "Esercizi di moto",
            Status = StudyTaskStatus.InProgress
        });
        var viewModel = CreateViewModel(data, out _);

        viewModel.SelectedTask = data.StudyTasks.Single(x => x.Id == "task-fisica");

        Assert.Equal("fisica", viewModel.SelectedCourse?.Id);
        Assert.Single(viewModel.SyllabusTopics);
        Assert.Equal("moto", viewModel.SyllabusTopics[0].Id);
        Assert.All(viewModel.AvailableTasks, task => Assert.True(string.IsNullOrWhiteSpace(task.CourseId) || task.CourseId == "fisica"));
    }

    [Fact]
    public void EmptyStateExplainsWhenTheSelectedFilterHasNoMatches()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Teoria", 9, 0, 10, 0);
        var viewModel = CreateViewModel(data, out _);

        viewModel.SelectedActivityFilter = viewModel.ActivityFilterOptions.Single(x => x.Type == StudyActivityType.Review);

        Assert.Empty(viewModel.Entries);
        Assert.Contains("filtro", viewModel.EmptyEntriesMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuickLogOptionsAreLocalizedAndLimitedToSupportedTypes()
    {
        var viewModel = CreateViewModel(Data(), out _);

        Assert.Equal(
            new[] { "Studio", "Ripasso", "Quiz", "Esercizi", "Lettura", "Simulazione" },
            viewModel.QuickLogTypeOptions.Select(x => x.Label));
        Assert.DoesNotContain(viewModel.QuickLogTypeOptions, x => x.Type == StudyActivityType.Lecture);
    }

    [Fact]
    public void RefreshFailureIsReportedWithoutEscapingTheViewModel()
    {
        var viewModel = CreateViewModel(Data(), out _);

        viewModel.SelectedDay = DateTime.MaxValue.Date;

        Assert.Contains("Diario non aggiornato", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshSynchronizesCoursesTopicsAndTasksAddedByTheWorkspace()
    {
        var data = Data();
        var viewModel = CreateViewModel(data, out _);
        var newCourse = new Course { Id = "fisica", Name = "Fisica", Cfu = 6 };
        var newTopic = new SyllabusTopicData { Id = "moto", CourseId = "fisica", Title = "Moto" };
        var newTask = new StudyTaskData
        {
            Id = "task-fisica",
            CourseId = "fisica",
            Title = "Esercizi di moto",
            Status = StudyTaskStatus.Todo
        };
        data.Courses.Add(newCourse);
        data.SyllabusTopics.Add(newTopic);
        data.StudyTasks.Add(newTask);

        viewModel.Refresh();

        Assert.Contains(viewModel.Courses, course => course.Id == "fisica");
        viewModel.SelectedCourse = newCourse;
        Assert.Contains(viewModel.SyllabusTopics, topic => topic.Id == "moto");
        Assert.Contains(viewModel.AvailableTasks, task => task.Id == "task-fisica");
    }

    [Fact]
    public void DayNavigationAtDateBoundaryReportsTheLimit()
    {
        var viewModel = CreateViewModel(Data(), out _);
        viewModel.SelectedDay = DateTime.MaxValue.Date;

        viewModel.NextDayCommand.Execute(null);

        Assert.Equal(DateTime.MaxValue.Date, viewModel.SelectedDay);
        Assert.Contains("Limite del calendario", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PausedFocusSessionIsLabeledAsPausedAndCreditsOnlyFocusTime()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", "Sessione in pausa", 25, At(9));
        focus.Pause(data, session.Id, At(9).AddMinutes(10));
        ledger.SyncFocusSession(data, session, At(9).AddMinutes(10));
        var viewModel = CreateViewModel(data, out _);

        var entry = Assert.Single(viewModel.Entries);

        Assert.EndsWith("in pausa", entry.TimeRangeDisplay, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("10 min", entry.DurationDisplay);
    }

    [Fact]
    public void RefreshKeepsSelectedEntryBoundToTheCurrentCollectionItem()
    {
        var data = Data();
        AddActivity(data, StudyActivityType.Study, "Voce selezionata", 9, 0, 10, 0);
        var viewModel = CreateViewModel(data, out _);
        viewModel.SelectedEntry = Assert.Single(viewModel.Entries);

        viewModel.Refresh();

        Assert.Same(viewModel.Entries.Single(), viewModel.SelectedEntry);
    }

    [Fact]
    public void InitializeRebindsSelectedCourseTopicAndTaskAfterWorkspaceReload()
    {
        var originalData = Data();
        var viewModel = CreateViewModel(originalData, out _);
        viewModel.SelectedSyllabusTopic = originalData.SyllabusTopics.Single();
        viewModel.SelectedTask = originalData.StudyTasks.Single();
        var reloadedData = Data();

        viewModel.Initialize(reloadedData);

        Assert.Same(reloadedData.Courses.Single(), viewModel.SelectedCourse);
        Assert.Same(reloadedData.SyllabusTopics.Single(), viewModel.SelectedSyllabusTopic);
        Assert.Same(reloadedData.StudyTasks.Single(), viewModel.SelectedTask);
    }

    [Fact]
    public async Task QuickLogNavigatesToItsSelectedEntryDate()
    {
        var data = Data();
        var viewModel = CreateViewModel(data, out _);
        viewModel.EntryDateInput = Day.AddDays(1);
        viewModel.TitleInput = "Studio del giorno successivo";
        viewModel.StartTimeInput = "09:00";
        viewModel.EndTimeInput = "10:00";

        await viewModel.AddQuickLogAsync();

        Assert.Equal(Day.AddDays(1), viewModel.SelectedDay);
        Assert.Single(viewModel.Entries);
        Assert.Equal("Studio del giorno successivo", viewModel.Entries[0].Title);
    }

    private sealed class MemoryStore(StudyData initial) : IStudyDataStore
    {
        public StudyData Data { get; private set; } = initial;
        public int SaveCount { get; private set; }
        public bool FailNextSave { get; set; }

        public Task<StudyData> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Data);

        public Task SaveAsync(StudyData data, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FailNextSave)
            {
                FailNextSave = false;
                throw new IOException("Simulated storage failure.");
            }

            Data = data;
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
