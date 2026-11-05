using System.Collections.ObjectModel;
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
}
