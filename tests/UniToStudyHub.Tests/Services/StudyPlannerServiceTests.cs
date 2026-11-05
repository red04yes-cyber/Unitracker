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
        Assert.Single(data.CalendarEvents.Where(x => x.Id == "manual"));
        Assert.Single(data.CalendarEvents.Where(x => x.ExternalId?.StartsWith("study-plan:v1:", StringComparison.Ordinal) == true));
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

        Assert.Single(data.CalendarEvents);
        Assert.Equal("Blocco spostato e rinominato dall'utente", data.CalendarEvents[0].Title);
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
        Assert.Equal(TimeSpan.FromHours(2), block.StartsAt.Offset);
        Assert.Equal(TimeSpan.FromHours(1), block.EndsAt.Offset);
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
}
