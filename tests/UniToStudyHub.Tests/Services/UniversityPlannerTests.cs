using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class UniversityPlannerTests
{
    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    [Fact]
    public void PlanRespectsPriorityAvailabilityBreaksAndReviewMargin()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi scritto",
            PlannedDate = new DateTime(2026, 11, 20)
        });
        data.SyllabusTopics.Add(Topic("hard", "Derivate", 60, 5, 1, 1));
        data.SyllabusTopics.Add(Topic("easy", "Limiti", 60, 2, 4, 2));
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "lecture-1",
            Title = "Lezione",
            Category = CalendarEventCategory.Lecture,
            StartsAt = AtLocal(2026, 11, 9, 18, 30),
            EndsAt = AtLocal(2026, 11, 9, 19, 30),
            TimeZoneId = TimeZoneInfo.Local.Id
        });
        data.PlannerSettings = Settings();

        var result = new UniversityPlannerService().CreatePlan(data, new PlannerRequest
        {
            CourseId = "analisi",
            ExamId = "exam-1"
        }, AtLocal(2026, 11, 9, 9, 0));

        Assert.True(result.IsFeasible);
        Assert.Equal(StudyPlanStatus.Active, result.Plan.Status);
        Assert.Equal(120, result.Plan.RequiredMinutes);
        Assert.Equal(24, result.Plan.ReviewReserveMinutes);
        Assert.Equal(144, result.Plan.ScheduledMinutes);
        Assert.Equal(144, result.Sessions.Sum(x => (int)(x.EndsAt - x.StartsAt).TotalMinutes));
        Assert.Equal("hard", result.Sessions[0].SyllabusTopicId);
        Assert.Contains(result.Sessions, x => x.Category == CalendarEventCategory.Review);
        Assert.All(result.Sessions, x =>
        {
            Assert.Equal(CalendarEventSource.Planner, x.Source);
            Assert.Equal(CalendarEventVerification.Indicative, x.Verification);
            Assert.Equal(TimeZoneInfo.Local.Id, x.TimeZoneId);
            Assert.True(x.StartsAt >= AtLocal(2026, 11, 9, 18, 0));
            Assert.True(x.EndsAt <= AtLocal(2026, 11, 9, 20, 0) ||
                        x.StartsAt.Date > new DateTime(2026, 11, 9));
        });
        Assert.DoesNotContain(result.Sessions, x =>
            x.StartsAt.Date == new DateTime(2026, 11, 18) || x.StartsAt.Date == new DateTime(2026, 11, 19));
        Assert.DoesNotContain(result.Sessions, x =>
            x.StartsAt < data.CalendarEvents[0].EndsAt && x.EndsAt > data.CalendarEvents[0].StartsAt);
    }

    [Fact]
    public void InsufficientTimeCreatesNoSessionsAndExplainsTheDeficit()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-tomorrow",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            PlannedDate = new DateTime(2026, 11, 10)
        });
        data.SyllabusTopics.Add(Topic("hard", "Tutto il programma", 120, 5, 1, 1));
        data.PlannerSettings = new PlannerSettingsData
        {
            PreferredSessionMinutes = 50,
            MinimumSessionMinutes = 25,
            BreakBetweenSessionsMinutes = 10,
            MaxSessionsPerDay = 2,
            ReviewReservePercent = 0,
            ReviewReserveDays = 0,
            Availability = new ObservableCollection<PlannerAvailabilityWindowData>
            {
                new() { DayOfWeek = DayOfWeek.Monday, StartsAt = new TimeOnly(18, 0), EndsAt = new TimeOnly(19, 0) }
            }
        };

        var result = new UniversityPlannerService().CreatePlan(data, new PlannerRequest
        {
            CourseId = "analisi",
            ExamId = "exam-tomorrow"
        }, AtLocal(2026, 11, 9, 9, 0));

        Assert.False(result.IsFeasible);
        Assert.Equal(StudyPlanStatus.InsufficientTime, result.Plan.Status);
        Assert.Empty(result.Sessions);
        Assert.Empty(data.CalendarEvents);
        Assert.Equal(70, result.DeficitMinutes);
        Assert.Contains(result.Plan.Warnings, x => x.Contains("Mancano 70 minuti", StringComparison.Ordinal));
    }

    [Fact]
    public void PriorityTieBreakPrefersTheHigherPriorityTopic()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            PlannedDate = new DateTime(2026, 11, 11)
        });
        data.SyllabusTopics.Add(Topic("priority-low", "Limiti", 30, 3, 3, 2, 1));
        data.SyllabusTopics.Add(Topic("priority-high", "Derivate", 30, 3, 3, 1, 5));
        data.PlannerSettings = Settings();
        data.PlannerSettings.PreferredSessionMinutes = 30;
        data.PlannerSettings.MinimumSessionMinutes = 25;
        data.PlannerSettings.MaxSessionsPerDay = 1;
        data.PlannerSettings.ReviewReservePercent = 0;
        data.PlannerSettings.ReviewReserveDays = 0;

        var result = new UniversityPlannerService().CreatePlan(data, new PlannerRequest
        {
            CourseId = "analisi",
            ExamId = "exam-1"
        }, AtLocal(2026, 11, 9, 9, 0));

        Assert.True(result.IsFeasible);
        Assert.Equal("priority-high", result.Sessions[0].SyllabusTopicId);
        Assert.Equal("priority-low", result.Sessions[1].SyllabusTopicId);
    }

    [Fact]
    public void MissedSessionsAreMovedWithHistoryAndCanBeReverted()
    {
        var data = PlannedData();
        var result = new UniversityPlannerService().CreatePlan(data, new PlannerRequest
        {
            CourseId = "analisi",
            ExamId = "exam-1"
        }, AtLocal(2026, 11, 9, 9, 0));
        Assert.True(result.IsFeasible);
        var first = result.Sessions[0];
        var originalStart = first.StartsAt;
        var originalEnd = first.EndsAt;
        var service = new UniversityPlannerService();

        var moved = service.RescheduleMissed(data, AtLocal(2026, 11, 10, 21, 0), apply: true);

        Assert.Equal(4, moved.Count);
        Assert.Equal(4, data.PlannerReschedules.Count);
        Assert.Equal(1, first.RescheduleCount);
        Assert.NotEqual(originalStart, first.StartsAt);
        Assert.All(data.PlannerReschedules, x => Assert.False(x.IsReversed));

        var history = data.PlannerReschedules[0];
        service.RevertReschedule(data, history.Id, AtLocal(2026, 11, 10, 22, 0));

        Assert.True(history.IsReversed);
        Assert.Equal(originalStart, first.StartsAt);
        Assert.Equal(originalEnd, first.EndsAt);
        Assert.Equal(PlannedSessionStatus.Missed, first.PlannerStatus);
    }

    [Fact]
    public void StartingAndFinishingFocusLinksThePlannedSessionWithoutDuplicatingTime()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            PlannedDate = new DateTime(2026, 11, 20)
        });
        data.SyllabusTopics.Add(Topic("topic-1", "Limiti", 50, 3, 3, 1));
        data.PlannerSettings = Settings();
        data.PlannerSettings.ReviewReservePercent = 0;
        var service = new UniversityPlannerService();
        var result = service.CreatePlan(data, new PlannerRequest
        {
            CourseId = "analisi",
            ExamId = "exam-1"
        }, AtLocal(2026, 11, 9, 9, 0));
        var planned = Assert.Single(result.Sessions);

        var focus = service.StartFocus(data, planned.Id, AtLocal(2026, 11, 9, 18, 0));
        Assert.Equal(planned.Id, focus.CalendarEventId);
        Assert.Equal("topic-1", focus.SyllabusTopicId);
        var finished = service.FinishFocus(data, focus.Id, "Studio", AtLocal(2026, 11, 9, 18, 30));

        Assert.Equal(StudySessionState.Completed, finished.State);
        Assert.Equal(PlannedSessionStatus.Completed, planned.PlannerStatus);
        Assert.Equal(30, planned.ActualMinutes);
        Assert.Single(data.StudyActivities);
        Assert.Equal(StudyActivityOrigin.Timer, data.StudyActivities[0].Origin);
        Assert.Equal(SyllabusTopicStatus.InProgress,
            data.SyllabusTopics.Single(x => x.Id == "topic-1").Status);
    }

    [Fact]
    public void TimelineClipsMidnightEventsAndKeepsFreeBlocksReal()
    {
        var data = Data();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "lecture",
            Title = "Lezione",
            Category = CalendarEventCategory.Lecture,
            StartsAt = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 11, 4, 11, 0, 0, TimeSpan.Zero),
            TimeZoneId = "UTC"
        });
        var night = new CalendarEvent
        {
            Id = "night",
            Title = "Studio notturno",
            Category = CalendarEventCategory.Study,
            StartsAt = new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(2026, 11, 5, 0, 30, 0, TimeSpan.Zero),
            TimeZoneId = "UTC"
        };
        data.CalendarEvents.Add(night);

        var timeline = new CalendarTimelineService().BuildDay(
            data,
            new DateOnly(2026, 11, 4),
            60,
            new DateTimeOffset(2026, 11, 4, 12, 0, 0, TimeSpan.Zero),
            "UTC");

        Assert.Equal(24, timeline.Slots.Count);
        Assert.Equal(2, timeline.Events.Count);
        Assert.Equal(150, timeline.BusyMinutes);
        Assert.Equal(1290, timeline.FreeMinutes);
        var clipped = Assert.Single(timeline.Events, x => x.Event.Id == "night");
        Assert.True(clipped.IsClippedAtEnd);
        Assert.Equal(30, clipped.DurationMinutes);
        Assert.True(timeline.Slots[23].HasEvents);
        Assert.Contains(timeline.FreeBlocks, x => x.Minutes == 750);
    }

    [Fact]
    public void AnalyticsSeparatesRecordedPlannedAndActualTime()
    {
        var data = Data();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "planned",
            Title = "Sessione pianificata",
            Category = CalendarEventCategory.Study,
            StartsAt = AtLocal(2026, 11, 9, 18, 0),
            EndsAt = AtLocal(2026, 11, 9, 18, 50),
            CourseId = "analisi",
            Source = CalendarEventSource.Planner,
            Verification = CalendarEventVerification.Indicative,
            PlanId = "plan-1",
            PlannerStatus = PlannedSessionStatus.Completed,
            ActualMinutes = 40,
            CompletedAt = AtLocal(2026, 11, 9, 18, 40),
            TimeZoneId = TimeZoneInfo.Local.Id
        });
        new ActivityLedgerService().AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Studio reale",
            CourseId = "analisi",
            StartedAt = AtLocal(2026, 11, 9, 9, 0),
            EndedAt = AtLocal(2026, 11, 9, 10, 0),
            TimeZoneId = TimeZoneInfo.Local.Id
        }, AtLocal(2026, 11, 9, 23, 0));

        var analytics = new StudyAnalyticsService().CalculateRange(
            data,
            new DateOnly(2026, 11, 9),
            new DateOnly(2026, 11, 9),
            AtLocal(2026, 11, 9, 23, 0));

        Assert.Equal(60, analytics.RecordedMinutes);
        Assert.Equal(50, analytics.PlannedMinutes);
        Assert.Equal(40, analytics.ActualPlannerMinutes);
        Assert.Equal(-10, analytics.PlannedVsActualMinutes);
        Assert.Equal(1, analytics.StudyDays);
        Assert.Equal(1, analytics.ElapsedDays);
        Assert.Equal(100m, analytics.ConsistencyPercent);
    }

    private static StudyData PlannedData()
    {
        var data = Data();
        data.Exams.Add(new Exam
        {
            Id = "exam-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            Name = "Analisi",
            PlannedDate = new DateTime(2026, 11, 20)
        });
        data.SyllabusTopics.Add(Topic("hard", "Derivate", 60, 5, 1, 1));
        data.SyllabusTopics.Add(Topic("easy", "Limiti", 60, 2, 4, 2));
        data.PlannerSettings = Settings();
        return data;
    }

    private static PlannerSettingsData Settings() => new()
    {
        PreferredSessionMinutes = 50,
        MinimumSessionMinutes = 25,
        BreakBetweenSessionsMinutes = 10,
        MaxSessionsPerDay = 2,
        ReviewReservePercent = 20,
        ReviewReserveDays = 2,
        Availability = new ObservableCollection<PlannerAvailabilityWindowData>(
            new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                DayOfWeek.Thursday, DayOfWeek.Friday
            }.Select(day => new PlannerAvailabilityWindowData
            {
                DayOfWeek = day,
                StartsAt = new TimeOnly(18, 0),
                EndsAt = new TimeOnly(20, 0)
            }))
    };

    private static SyllabusTopicData Topic(
        string id,
        string title,
        int minutes,
        int difficulty,
        int preparation,
        int position,
        int priority = 3) => new()
    {
        Id = id,
        CourseId = "analisi",
        Title = title,
        EstimatedMinutes = minutes,
        Difficulty = difficulty,
        PreparationLevel = preparation,
        Priority = priority,
        Position = position
    };

    private static DateTimeOffset AtLocal(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }
}
