using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class ActivityAndMissionPersistenceTests
{
    [Fact]
    public async Task ActivitiesMissionsAndRevisionsRoundTripThroughSqlite()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
            }
        };
        var mission = new StudyMissionData
        {
            Id = "mission-1",
            CourseId = "analisi",
            Title = "Missione",
            StartsAt = start,
            Steps = new ObservableCollection<MissionStepData>
            {
                new() { Id = "step-1", Title = "Primo", IsCompleted = true, CompletedAt = start },
                new() { Id = "step-2", Title = "Secondo" }
            }
        };
        data.StudyMissions.Add(mission);
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "activity-1",
            CourseId = "analisi",
            MissionId = "mission-1",
            Title = "Lettura",
            StartedAt = start,
            EndedAt = start.AddMinutes(30),
            EffectiveSeconds = 1500,
            TimeZoneId = "UTC",
            Tags = new ObservableCollection<string> { "limiti" },
            IsPrivate = true
        });
        data.ActivityRevisions.Add(new ActivityRevisionData
        {
            Id = "revision-1",
            ActivityId = "activity-1",
            Action = ActivityRevisionAction.Created,
            Reason = "Inserimento",
            RevisedAt = start
        });

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();

        Assert.Single(loaded.StudyActivities);
        Assert.Equal("Lettura", loaded.StudyActivities[0].Title);
        Assert.Equal(1500, loaded.StudyActivities[0].EffectiveSeconds);
        Assert.True(loaded.StudyActivities[0].IsPrivate);
        Assert.Contains("limiti", loaded.StudyActivities[0].Tags);
        Assert.Single(loaded.StudyMissions);
        Assert.Equal(2, loaded.StudyMissions[0].Steps.Count);
        Assert.Equal("mission-1", loaded.StudyActivities[0].MissionId);
        Assert.Single(loaded.ActivityRevisions);
        Assert.Equal(ActivityRevisionAction.Created, loaded.ActivityRevisions[0].Action);
    }

    [Fact]
    public async Task IncrementalMigrationPreservesExistingUserData()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261105000258_AddStudyLabAndCalendarPersistence");

        await using (var context = database.CreateContext())
        {
            context.ProfileSettings.Add(new UniToStudyHub.Infrastructure.Data.Entities.ProfileSettingsEntity
            {
                Id = 1,
                DegreeProgramName = "Corso esistente"
            });
            context.Courses.Add(new UniToStudyHub.Infrastructure.Data.Entities.CourseEntity
            {
                Id = "course-existing",
                Name = "Analisi",
                Code = "MAT1",
                Cfu = 6,
                Source = "manual"
            });
            await context.SaveChangesAsync();
        }

        await database.MigrateAsync();

        await using var migrated = database.CreateContext();
        Assert.Equal("Corso esistente", (await migrated.ProfileSettings.SingleAsync()).DegreeProgramName);
        Assert.Equal("Analisi", (await migrated.Courses.SingleAsync()).Name);
        Assert.True(await migrated.StudyActivities.AnyAsync() == false);
        Assert.True(await migrated.StudyMissions.AnyAsync() == false);
    }
}
