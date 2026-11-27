using System.Collections.ObjectModel;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class ActivityLedgerDailySummaryTests
{
    private static StudyData Data() => new()
    {
        Courses = new ObservableCollection<Course>
        {
            new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
        }
    };

    [Fact]
    public void DailyLedgerSummarySplitsCrossMidnightIntervalsAndCountsOverlapsPerDay()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        var zone = TimeZoneInfo.Local;
        var firstDay = new DateOnly(2026, 11, 12);
        var secondDay = firstDay.AddDays(1);
        var firstLocal = new DateTime(2026, 11, 12, 23, 30, 0, DateTimeKind.Unspecified);
        var secondLocal = firstLocal.AddMinutes(15);
        var firstStart = new DateTimeOffset(firstLocal, zone.GetUtcOffset(firstLocal));
        var firstEndLocal = firstLocal.AddHours(1);
        var firstEnd = new DateTimeOffset(firstEndLocal, zone.GetUtcOffset(firstEndLocal));
        var secondStart = new DateTimeOffset(secondLocal, zone.GetUtcOffset(secondLocal));
        var secondEndLocal = firstLocal.AddMinutes(45);
        var secondEnd = new DateTimeOffset(secondEndLocal, zone.GetUtcOffset(secondEndLocal));
        var observedAt = firstEnd.AddHours(1);

        ledger.AddManual(data, new StudyActivityData
        {
            Id = "overnight-first",
            Type = StudyActivityType.Study,
            Title = "Sessione notturna",
            StartedAt = firstStart,
            EndedAt = firstEnd,
            EffectiveSeconds = 3600,
            TimeZoneId = zone.Id
        }, observedAt);
        ledger.AddManual(data, new StudyActivityData
        {
            Id = "overnight-overlap",
            Type = StudyActivityType.Review,
            Title = "Ripasso notturno",
            StartedAt = secondStart,
            EndedAt = secondEnd,
            EffectiveSeconds = 1800,
            TimeZoneId = zone.Id
        }, observedAt);

        var summaries = ledger.SummarizeByDay(data, firstDay, secondDay, observedAt, zone.Id);

        Assert.Equal(1800, summaries[firstDay].RecordedSeconds);
        Assert.Equal(900, summaries[firstDay].OverlapSeconds);
        Assert.Equal(1800, summaries[secondDay].RecordedSeconds);
        Assert.Equal(900, summaries[secondDay].OverlapSeconds);

        var progress = new ProgressService().Calculate(data, firstDay, secondDay, observedAt);
        Assert.Equal(1800, progress.Daily.Single(x => x.Day == firstDay).RecordedSeconds);
        Assert.Equal(900, progress.Daily.Single(x => x.Day == firstDay).OverlapSeconds);
        Assert.Equal(1800, progress.Daily.Single(x => x.Day == secondDay).RecordedSeconds);
        Assert.Equal(900, progress.Daily.Single(x => x.Day == secondDay).OverlapSeconds);
    }

    [Fact]
    public void DailyLedgerSummaryUsesElapsedTimeAcrossRomeDaylightSavingTransitions()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();
        const string zoneId = "Europe/Rome";
        var springDay = new DateOnly(2026, 3, 29);
        var springStart = new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.FromHours(1));
        var springEnd = new DateTimeOffset(2026, 3, 29, 3, 30, 0, TimeSpan.FromHours(2));
        var fallDay = new DateOnly(2026, 10, 25);
        var fallStart = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(2));
        var fallEnd = new DateTimeOffset(2026, 10, 25, 2, 30, 0, TimeSpan.FromHours(1));
        var observedAt = fallEnd.AddDays(1);

        ledger.AddManual(data, new StudyActivityData
        {
            Id = "spring-forward",
            Type = StudyActivityType.Study,
            Title = "Sessione cambio ora primaverile",
            StartedAt = springStart,
            EndedAt = springEnd,
            EffectiveSeconds = 7200,
            TimeZoneId = zoneId
        }, observedAt);
        ledger.AddManual(data, new StudyActivityData
        {
            Id = "fall-back",
            Type = StudyActivityType.Study,
            Title = "Sessione cambio ora autunnale",
            StartedAt = fallStart,
            EndedAt = fallEnd,
            EffectiveSeconds = 3600,
            TimeZoneId = zoneId
        }, observedAt);

        var spring = ledger.SummarizeByDay(data, springDay, springDay, observedAt, zoneId)[springDay];
        var fall = ledger.SummarizeByDay(data, fallDay, fallDay, observedAt, zoneId)[fallDay];

        Assert.Equal(7200, spring.RecordedSeconds);
        Assert.Equal(0, spring.OverlapSeconds);
        Assert.Equal(3600, fall.RecordedSeconds);
        Assert.Equal(0, fall.OverlapSeconds);
    }

    [Fact]
    public void DailyLedgerSummaryRejectsReversedDateRanges()
    {
        var data = Data();
        var ledger = new ActivityLedgerService();

        Assert.Throws<ArgumentOutOfRangeException>(() => ledger.SummarizeByDay(
            data,
            new DateOnly(2026, 11, 13),
            new DateOnly(2026, 11, 12),
            new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero),
            "UTC"));
    }
}
