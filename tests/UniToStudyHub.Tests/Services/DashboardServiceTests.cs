using System.Collections.ObjectModel;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class DashboardServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);

    private static StudyData Data() => new()
    {
        WeeklyStudyHours = 10,
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analysis", Name = "Analisi", Cfu = 9 },
            new() { Id = "programming", Name = "Programmazione", Cfu = 6 }
        }
    };

    [Fact]
    public void BuildPrefersOfficialCalendarExamAndDeduplicatesIndicativeExam()
    {
        var data = Data();
        var examDate = Now.AddDays(4);
        data.Exams.Add(new Exam
        {
            Id = "planned-1",
            CourseId = "analysis",
            CourseName = "Analisi",
            Name = "Appello Analisi",
            PlannedDate = examDate.Date
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "official-1",
            Title = "Appello Analisi",
            Category = CalendarEventCategory.Exam,
            CourseId = "analysis",
            CourseName = "Analisi",
            StartsAt = examDate,
            EndsAt = examDate.AddHours(2),
            Verification = CalendarEventVerification.Official,
            TimeZoneId = "UTC"
        });

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        var exam = Assert.Single(snapshot.Exams);
        Assert.True(exam.IsOfficial);
        Assert.Equal("Appello ufficiale", exam.VerificationLabel);
        Assert.Equal("Tra 4 giorni", exam.CountdownText);
    }

    [Fact]
    public void BuildShowsOnlyOpenTasksDueTodayOrEarlierAndUpcomingDeadlines()
    {
        var data = Data();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "overdue",
            Title = "Recuperare esercizi",
            CourseId = "analysis",
            DueAt = Now.AddDays(-1),
            Priority = 5,
            Status = StudyTaskStatus.Todo
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "today",
            Title = "Terminare relazione",
            CourseId = "programming",
            DueAt = Now.AddHours(3),
            Priority = 3,
            Status = StudyTaskStatus.InProgress
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "completed",
            Title = "Già consegnata",
            DueAt = Now.AddHours(-1),
            Status = StudyTaskStatus.Completed,
            CompletedAt = Now
        });
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "future",
            Title = "Non ancora da fare oggi",
            DueAt = Now.AddDays(2),
            Status = StudyTaskStatus.Todo
        });

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        Assert.Equal(2, snapshot.Tasks.Count);
        Assert.Equal("overdue", snapshot.Tasks[0].Id);
        Assert.True(snapshot.Tasks[0].IsOverdue);
        Assert.Contains(snapshot.Tasks, x => x.Id == "today" && x.CanComplete);
        Assert.Contains(snapshot.Deadlines, x => x.Id == "future");
        Assert.DoesNotContain(snapshot.Tasks, x => x.Id == "completed");
    }

    [Fact]
    public void BuildCountsUniqueRecordedSecondsAndWeeklyGoalProgress()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Id = "activity-1",
            CourseId = "analysis",
            Title = "Sessione lunga",
            StartedAt = Now.AddHours(-2),
            EndedAt = Now.AddHours(-1),
            EffectiveSeconds = 3600,
            TimeZoneId = "UTC"
        }, Now);
        ledger.AddManual(data, new StudyActivityData
        {
            Id = "activity-2",
            CourseId = "analysis",
            Title = "Intervallo sovrapposto",
            StartedAt = Now.AddMinutes(-90),
            EndedAt = Now.AddMinutes(-60),
            EffectiveSeconds = 1800,
            TimeZoneId = "UTC"
        }, Now);

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        Assert.Equal(3600, snapshot.WeeklyRecordedSeconds);
        Assert.Equal(36000, snapshot.WeeklyGoalSeconds);
        Assert.Equal(10, snapshot.WeeklyStudyProgressPercent);
        Assert.Equal("1 h 00 min", snapshot.WeeklyStudyDisplay);
    }

    [Fact]
    public void RecommendationSuggestsIncompleteTopicForAnExamWithinSevenDays()
    {
        var data = Data();
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "exam-1",
            Title = "Appello di Analisi",
            Category = CalendarEventCategory.Exam,
            CourseId = "analysis",
            CourseName = "Analisi",
            StartsAt = Now.AddDays(5),
            EndsAt = Now.AddDays(5).AddHours(2),
            Verification = CalendarEventVerification.Official,
            TimeZoneId = "UTC"
        });
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "topic-1",
            CourseId = "analysis",
            Title = "Integrali impropri",
            Status = SyllabusTopicStatus.InProgress,
            EstimatedMinutes = 60
        });

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        Assert.Equal("Ripassa: Integrali impropri", snapshot.Recommendation.Title);
        Assert.Equal("Alta", snapshot.Recommendation.PriorityLabel);
        Assert.Equal(60, snapshot.Recommendation.DurationMinutes);
        Assert.Contains("5 giorni", snapshot.Recommendation.Reason);
    }

    [Fact]
    public void OverdueTaskTakesPriorityOverExamPreparation()
    {
        var data = Data();
        data.StudyTasks.Add(new StudyTaskData
        {
            Id = "late",
            Title = "Consegnare esercizi",
            CourseId = "programming",
            DueAt = Now.AddDays(-2),
            Priority = 5,
            EstimatedMinutes = 40,
            Status = StudyTaskStatus.Todo
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "exam-1",
            Title = "Appello di Analisi",
            Category = CalendarEventCategory.Exam,
            CourseId = "analysis",
            CourseName = "Analisi",
            StartsAt = Now.AddDays(3),
            EndsAt = Now.AddDays(3).AddHours(2),
            Verification = CalendarEventVerification.Official,
            TimeZoneId = "UTC"
        });

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        Assert.Equal("Consegnare esercizi", snapshot.Recommendation.Title);
        Assert.Equal("Alta", snapshot.Recommendation.PriorityLabel);
    }

    [Fact]
    public void WeeklyMissionProgressUsesCompletedSteps()
    {
        var data = Data();
        var mission = new StudyMissionData
        {
            Id = "mission-1",
            Title = "Preparazione Analisi",
            Period = StudyMissionPeriod.Weekly,
            StartsAt = Now.AddDays(-1),
            DueAt = Now.AddDays(4),
            Status = StudyMissionStatus.InProgress,
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-1", Title = "Limiti", IsCompleted = true },
                new() { Id = "step-2", Title = "Derivate", IsCompleted = false }
            }
        };
        data.StudyMissions.Add(mission);

        var snapshot = new DashboardService().Build(data, Now, "UTC");

        Assert.Equal(1, snapshot.WeeklyMissionCompletedSteps);
        Assert.Equal(2, snapshot.WeeklyMissionTotalSteps);
    }
}
