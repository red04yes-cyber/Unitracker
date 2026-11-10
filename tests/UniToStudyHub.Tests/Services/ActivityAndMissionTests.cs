using System.Collections.ObjectModel;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ActivityAndMissionTests
{
    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    private static readonly DateTimeOffset Start = new(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ManualQuickLogKeepsARevisionAndSoftDeletesWithoutLosingIt()
    {
        var data = Data();
        var service = new ActivityLedgerService();
        var activity = service.AddManual(data, new StudyActivityData
        {
            CourseId = "analisi",
            Type = StudyActivityType.Study,
            Title = "Ripasso limiti",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            EffectiveSeconds = 1500,
            TimeZoneId = "UTC",
            Tags = new ObservableCollection<string> { "limiti", "esame" }
        }, Start.AddHours(1));

        Assert.Single(data.StudyActivities);
        Assert.Single(data.ActivityRevisions);
        Assert.Equal(ActivityRevisionAction.Created, data.ActivityRevisions[0].Action);
        Assert.Equal(1500, activity.EffectiveSeconds);

        var corrected = new StudyActivityData
        {
            Id = activity.Id,
            CourseId = "analisi",
            Type = StudyActivityType.Study,
            Title = "Ripasso limiti e continuità",
            StartedAt = activity.StartedAt,
            EndedAt = activity.EndedAt,
            EffectiveSeconds = 1200,
            TimeZoneId = "UTC",
            Tags = activity.Tags
        };
        service.Update(data, corrected, "Correzione durata", Start.AddHours(2));

        Assert.Equal(2, activity.Revision);
        Assert.Equal(2, data.ActivityRevisions.Count);
        Assert.Equal(ActivityRevisionAction.Updated, data.ActivityRevisions[1].Action);
        Assert.Contains("Ripasso limiti", data.ActivityRevisions[1].BeforeJson, StringComparison.Ordinal);
        Assert.Contains("Ripasso limiti e continuit", data.ActivityRevisions[1].AfterJson, StringComparison.Ordinal);

        service.SoftDelete(data, activity.Id, "Inserimento duplicato", Start.AddHours(3));

        Assert.True(activity.IsDeleted);
        Assert.Equal(3, data.ActivityRevisions.Count);
        Assert.Empty(service.GetForDay(data, new DateOnly(2026, 11, 4), "UTC"));
        Assert.Empty(service.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4), Start.AddHours(4), "UTC").ByType);
    }

    [Fact]
    public void FocusSessionSyncIsIdempotentAndDoesNotDuplicateTheSameTime()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", "Limiti", 25, Start);

        var running = ledger.SyncFocusSession(data, session, Start.AddMinutes(10));
        var again = ledger.SyncFocusSession(data, session, Start.AddMinutes(20));
        focus.Finish(data, session.Id, "Completata", Start.AddMinutes(25));
        var completed = ledger.SyncFocusSession(data, session, Start.AddMinutes(25));

        Assert.Single(data.StudyActivities);
        Assert.Equal(StudyActivityStatus.Completed, completed.Status);
        Assert.Equal(1500, completed.EffectiveSeconds);
        Assert.Equal(StudyActivityOrigin.Timer, completed.Origin);
        Assert.Single(data.ActivityRevisions); // timer synchronization is not a manual correction
        Assert.Equal(1, running.Revision);
        Assert.Equal(1, again.Revision);
        Assert.Equal(1500, ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(1), "UTC").RecordedSeconds);
    }

    [Fact]
    public void TimelineAndHeatmapUseRealIntervalsAndFlagOverlaps()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Prima",
            StartedAt = Start,
            EndedAt = Start.AddHours(1),
            TimeZoneId = "UTC"
        }, Start.AddHours(3));
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Review,
            Title = "Seconda",
            StartedAt = Start.AddMinutes(30),
            EndedAt = Start.AddMinutes(90),
            TimeZoneId = "UTC"
        }, Start.AddHours(3));

        var timeline = ledger.BuildTimeline(data, new DateOnly(2026, 11, 4), 30, Start.AddHours(3), "UTC");
        var heatmap = ledger.BuildHeatmap(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(3), "UTC");
        var overlaps = ledger.FindOverlaps(data, new DateOnly(2026, 11, 4), Start.AddHours(3), "UTC");

        Assert.Equal(48, timeline.Slots.Count);
        Assert.Equal(5400, timeline.RecordedSeconds);
        Assert.Equal(1800, timeline.OverlapSeconds);
        Assert.Equal(2, timeline.Slots.Single(x => x.Index == 19).ActivityIds.Count);
        Assert.True(timeline.Slots.Single(x => x.Index == 19).HasOverlap);
        Assert.Equal(5400, timeline.Slots.Skip(18).Take(3).Sum(x => x.RecordedSeconds));
        Assert.Equal(3600, heatmap.Cells.Single(x => x.Hour == 9).RecordedSeconds);
        Assert.Equal(1800, heatmap.Cells.Single(x => x.Hour == 10).RecordedSeconds);
        var overlap = Assert.Single(overlaps);
        Assert.Equal(1800, (int)(overlap.EndsAt - overlap.StartsAt).TotalSeconds);
    }

    [Fact]
    public void PausedFocusIntervalIsExcludedFromTimelineAndSummary()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", "Limiti", 25, Start);
        focus.Pause(data, session.Id, Start.AddMinutes(10));
        focus.Resume(data, session.Id, Start.AddMinutes(20));
        focus.Finish(data, session.Id, "Completata", Start.AddMinutes(30));
        ledger.SyncFocusSession(data, session, Start.AddMinutes(30));

        var timeline = ledger.BuildTimeline(data, new DateOnly(2026, 11, 4), 30, Start.AddHours(1), "UTC");
        var summary = ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(1), "UTC");

        Assert.Equal(1200, session.AccumulatedFocusSeconds);
        Assert.Equal(3, session.Segments.Count);
        Assert.Equal(1200, timeline.RecordedSeconds);
        Assert.Equal(1200, summary.RecordedSeconds);
        Assert.Equal(0, timeline.OverlapSeconds);
        Assert.Equal(1200, timeline.Slots.Single(x => x.Index == 18).RecordedSeconds);
        Assert.Equal(0, timeline.Slots.Single(x => x.Index == 19).RecordedSeconds);
        var pauseSegment = Assert.Single(session.Segments, x => x.Type == StudySessionSegmentType.Pause);
        Assert.Equal(Start.AddMinutes(10), pauseSegment.StartedAt);
        Assert.Equal(Start.AddMinutes(20), pauseSegment.EndedAt);
    }

    [Fact]
    public void ImmediatePauseResumeAndFinishDoesNotLeaveZeroLengthSegments()
    {
        var data = Data();
        var focus = new FocusSessionService();
        var session = focus.Start(data, "analisi", "Limiti", 25, Start);
        focus.Pause(data, session.Id, Start);
        focus.Resume(data, session.Id, Start);
        focus.Finish(data, session.Id, "Completata", Start);

        Assert.DoesNotContain(session.Segments,
            segment => segment.EndedAt is { } end && end <= segment.StartedAt);
        StudyDataEntityMapper.NormalizeAndValidate(data);
    }

    [Fact]
    public void LegacySessionWithoutSegmentsUsesEffectiveSecondsAsAnApproximateBlock()
    {
        var data = Data();
        var session = new StudySessionData
        {
            Id = "legacy-session",
            CourseId = "analisi",
            Topic = "Sessione legacy",
            PlannedMinutes = 25,
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            LastResumedAt = null,
            State = StudySessionState.Completed,
            AccumulatedFocusSeconds = 1200
        };
        data.StudySessions.Add(session);
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Sessione legacy",
            CourseId = "analisi",
            SessionId = session.Id,
            StartedAt = Start,
            EndedAt = Start.AddMinutes(30),
            EffectiveSeconds = 1200,
            TimeZoneId = "UTC"
        }, Start.AddHours(1));

        var summary = ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(1), "UTC");
        var timeline = ledger.BuildTimeline(data, new DateOnly(2026, 11, 4), 30,
            Start.AddHours(1), "UTC");

        Assert.Empty(session.Segments);
        Assert.Equal(1200, summary.RecordedSeconds);
        Assert.Equal(1200, timeline.RecordedSeconds);
        Assert.Equal(0, timeline.Slots.Single(x => x.Index == 19).RecordedSeconds);
    }

    [Fact]
    public void ActivityAcrossMidnightAppearsInBothDaysWithoutLosingSeconds()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            Type = StudyActivityType.Study,
            Title = "Sessione notturna",
            StartedAt = new DateTimeOffset(2026, 11, 4, 23, 30, 0, TimeSpan.Zero),
            EndedAt = new DateTimeOffset(2026, 11, 5, 0, 30, 0, TimeSpan.Zero),
            TimeZoneId = "UTC"
        }, new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero));

        var first = ledger.BuildTimeline(data, new DateOnly(2026, 11, 4), 60,
            new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero), "UTC");
        var second = ledger.BuildTimeline(data, new DateOnly(2026, 11, 5), 60,
            new DateTimeOffset(2026, 11, 5, 1, 0, 0, TimeSpan.Zero), "UTC");

        Assert.Single(ledger.GetForDay(data, new DateOnly(2026, 11, 4), "UTC"));
        Assert.Single(ledger.GetForDay(data, new DateOnly(2026, 11, 5), "UTC"));
        Assert.Equal(1800, first.RecordedSeconds);
        Assert.Equal(1800, second.RecordedSeconds);
    }

    [Fact]
    public void DstDayUsesItsRealLength()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        var day = new DateOnly(2026, 3, 29);
        var timeline = ledger.BuildTimeline(data, day, 60,
            new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.FromHours(2)), "Europe/Rome");

        Assert.Equal(23 * 3600, timeline.TotalSeconds);
        Assert.Equal(23, timeline.Slots.Count);
    }

    [Fact]
    public void ThreeOverlappingActivitiesUseUniqueSummarySeconds()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        foreach (var (title, offset, minutes) in new[]
                 {
                     ("A", 0, 120), ("B", 30, 60), ("C", 60, 120)
                 })
        {
            ledger.AddManual(data, new StudyActivityData
            {
                Type = StudyActivityType.Study,
                Title = title,
                StartedAt = Start.AddMinutes(offset),
                EndedAt = Start.AddMinutes(offset + minutes),
                TimeZoneId = "UTC"
            }, Start.AddHours(4));
        }

        var summary = ledger.Summarize(data, new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 4),
            Start.AddHours(4), "UTC");

        Assert.Equal(3 * 3600, summary.RecordedSeconds);
        Assert.Equal(90 * 60, summary.OverlapSeconds);
        Assert.Equal(3, summary.ActivityCount);
    }

    [Fact]
    public void MissionStepsDriveStatusAndCompletion()
    {
        var data = Data();
        var service = new StudyMissionService();
        var mission = service.Add(data, new StudyMissionData
        {
            CourseId = "analisi",
            Title = "Missione limiti",
            Description = "Tre passi",
            StartsAt = Start,
            DueAt = Start.AddHours(4),
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Title = "Leggi" },
                new() { Title = "Esercizi" },
                new() { Title = "Ripassa" }
            }
        }, Start);

        Assert.Equal(3, mission.Steps.Count);
        Assert.Equal(StudyMissionStatus.Planned, mission.Status);
        service.SetStepCompleted(data, mission.Id, mission.Steps[0].Id, true, Start.AddMinutes(10));
        Assert.Equal(StudyMissionStatus.InProgress, mission.Status);
        service.Complete(data, mission.Id, Start.AddMinutes(20));

        Assert.Equal(StudyMissionStatus.Completed, mission.Status);
        Assert.True(mission.Steps.All(x => x.IsCompleted));
        Assert.NotNull(mission.CompletedAt);
        Assert.Equal(3, mission.Steps.Count);
    }

    [Fact]
    public void ActivityRejectsMoreCreditedTimeThanItsInterval()
    {
        var data = Data();
        var service = new ActivityLedgerService();
        Assert.Throws<ArgumentException>(() => service.AddManual(data, new StudyActivityData
        {
            Title = "Errore",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(10),
            EffectiveSeconds = 700,
            TimeZoneId = "UTC"
        }, Start.AddHours(1)));
    }

    [Fact]
    public void FocusSessionSyncCanLinkAnOpenTaskAndKeepsTheLinkIdempotently()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Esercizi sui limiti",
            EstimatedMinutes = 30
        });
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, null, task.Title, 25, Start);

        var created = ledger.SyncFocusSession(data, session, Start.AddMinutes(5), task.Id);
        var refreshed = ledger.SyncFocusSession(data, session, Start.AddMinutes(10));

        Assert.Single(data.StudyActivities);
        Assert.Equal(task.Id, created.TaskId);
        Assert.Equal(task.Id, refreshed.TaskId);
        Assert.Equal("analisi", session.CourseId);
        Assert.Same(created, refreshed);
        Assert.Single(data.ActivityRevisions);
    }

    [Fact]
    public void TaskProgressUnionsOverlappingManualAndPausedTimerIntervals()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Preparare gli esercizi",
            EstimatedMinutes = 30
        });
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", task.Title, 30, Start);
        focus.Pause(data, session.Id, Start.AddMinutes(10));
        focus.Resume(data, session.Id, Start.AddMinutes(20));
        focus.Finish(data, session.Id, "Completata", Start.AddMinutes(30));
        ledger.SyncFocusSession(data, session, Start.AddMinutes(30), task.Id);
        ledger.AddManual(data, new StudyActivityData
        {
            CourseId = "analisi",
            TaskId = task.Id,
            Type = StudyActivityType.Exercise,
            Title = "Esercizi svolti fuori timer",
            StartedAt = Start.AddMinutes(5),
            EndedAt = Start.AddMinutes(15),
            EffectiveSeconds = 600,
            TimeZoneId = "UTC"
        }, Start.AddHours(1));

        var progress = ledger.GetTaskProgress(data, task.Id, Start.AddHours(1));

        // 20 focus minutes plus a manual interval that adds only 5 unique minutes.
        Assert.Equal(1500, progress.RecordedSeconds);
        Assert.Equal(300, progress.RemainingSeconds);
        Assert.Equal(5, progress.RemainingMinutes);
        Assert.Equal(83, progress.ProgressPercent);
        Assert.False(progress.IsOverEstimate);
    }

    [Fact]
    public void CancelledTimerStillCreditsFocusThatWasActuallyRecorded()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Ripasso interrotto",
            EstimatedMinutes = 10
        });
        var focus = new FocusSessionService();
        var ledger = new ActivityLedgerService();
        var session = focus.Start(data, "analisi", task.Title, 10, Start);
        focus.Cancel(data, session.Id, Start.AddMinutes(7));
        ledger.SyncFocusSession(data, session, Start.AddMinutes(7), task.Id);

        var progress = ledger.GetTaskProgress(data, task.Id, Start.AddMinutes(8));

        Assert.Equal(420, progress.RecordedSeconds);
        Assert.Equal(7, progress.RecordedMinutes);
        Assert.Equal(70, progress.ProgressPercent);
    }

    [Fact]
    public void TaskLinkRejectsMissingTasksAndCrossCourseAttribution()
    {
        var data = Data();
        data.Courses.Add(new Course { Id = "fisica", Name = "Fisica", Cfu = 6 });
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "fisica-task",
            CourseId = "fisica",
            Title = "Esercizi di fisica",
            EstimatedMinutes = 20
        });
        var session = new FocusSessionService().Start(data, "analisi", "Limiti", 25, Start);
        var ledger = new ActivityLedgerService();

        Assert.Throws<ArgumentException>(() => ledger.SyncFocusSession(data, session, Start, "missing-task"));
        Assert.Throws<ArgumentException>(() => ledger.SyncFocusSession(data, session, Start, task.Id));
        Assert.Empty(data.StudyActivities);
    }

    [Fact]
    public void TaskRemovalKeepsHistoricalReferenceAndCancelsTheOpenTask()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Ripassare",
            EstimatedMinutes = 20
        });
        var session = new FocusSessionService().Start(data, "analisi", task.Title, 20, Start);
        new ActivityLedgerService().SyncFocusSession(data, session, Start.AddMinutes(5), task.Id);

        new StudyTaskService().Remove(data, task.Id, Start.AddMinutes(6));

        Assert.Contains(task, data.StudyTasks);
        Assert.Equal(StudyTaskStatus.Cancelled, task.Status);
        Assert.Equal(task.Id, Assert.Single(data.StudyActivities).TaskId);
        StudyDataEntityMapper.NormalizeAndValidate(data);
    }

    [Fact]
    public void TaskProgressCapsTheBarAndReportsAnEstimateOverrun()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Studio approfondito",
            EstimatedMinutes = 5
        });
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            CourseId = "analisi",
            TaskId = task.Id,
            Type = StudyActivityType.Study,
            Title = "Studio effettivo",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(8),
            EffectiveSeconds = 480,
            TimeZoneId = "UTC"
        }, Start.AddMinutes(9));

        var progress = ledger.GetTaskProgress(data, task.Id, Start.AddMinutes(10));

        Assert.Equal(480, progress.RecordedSeconds);
        Assert.Equal(0, progress.RemainingSeconds);
        Assert.Equal(100, progress.ProgressPercent);
        Assert.True(progress.IsOverEstimate);
    }

    [Fact]
    public void PomodoroStartCanLinkItsFocusSessionToAnOpenTask()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-1",
            CourseId = "analisi",
            Title = "Preparare gli integrali",
            EstimatedMinutes = 45
        });
        var ledger = new ActivityLedgerService();
        var pomodoro = new PomodoroService(new FocusSessionService(), ledger);

        var timer = pomodoro.Start(data, "analisi", task.Title, Start, task.Id);

        var activity = Assert.Single(data.StudyActivities);
        Assert.Equal(timer.SessionId, activity.SessionId);
        Assert.Equal(task.Id, activity.TaskId);
        Assert.Equal("analisi", activity.CourseId);
    }


    [Fact]
    public void PomodoroRejectsCrossCourseTaskBeforeCreatingAnySession()
    {
        var data = Data();
        data.Courses.Add(new Course { Id = "fisica", Name = "Fisica", Cfu = 6 });
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "fisica-task",
            CourseId = "fisica",
            Title = "Esercizi di fisica",
            EstimatedMinutes = 30
        });
        var pomodoro = new PomodoroService(new FocusSessionService(), new ActivityLedgerService());

        Assert.Throws<ArgumentException>(() => pomodoro.Start(data, "analisi", task.Title, Start, task.Id));
        Assert.Empty(data.StudySessions);
        Assert.Null(data.PomodoroTimer);
        Assert.Empty(data.StudyActivities);
    }

    [Fact]
    public void TaskWithoutEstimateIsNotPresentedAsComplete()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-unestimated",
            CourseId = "analisi",
            Title = "Lettura libera",
            EstimatedMinutes = 0
        });
        var ledger = new ActivityLedgerService();
        ledger.AddManual(data, new StudyActivityData
        {
            CourseId = "analisi",
            TaskId = task.Id,
            Type = StudyActivityType.Reading,
            Title = "Lettura",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(5),
            EffectiveSeconds = 300,
            TimeZoneId = "UTC"
        }, Start.AddMinutes(6));

        var progress = ledger.GetTaskProgress(data, task.Id, Start.AddMinutes(7));

        Assert.False(progress.HasEstimate);
        Assert.Equal(300, progress.RecordedSeconds);
        Assert.Equal(0, progress.ProgressPercent);
        Assert.Equal(0, progress.RemainingSeconds);
        Assert.False(progress.IsOverEstimate);
    }

    [Fact]
    public void UnreferencedTaskCanStillBePhysicallyRemoved()
    {
        var data = Data();
        var task = new StudyTaskService().Add(data, new StudyTaskData
        {
            Id = "task-unlinked",
            CourseId = "analisi",
            Title = "Attività senza storico",
            EstimatedMinutes = 15
        });

        new StudyTaskService().Remove(data, task.Id, Start);

        Assert.DoesNotContain(data.StudyTasks, x => x.Id == task.Id);
    }


    [Fact]
    public void TimerActivityWithoutFocusSessionIsRejectedByPersistenceValidation()
    {
        var data = Data();
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "orphan-timer",
            Type = StudyActivityType.Study,
            Title = "Timer senza sessione",
            StartedAt = Start,
            EndedAt = Start.AddMinutes(5),
            TimeZoneId = "UTC",
            EffectiveSeconds = 300,
            Origin = StudyActivityOrigin.Timer,
            Status = StudyActivityStatus.Completed
        });

        Assert.Throws<InvalidDataException>(() => StudyDataEntityMapper.NormalizeAndValidate(data));
    }

}
