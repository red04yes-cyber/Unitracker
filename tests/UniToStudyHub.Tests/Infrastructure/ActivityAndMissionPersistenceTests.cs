using System.Collections.ObjectModel;
using System.IO;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
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
        data.StudySessions.Add(new StudySessionData
        {
            Id = "session-1",
            CourseId = "analisi",
            Topic = "Limiti",
            PlannedMinutes = 25,
            StartedAt = start,
            LastResumedAt = start,
            State = StudySessionState.Running,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-1",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = start
                }
            }
        });
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
        Assert.Single(loaded.StudySessions);
        var loadedSegment = Assert.Single(loaded.StudySessions[0].Segments);
        Assert.Equal(StudySessionSegmentType.Focus, loadedSegment.Type);
        Assert.Equal(start, loadedSegment.StartedAt);
        Assert.Null(loadedSegment.EndedAt);
        Assert.Equal("mission-1", loaded.StudyActivities[0].MissionId);
        Assert.Single(loaded.ActivityRevisions);
        Assert.Equal(ActivityRevisionAction.Created, loaded.ActivityRevisions[0].Action);
    }

    [Fact]
    public async Task RemovingASessionCascadesItsFocusSegments()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData
        {
            StudySessions = new ObservableCollection<StudySessionData>
            {
                new()
                {
                    Id = "session-cascade",
                    StartedAt = start,
                    LastResumedAt = start,
                    State = StudySessionState.Running,
                    Segments = new ObservableCollection<StudySessionSegmentData>
                    {
                        new() { Id = "segment-cascade", Type = StudySessionSegmentType.Focus, StartedAt = start }
                    }
                }
            }
        };
        await store.SaveAsync(data);

        await using (var context = database.CreateContext())
        {
            context.StudySessions.Remove(await context.StudySessions.SingleAsync());
            await context.SaveChangesAsync();
        }

        await using var verification = database.CreateContext();
        Assert.True(await verification.StudySessionSegments.AnyAsync() == false);
    }

    [Fact]
    public void PersistenceValidationRejectsOverlappingSessionSegments()
    {
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course> { new() { Id = "analisi", Name = "Analisi", Cfu = 6 } },
            StudySessions = new ObservableCollection<StudySessionData>
            {
                new()
                {
                    Id = "session-overlap",
                    CourseId = "analisi",
                    StartedAt = start,
                    State = StudySessionState.Running,
                    Segments = new ObservableCollection<StudySessionSegmentData>
                    {
                        new() { Type = StudySessionSegmentType.Focus, StartedAt = start, EndedAt = start.AddHours(1) },
                        new() { Type = StudySessionSegmentType.Pause, StartedAt = start.AddMinutes(30), EndedAt = start.AddMinutes(45) }
                    }
                }
            }
        };

        Assert.Throws<InvalidDataException>(() => StudyDataEntityMapper.NormalizeAndValidate(data));
    }

    [Fact]
    public async Task IncrementalMigrationPreservesExistingUserData()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261105005756_AddActivityLedgerAndMissions");

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
            var legacyStart = new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
            context.StudySessions.Add(new UniToStudyHub.Infrastructure.Data.Entities.StudySessionEntity
            {
                Id = "session-legacy",
                CourseId = "course-existing",
                Topic = "Limiti legacy",
                PlannedMinutes = 25,
                StartedAt = legacyStart,
                EndedAt = legacyStart.AddMinutes(20),
                AccumulatedFocusSeconds = 1200,
                State = (int)StudySessionState.Completed,
                Outcome = "Completata prima della migrazione",
                CreatedAt = legacyStart,
                UpdatedAt = legacyStart.AddMinutes(20)
            });
            context.StudyActivities.Add(new UniToStudyHub.Infrastructure.Data.Entities.StudyActivityEntity
            {
                Id = "activity-legacy",
                Type = (int)StudyActivityType.Study,
                Title = "Attività legacy",
                CourseId = "course-existing",
                SessionId = "session-legacy",
                StartedAt = legacyStart,
                EndedAt = legacyStart.AddMinutes(20),
                TimeZoneId = "UTC",
                EffectiveSeconds = 1200,
                Origin = (int)StudyActivityOrigin.Manual,
                Status = (int)StudyActivityStatus.Completed,
                CreatedAt = legacyStart,
                UpdatedAt = legacyStart.AddMinutes(20)
            });
            await context.SaveChangesAsync();
        }

        await database.MigrateAsync();

        await using (var migrated = database.CreateContext())
        {
            Assert.Equal("Corso esistente", (await migrated.ProfileSettings.SingleAsync()).DegreeProgramName);
            Assert.Equal("Analisi", (await migrated.Courses.SingleAsync()).Name);
            Assert.Equal("session-legacy", (await migrated.StudySessions.SingleAsync()).Id);
            Assert.Equal("activity-legacy", (await migrated.StudyActivities.SingleAsync()).Id);
            Assert.Equal(1200, (await migrated.StudyActivities.SingleAsync()).EffectiveSeconds);
            Assert.True(await migrated.StudySessionSegments.AnyAsync() == false);
            Assert.True(await migrated.StudyMissions.AnyAsync() == false);
        }

        var loaded = await new SqliteStudyDataStore(database.Factory).LoadAsync();
        Assert.Single(loaded.StudySessions);
        Assert.Single(loaded.StudyActivities);
        Assert.Equal("Limiti legacy", loaded.StudySessions[0].Topic);
        Assert.Equal("Attività legacy", loaded.StudyActivities[0].Title);
        Assert.Empty(loaded.StudySessions[0].Segments);
    }
}
