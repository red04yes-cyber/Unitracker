using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyPlannerServiceTests
{
    private static readonly DateOnly Thursday = new(2026, 11, 5);
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        WeeklyStudyHours = 8,
        MaxDailyStudyMinutes = 240,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analysis", Name = "Analisi", Cfu = 9 }
        }
    };

    private static StudyPlannerOptions Options(
        DateOnly? from = null,
        DateOnly? to = null,
        IReadOnlyList<StudyAvailabilityWindow>? availability = null,
        int? weeklyMinutes = 240,
        int? dailyMinutes = 180,
        bool replace = false,
        int preferredBlockMinutes = 50,
        int breakMinutes = 10) => new()
    {
        From = from ?? Thursday,
        To = to ?? Thursday,
        TimeZoneId = "Europe/Rome",
        WeeklyAvailability = availability ?? new[]
        {
            new StudyAvailabilityWindow(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(13, 0))
        },
        WeeklyStudyMinutes = weeklyMinutes,
        MaxDailyStudyMinutes = dailyMinutes,
        PreferredBlockMinutes = preferredBlockMinutes,
        MinimumBlockMinutes = 15,
        BreakMinutes = breakMinutes,
        MaxBlocksPerDay = 4,
        ReplaceExistingGeneratedBlocks = replace
    };

    private static StudyTaskData AddTask(
        StudyData data,
        string id = "task-1",
        int minutes = 90,
        int priority = 3,
        DateTimeOffset? dueAt = null)
    {
        var task = new StudyTaskData
        {
            Id = id,
            CourseId = "analysis",
            Title = $"Studiare {id}",
            EstimatedMinutes = minutes,
            Priority = priority,
            DueAt = dueAt,
            Status = StudyTaskStatus.Todo,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        };
        data.StudyTasks.Add(task);
        return task;
    }

    private static StudyActivityData AddTaskActivity(
        StudyData data,
        string id,
        string taskId,
        DateTimeOffset startedAt,
        int seconds,
        StudyActivityStatus status = StudyActivityStatus.Completed,
        DateTimeOffset? deletedAt = null)
    {
        var activity = new StudyActivityData
        {
            Id = id,
            Type = StudyActivityType.Study,
            Title = "Studio registrato",
            CourseId = "analysis",
            TaskId = taskId,
            StartedAt = startedAt,
            EndedAt = startedAt.AddSeconds(seconds),
            EffectiveSeconds = seconds,
            TimeZoneId = "Europe/Rome",
            Origin = StudyActivityOrigin.Manual,
            Status = status,
            DeletedAt = deletedAt
        };
        data.StudyActivities.Add(activity);
        return activity;
    }

    [Fact]
    public void PreviewSplitsTasksAroundExistingCommitmentsWithoutMutatingData()
    {
        var data = Data();
        AddTask(data, minutes: 90, dueAt: Now.AddDays(1));
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "lecture",
            Title = "Lezione di Analisi",
            Category = CalendarEventCategory.Lecture,
            StartsAt = new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(1)),
            EndsAt = new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(1)),
            TimeZoneId = "Europe/Rome"
        });

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(90, preview.ScheduledMinutes);
        Assert.Equal(2, preview.ScheduledBlockCount);
        Assert.DoesNotContain(preview.Blocks, x =>
            x.StartsAt < new DateTimeOffset(2026, 11, 5, 11, 0, 0, TimeSpan.FromHours(1)) &&
            x.EndsAt > new DateTimeOffset(2026, 11, 5, 10, 0, 0, TimeSpan.FromHours(1)));
        Assert.Empty(preview.UnscheduledTasks);
        Assert.Single(data.CalendarEvents);
        Assert.Equal(StudyTaskStatus.Todo, Assert.Single(data.StudyTasks).Status);
    }

    [Fact]
    public void PreviewDoesNotInventAvailabilityAndReportsUnscheduledTasks()
    {
        var data = Data();
        AddTask(data, "one", 60);
        AddTask(data, "two", 30);
        var options = Options(availability: Array.Empty<StudyAvailabilityWindow>());

        var preview = new StudyPlannerService().CreatePreview(data, options, Now);

        Assert.Empty(preview.Blocks);
        Assert.Equal(2, preview.UnscheduledTasks.Count);
        Assert.Contains(preview.Warnings, x => x.Contains("non inventa fasce", StringComparison.Ordinal));
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void PreviewDeductsRecordedLedgerMinutesFromDailyAndWeeklyBudgets()
    {
        var data = Data();
        AddTask(data, minutes: 120);
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "recorded",
            Type = StudyActivityType.Study,
            Title = "Studio già svolto",
            CourseId = "analysis",
            StartedAt = new DateTimeOffset(2026, 11, 5, 7, 0, 0, TimeSpan.FromHours(1)),
            EndedAt = new DateTimeOffset(2026, 11, 5, 8, 0, 0, TimeSpan.FromHours(1)),
            EffectiveSeconds = 3600,
            TimeZoneId = "Europe/Rome",
            Status = StudyActivityStatus.Completed
        });

        var preview = new StudyPlannerService().CreatePreview(
            data,
            Options(weeklyMinutes: 90, dailyMinutes: 90),
            Now);

        Assert.Equal(30, preview.ScheduledMinutes);
        Assert.Equal(90, preview.WeeklyBudgetMinutes);
        Assert.Equal(90, preview.DailyBudgetMinutes);
        Assert.Equal(90, Assert.Single(preview.UnscheduledTasks).RemainingMinutes);
    }

    [Fact]
    public void PreviewHonorsDeadlineAndOrdersByDeadlineThenPriority()
    {
        var data = Data();
        AddTask(data, "low-priority", 30, priority: 1, dueAt: Now.AddDays(2));
        AddTask(data, "urgent", 30, priority: 5, dueAt: Now.AddHours(2));
        AddTask(data, "too-late", 30, priority: 5, dueAt: Now.AddMinutes(10));

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal("too-late", preview.UnscheduledTasks.Single().TaskId);
        Assert.Equal(new[] { "urgent", "low-priority" }, preview.Blocks.Select(x => x.TaskId).Distinct().ToArray());
        Assert.True(preview.Blocks.Single(x => x.TaskId == "urgent").EndsAt <= Now.AddHours(2));
    }

    [Fact]
    public void ApplyIsIdempotentAndKeepsPlannedTasksOpen()
    {
        var data = Data();
        var task = AddTask(data, minutes: 60);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);

        var first = service.Apply(data, preview, Now);
        var second = service.Apply(data, preview, Now.AddMinutes(1));

        Assert.Equal(preview.ScheduledBlockCount, first.Added);
        Assert.Equal(preview.ScheduledBlockCount, second.AlreadyPresent);
        Assert.Equal(preview.ScheduledBlockCount, data.CalendarEvents.Count);
        Assert.All(data.CalendarEvents, item =>
        {
            Assert.Equal(CalendarEventSource.Planner, item.Source);
            Assert.Equal(CalendarEventVerification.Indicative, item.Verification);
            Assert.Equal(CalendarEventCategory.Study, item.Category);
            Assert.StartsWith("study-plan:v1:", item.ExternalId, StringComparison.Ordinal);
        });
        Assert.Equal(StudyTaskStatus.Todo, task.Status);
    }

    [Fact]
    public void ApplyRechecksCalendarAndDoesNotOverwriteNewCommitments()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var firstBlock = Assert.Single(preview.Blocks);

        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "new-appointment",
            Title = "Nuovo impegno",
            Category = CalendarEventCategory.Personal,
            StartsAt = firstBlock.StartsAt,
            EndsAt = firstBlock.EndsAt,
            TimeZoneId = "Europe/Rome"
        });

        var result = service.Apply(data, preview, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Conflicts);
        Assert.Single(data.CalendarEvents);
        Assert.Equal("new-appointment", data.CalendarEvents[0].Id);
    }

    [Fact]
    public void ReplacePlanOnlyRemovesUnchangedPlannerBlocksAndPreservesManualEvents()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "manual",
            Title = "Impegno personale",
            Category = CalendarEventCategory.Personal,
            StartsAt = Now.AddHours(1),
            EndsAt = Now.AddHours(2),
            TimeZoneId = "Europe/Rome"
        });
        var service = new StudyPlannerService();
        var firstPreview = service.CreatePreview(data, Options(), Now);
        service.Apply(data, firstPreview, Now);

        var replacement = service.CreatePreview(data, Options(replace: true), Now.AddMinutes(1));
        var result = service.Apply(data, replacement, Now.AddMinutes(1));

        Assert.Equal(1, result.Added);
        Assert.Single(data.CalendarEvents, x => x.Id == "manual");
        Assert.Single(data.CalendarEvents, x => x.ExternalId?.StartsWith("study-plan:v1:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ReplacePlanPreservesUserEditedGeneratedEvents()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var firstPreview = service.CreatePreview(data, Options(), Now);
        service.Apply(data, firstPreview, Now);
        var generated = Assert.Single(data.CalendarEvents);
        generated.Title = "Blocco spostato e rinominato dall'utente";
        generated.StartsAt = generated.StartsAt.AddHours(1);
        generated.EndsAt = generated.EndsAt.AddHours(1);
        generated.UpdatedAt = Now.AddMinutes(1);

        var replacement = service.CreatePreview(data, Options(replace: true), Now.AddMinutes(2));
        service.Apply(data, replacement, Now.AddMinutes(2));

        Assert.Equal(2, data.CalendarEvents.Count);
        Assert.Contains(data.CalendarEvents, x =>
            x.Id == generated.Id && x.Title == "Blocco spostato e rinominato dall'utente");
        Assert.Single(data.CalendarEvents, x => x.Id != generated.Id &&
            x.ExternalId?.StartsWith("study-plan:v1:", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task ApplyAndSavePersistsIndicativePlanBlocksThroughSqlite()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);

        var result = await service.ApplyAndSaveAsync(data, preview, store, Now);
        var loaded = await store.LoadAsync();

        Assert.Equal(1, result.Added);
        var item = Assert.Single(loaded.CalendarEvents);
        Assert.Equal(CalendarEventSource.Planner, item.Source);
        Assert.Equal(CalendarEventVerification.Indicative, item.Verification);
        Assert.Equal("task-1", item.ExternalId!.Split(':')[2]);
        Assert.Single(loaded.StudyTasks);
        Assert.Equal(StudyTaskStatus.Todo, loaded.StudyTasks[0].Status);
    }

    [Fact]
    public void AmbiguousAutumnHourUsesEarlierStartAndLaterEndToAvoidLosingAvailability()
    {
        var data = Data();
        AddTask(data, minutes: 120);
        var sunday = new DateOnly(2026, 10, 25);
        var options = Options(
            from: sunday,
            to: sunday,
            availability: new[]
            {
                new StudyAvailabilityWindow(DayOfWeek.Sunday, new TimeOnly(2, 0), new TimeOnly(3, 0))
            },
            preferredBlockMinutes: 120,
            breakMinutes: 0);

        var preview = new StudyPlannerService().CreatePreview(
            data,
            options,
            new DateTimeOffset(2026, 10, 24, 22, 0, 0, TimeSpan.Zero));

        var block = Assert.Single(preview.Blocks);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome");
        var localEnd = TimeZoneInfo.ConvertTime(block.EndsAt, zone);
        Assert.Equal(TimeSpan.FromHours(2), block.StartsAt.Offset);
        Assert.Equal(new TimeOnly(3, 0), TimeOnly.FromDateTime(localEnd.DateTime));
        Assert.Equal(TimeSpan.FromHours(1), localEnd.Offset);
        Assert.Equal(TimeSpan.FromHours(2), block.EndsAt - block.StartsAt);
    }

    [Fact]
    public void InvalidSpringForwardAvailabilityIsAdjustedAndDocumented()
    {
        var data = Data();
        AddTask(data, minutes: 30);
        var sunday = new DateOnly(2026, 3, 29);
        var options = Options(
            from: sunday,
            to: sunday,
            availability: new[]
            {
                new StudyAvailabilityWindow(DayOfWeek.Sunday, new TimeOnly(2, 30), new TimeOnly(4, 0))
            });

        var preview = new StudyPlannerService().CreatePreview(
            data,
            options,
            new DateTimeOffset(2026, 3, 28, 22, 0, 0, TimeSpan.Zero));

        var block = Assert.Single(preview.Blocks);
        Assert.Equal(new TimeOnly(3, 0), TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(block.StartsAt,
            TimeZoneInfo.FindSystemTimeZoneById("Europe/Rome")).DateTime));
        Assert.Contains(preview.Warnings, x => x.Contains("cambio di orario legale/solare", StringComparison.Ordinal));
    }

    [Fact]
    public void ReplacePlanPreservesExistingBlocksWhenReplacementConflicts()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var initial = service.CreatePreview(data, Options(), Now);
        service.Apply(data, initial, Now);
        var previous = Assert.Single(data.CalendarEvents);

        var replacement = service.CreatePreview(data, Options(replace: true), Now.AddMinutes(1));
        var proposed = Assert.Single(replacement.Blocks);
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "new-manual-conflict",
            Title = "Nuovo impegno",
            Category = CalendarEventCategory.Personal,
            StartsAt = proposed.StartsAt,
            EndsAt = proposed.EndsAt,
            TimeZoneId = "Europe/Rome"
        });

        var result = service.Apply(data, replacement, Now.AddMinutes(2));

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Conflicts);
        Assert.Contains(data.CalendarEvents, item => item.Id == previous.Id);
        Assert.Contains(data.CalendarEvents, item => item.Id == "new-manual-conflict");
        Assert.Equal(2, data.CalendarEvents.Count);
    }

    [Fact]
    public void ReplacePlanPreservesExistingBlocksWhenTaskClosesAfterPreview()
    {
        var data = Data();
        var task = AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var initial = service.CreatePreview(data, Options(), Now);
        service.Apply(data, initial, Now);
        var previous = Assert.Single(data.CalendarEvents);

        var replacement = service.CreatePreview(data, Options(replace: true), Now.AddMinutes(1));
        task.Status = StudyTaskStatus.Completed;

        var result = service.Apply(data, replacement, Now.AddMinutes(2));

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Contains(data.CalendarEvents, item => item.Id == previous.Id);
        Assert.Single(data.CalendarEvents);
    }

    [Fact]
    public void ReplacePlanPreservesExistingBlocksWhenPreviewIsEmpty()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var initial = service.CreatePreview(data, Options(), Now);
        service.Apply(data, initial, Now);
        var previous = Assert.Single(data.CalendarEvents);

        var emptyReplacement = service.CreatePreview(
            data,
            Options(availability: Array.Empty<StudyAvailabilityWindow>(), replace: true),
            Now.AddMinutes(1));
        Assert.Empty(emptyReplacement.Blocks);

        var result = service.Apply(data, emptyReplacement, Now.AddMinutes(2));

        Assert.Equal(0, result.Added);
        Assert.Contains(result.Warnings, warning => warning.Contains("piano esistente è stato conservato", StringComparison.Ordinal));
        Assert.Contains(data.CalendarEvents, item => item.Id == previous.Id);
        Assert.Single(data.CalendarEvents);
    }

    [Fact]
    public void ReplacePlanIsAtomicWhenAnyReplacementBlockConflicts()
    {
        var data = Data();
        AddTask(data, "first", 50);
        AddTask(data, "second", 50);
        var service = new StudyPlannerService();
        var initial = service.CreatePreview(data, Options(), Now);
        Assert.Equal(2, initial.Blocks.Count);
        service.Apply(data, initial, Now);
        var previousIds = data.CalendarEvents.Select(item => item.Id).ToHashSet(StringComparer.Ordinal);

        var replacement = service.CreatePreview(data, Options(replace: true), Now.AddMinutes(1));
        var conflictingBlock = replacement.Blocks[1];
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "new-manual-conflict",
            Title = "Nuovo impegno",
            Category = CalendarEventCategory.Personal,
            StartsAt = conflictingBlock.StartsAt,
            EndsAt = conflictingBlock.EndsAt,
            TimeZoneId = "Europe/Rome"
        });

        var result = service.Apply(data, replacement, Now.AddMinutes(2));

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(1, result.Skipped);
        Assert.All(previousIds, id => Assert.Contains(data.CalendarEvents, item => item.Id == id));
        Assert.Equal(3, data.CalendarEvents.Count);
    }

    [Fact]
    public void ApplyRejectsPreviewMetadataThatDoesNotMatchOptions()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now) with { TimeZoneId = "UTC" };

        Assert.Throws<ArgumentException>(() => service.Apply(data, preview, Now));
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplySkipsBlocksOutsideThePreviewDateRange()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var block = Assert.Single(preview.Blocks);
        var outside = block with
        {
            StartsAt = block.StartsAt.AddDays(-1),
            EndsAt = block.EndsAt.AddDays(-1)
        };

        var result = service.Apply(data, preview with { Blocks = new[] { outside } }, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplySkipsBlocksWhoseDurationDoesNotMatchTheirPlannedMinutes()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var block = Assert.Single(preview.Blocks);

        var result = service.Apply(
            data,
            preview with { Blocks = new[] { block with { EndsAt = block.EndsAt.AddMinutes(-1) } } },
            Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplySkipsBlocksWhenTaskTitleChangesAfterPreview()
    {
        var data = Data();
        var task = AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        task.Title = "Titolo aggiornato";

        var result = service.Apply(data, preview, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
        Assert.Contains(result.Warnings, warning => warning.Contains("cambiato dopo l'anteprima", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplySkipsBlocksWhenTaskEstimateIsReducedAfterPreview()
    {
        var data = Data();
        var task = AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        task.EstimatedMinutes = 30;

        var result = service.Apply(data, preview, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
        Assert.Contains(result.Warnings, warning => warning.Contains("stima del task", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplySkipsBlocksWhenTaskDeadlineChangesAfterPreview()
    {
        var data = Data();
        var task = AddTask(data, minutes: 50, dueAt: Now.AddDays(1));
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        task.DueAt = Now.AddHours(2);

        var result = service.Apply(data, preview, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplySkipsBlocksThatHaveAlreadyStarted()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);

        var result = service.Apply(data, preview, Now.AddHours(2));

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplyUsesCurrentCourseNameInsteadOfStalePreviewText()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        data.Courses.Single().Name = "Analisi matematica";

        var result = service.Apply(data, preview, Now);

        Assert.Equal(1, result.Added);
        Assert.Equal("Analisi matematica", Assert.Single(data.CalendarEvents).CourseName);
    }

    [Fact]
    public void ClearGeneratedPlanRejectsDateOnlyMaxValueWithoutChangingCalendar()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        service.Apply(data, preview, Now);
        var existingId = Assert.Single(data.CalendarEvents).Id;

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ClearGeneratedPlan(data, DateOnly.MaxValue, DateOnly.MaxValue, "Europe/Rome"));

        Assert.Contains(data.CalendarEvents, item => item.Id == existingId);
        Assert.Single(data.CalendarEvents);
    }


    [Fact]
    public void ApplySkipsNullBlocksInMalformedPreview()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now) with
        {
            Blocks = new StudyPlanBlock[] { null! }
        };

        var result = service.Apply(data, preview, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
    }


    [Fact]
    public void PreviewSchedulesOnlyTheUnrecordedRemainderOfATask()
    {
        var data = Data();
        AddTask(data, minutes: 90);
        AddTaskActivity(data, "recorded-60", "task-1", Now.AddHours(-1), 3600);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(30, preview.ScheduledMinutes);
        var progress = Assert.Single(preview.TaskProgress);
        Assert.Equal(60, progress.RecordedMinutes);
        Assert.Equal(30, progress.RemainingMinutes);
        Assert.Equal(30L, preview.RemainingTaskMinutes);
    }

    [Fact]
    public void PreviewRoundsPartialRemainingMinutesUp()
    {
        var data = Data();
        AddTask(data, minutes: 60);
        AddTaskActivity(data, "recorded-partial", "task-1", Now.AddSeconds(-1530), 1530);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(35, preview.ScheduledMinutes);
        Assert.Equal(35, Assert.Single(preview.TaskProgress).RemainingMinutes);
    }

    [Fact]
    public void PreviewCountsOverlappingTaskActivitiesOnlyOnce()
    {
        var data = Data();
        AddTask(data, minutes: 120);
        AddTaskActivity(data, "first-half", "task-1", Now.AddHours(-2), 1800);
        AddTaskActivity(data, "overlapping-half", "task-1", Now.AddMinutes(-105), 1800);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        var progress = Assert.Single(preview.TaskProgress);
        Assert.Equal(45, progress.RecordedMinutes);
        Assert.Equal(75, progress.RemainingMinutes);
        Assert.Equal(75, preview.ScheduledMinutes);
    }

    [Fact]
    public void PreviewDoesNotDeductTimeRecordedAgainstAnotherTask()
    {
        var data = Data();
        AddTask(data, "task-1", 60);
        AddTask(data, "task-2", 60);
        AddTaskActivity(data, "task-2-study", "task-2", Now.AddMinutes(-30), 1800);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(60, preview.TaskProgress.Single(x => x.TaskId == "task-1").RemainingMinutes);
        Assert.Equal(30, preview.TaskProgress.Single(x => x.TaskId == "task-2").RemainingMinutes);
        Assert.Equal(90, preview.ScheduledMinutes);
    }

    [Fact]
    public void PreviewIgnoresSoftDeletedTaskActivities()
    {
        var data = Data();
        AddTask(data, minutes: 60);
        AddTaskActivity(
            data, "deleted-study", "task-1", Now.AddMinutes(-30), 1800,
            deletedAt: Now.AddMinutes(-1));

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(60, preview.ScheduledMinutes);
        Assert.Equal(0, Assert.Single(preview.TaskProgress).RecordedSeconds);
    }

    [Fact]
    public void PreviewIgnoresCancelledManualTaskActivities()
    {
        var data = Data();
        AddTask(data, minutes: 60);
        AddTaskActivity(
            data, "cancelled-manual", "task-1", Now.AddMinutes(-30), 1800,
            status: StudyActivityStatus.Cancelled);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(60, preview.ScheduledMinutes);
        Assert.Equal(0, Assert.Single(preview.TaskProgress).RecordedSeconds);
    }

    [Fact]
    public void PreviewWarnsWhenRecordedWorkExhaustsAnOpenTaskEstimate()
    {
        var data = Data();
        AddTask(data, minutes: 60);
        AddTaskActivity(data, "estimate-used", "task-1", Now.AddSeconds(-3900), 3900);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Empty(preview.Blocks);
        Assert.Equal(0, Assert.Single(preview.UnscheduledTasks).RemainingMinutes);
        Assert.Contains(preview.Warnings, x => x.Contains("supera la stima", StringComparison.Ordinal));
        Assert.True(Assert.Single(preview.TaskProgress).IsOverEstimate);
    }

    [Fact]
    public void PreviewExposesRecordedAndRemainingTaskTotalsIncludingUnestimatedTasks()
    {
        var data = Data();
        AddTask(data, "estimated", 60);
        AddTask(data, "unestimated", 0);
        AddTaskActivity(data, "recorded-30", "estimated", Now.AddMinutes(-30), 1800);

        var preview = new StudyPlannerService().CreatePreview(data, Options(), Now);

        Assert.Equal(2, preview.TaskProgress.Count);
        Assert.Equal(30L, preview.RecordedTaskMinutes);
        Assert.Equal(30L, preview.RemainingTaskMinutes);
        Assert.Contains(preview.UnscheduledTasks, x =>
            x.TaskId == "unestimated" && x.Reason.Contains("Manca una stima", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRejectsPreviewWhenTaskProgressChangesAfterGeneration()
    {
        var data = Data();
        AddTask(data, minutes: 90);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        AddTaskActivity(data, "new-study", "task-1", Now.AddMinutes(-30), 1800);

        var result = service.Apply(data, preview, Now.AddMinutes(1));

        Assert.Equal(0, result.Added);
        Assert.Equal(preview.Blocks.Count, result.Skipped);
        Assert.Empty(data.CalendarEvents);
        Assert.Contains(result.Warnings, x => x.Contains("tempo registrato è cambiato", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRejectsBlocksThatExceedTheCurrentRemainingTaskEstimate()
    {
        var data = Data();
        AddTask(data, minutes: 60);
        AddTaskActivity(data, "already-studied", "task-1", Now.AddMinutes(-30), 1800);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var block = Assert.Single(preview.Blocks);
        var oversized = block with
        {
            PlannedMinutes = block.PlannedMinutes + 1,
            EndsAt = block.EndsAt.AddMinutes(1)
        };

        var result = service.Apply(data, preview with { Blocks = new[] { oversized } }, Now);

        Assert.Equal(0, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Empty(data.CalendarEvents);
        Assert.Contains(result.Warnings, x => x.Contains("stima residua", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviewRejectsNullAvailabilityWindows()
    {
        var data = Data();
        var options = Options(availability: new StudyAvailabilityWindow[] { null! });

        Assert.Throws<ArgumentException>(() =>
            new StudyPlannerService().CreatePreview(data, options, Now));
    }

    [Fact]
    public void PreviewRejectsExcessiveAvailabilityWindowCounts()
    {
        var data = Data();
        var windows = Enumerable.Repeat(
            new StudyAvailabilityWindow(DayOfWeek.Thursday, new TimeOnly(9, 0), new TimeOnly(13, 0)),
            65).ToArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StudyPlannerService().CreatePreview(data, Options(availability: windows), Now));
    }

    [Fact]
    public void PreviewClampsCorruptedPersistedStudyBudgetsAndExplainsTheLimit()
    {
        var data = Data();
        data.WeeklyStudyHours = int.MaxValue;
        data.MaxDailyStudyMinutes = int.MaxValue;
        AddTask(data, minutes: 30);

        var preview = new StudyPlannerService().CreatePreview(
            data,
            Options(weeklyMinutes: null, dailyMinutes: null),
            Now);

        Assert.Equal(10080, preview.WeeklyBudgetMinutes);
        Assert.Equal(1440, preview.DailyBudgetMinutes);
        Assert.Contains(preview.Warnings, x => x.Contains("ore settimanali salvate", StringComparison.Ordinal));
        Assert.Contains(preview.Warnings, x => x.Contains("limite giornaliero salvato", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyRejectsPreviewWithMoreThanTheMaximumPossibleBlockCount()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var block = Assert.Single(preview.Blocks);
        var malformed = preview with { Blocks = Enumerable.Repeat(block, 3621).ToArray() };

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Apply(data, malformed, Now));
        Assert.Empty(data.CalendarEvents);
    }

    [Fact]
    public void ApplySupportsLegacyPreviewsWithoutTaskProgressSnapshots()
    {
        var data = Data();
        AddTask(data, minutes: 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now) with
        {
            TaskProgress = Array.Empty<TaskProgressSnapshot>(),
            HasTaskProgressSnapshot = false
        };

        var result = service.Apply(data, preview, Now);

        Assert.Equal(1, result.Added);
        Assert.Single(data.CalendarEvents);
    }


    [Fact]
    public void ApplyRejectsBlocksMissingProgressRowsWhenUsingTheNewPreviewContract()
    {
        var data = Data();
        AddTask(data, "task-1", 50);
        AddTask(data, "task-2", 50);
        var service = new StudyPlannerService();
        var preview = service.CreatePreview(data, Options(), Now);
        var incompleteProgress = preview.TaskProgress
            .Where(x => x.TaskId == "task-1")
            .ToArray();

        var result = service.Apply(
            data,
            preview with { TaskProgress = incompleteProgress },
            Now);

        Assert.Equal(1, result.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Single(data.CalendarEvents);
        Assert.Contains(result.Warnings, x => x.Contains("non contiene il progresso aggiornato", StringComparison.Ordinal));
    }

}
