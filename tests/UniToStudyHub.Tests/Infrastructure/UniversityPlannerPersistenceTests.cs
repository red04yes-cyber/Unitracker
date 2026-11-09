using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class UniversityPlannerPersistenceTests
{
    [Fact]
    public async Task PlannerSettingsPlansSessionsLinksAndHistoryRoundTripThroughSqlite()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var start = new DateTimeOffset(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "analisi", Name = "Analisi", Cfu = 6 }
            },
            PlannerSettings = new PlannerSettingsData
            {
                PreferredSessionMinutes = 50,
                MinimumSessionMinutes = 25,
                ReviewReservePercent = 20,
                Availability = new ObservableCollection<PlannerAvailabilityWindowData>
                {
                    new()
                    {
                        DayOfWeek = DayOfWeek.Monday,
                        StartsAt = new TimeOnly(18, 0),
                        EndsAt = new TimeOnly(20, 0)
                    }
                }
            }
        };
        data.SyllabusTopics.Add(new SyllabusTopicData
        {
            Id = "topic-1",
            CourseId = "analisi",
            Title = "Derivate",
            Description = "Regole",
            EstimatedMinutes = 100,
            Difficulty = 4,
            PreparationLevel = 2,
            Priority = 1,
            Position = 0
        });
        data.StudyPlans.Add(new StudyPlanData
        {
            Id = "plan-1",
            CourseId = "analisi",
            CourseName = "Analisi",
            ExamDate = new DateTime(2026, 11, 20),
            RequiredMinutes = 100,
            ReviewReserveMinutes = 20,
            ScheduledMinutes = 120,
            Status = StudyPlanStatus.Completed,
            Explanation = "Piano di prova deterministico",
            Warnings = new ObservableCollection<string> { "Avviso di prova" },
            CreatedAt = start.AddDays(-1),
            UpdatedAt = start
        });
        data.CalendarEvents.Add(new CalendarEvent
        {
            Id = "plan-session-1",
            Title = "Studio: Derivate",
            Category = CalendarEventCategory.Study,
            StartsAt = start,
            EndsAt = start.AddMinutes(120),
            CourseId = "analisi",
            Source = CalendarEventSource.Planner,
            Verification = CalendarEventVerification.Indicative,
            TimeZoneId = "UTC",
            PlanId = "plan-1",
            SyllabusTopicId = "topic-1",
            PlannerStatus = PlannedSessionStatus.Completed,
            PriorityScore = 29,
            ActualMinutes = 120,
            CompletedAt = start.AddMinutes(120),
            ExternalId = "planner:plan-1:000"
        });
        data.PlannerReschedules.Add(new PlannerRescheduleData
        {
            Id = "move-1",
            PlanId = "plan-1",
            CalendarEventId = "plan-session-1",
            PreviousStartsAt = start.AddDays(-1),
            PreviousEndsAt = start.AddDays(-1).AddMinutes(120),
            NewStartsAt = start,
            NewEndsAt = start.AddMinutes(120),
            Reason = "Sessione scaduta",
            MovedAt = start.AddHours(1)
        });
        data.StudySessions.Add(new StudySessionData
        {
            Id = "focus-1",
            CourseId = "analisi",
            Topic = "Derivate",
            SyllabusTopicId = "topic-1",
            CalendarEventId = "plan-session-1",
            PlannedMinutes = 120,
            StartedAt = start,
            EndedAt = start.AddMinutes(110),
            LastResumedAt = null,
            AccumulatedFocusSeconds = 6600,
            State = StudySessionState.Completed,
            Segments = new ObservableCollection<StudySessionSegmentData>
            {
                new()
                {
                    Id = "segment-1",
                    Type = StudySessionSegmentType.Focus,
                    StartedAt = start,
                    EndedAt = start.AddMinutes(110)
                }
            }
        });

        await store.SaveAsync(data);
        var loaded = await store.LoadAsync();

        Assert.Equal(50, loaded.PlannerSettings.PreferredSessionMinutes);
        Assert.Single(loaded.PlannerSettings.Availability);
        Assert.Equal(TimeOnly.FromTimeSpan(TimeSpan.FromHours(20)), loaded.PlannerSettings.Availability[0].EndsAt);
        var plan = Assert.Single(loaded.StudyPlans);
        Assert.Equal(StudyPlanStatus.Completed, plan.Status);
        Assert.Equal(120, plan.ScheduledMinutes);
        Assert.Equal("Avviso di prova", Assert.Single(plan.Warnings));
        var session = Assert.Single(loaded.CalendarEvents);
        Assert.Equal("plan-1", session.PlanId);
        Assert.Equal("topic-1", session.SyllabusTopicId);
        Assert.Equal(PlannedSessionStatus.Completed, session.PlannerStatus);
        Assert.Equal(120, session.ActualMinutes);
        var history = Assert.Single(loaded.PlannerReschedules);
        Assert.False(history.IsReversed);
        Assert.Equal("plan-session-1", history.CalendarEventId);
        var focus = Assert.Single(loaded.StudySessions);
        Assert.Equal("plan-session-1", focus.CalendarEventId);
        Assert.Equal("topic-1", focus.SyllabusTopicId);
        Assert.Equal(6600, focus.AccumulatedFocusSeconds);
        Assert.Single(focus.Segments);
    }

    [Fact]
    public async Task IncrementalMigrationFromSprintAKeepsLegacyDataAndUsesConservativeDefaults()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261105120000_AddSessionNotePrivacy");

        await using (var context = database.CreateContext())
        {
            context.ProfileSettings.Add(new ProfileSettingsEntity
            {
                Id = 1,
                DegreeProgramName = "Informatica",
                DegreeTotalCfu = 180
            });
            context.Courses.Add(new CourseEntity
            {
                Id = "legacy-course",
                Name = "Analisi",
                Code = "MAT1",
                Cfu = 9,
                Source = "manual"
            });
            await context.SaveChangesAsync();

            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "SyllabusTopics"
                    ("Id", "CourseId", "Title", "Description", "Status", "EstimatedMinutes",
                     "SourceUrl", "LastStudiedAt", "UpdatedAt")
                VALUES
                    ('legacy-topic', 'legacy-course', 'Limiti', 'Definizione', 1, 90,
                     '', '2026-11-01 09:00:00', '2026-11-01 09:00:00');
                """);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "CalendarEvents"
                    ("Id", "Title", "Category", "StartsAt", "EndsAt", "IsAllDay", "CourseId",
                     "CourseName", "Location", "Notes", "Source", "Verification", "TimeZoneId",
                     "ExternalId", "ExternalSequence", "IsCancelled", "RecurrenceRule", "CreatedAt", "UpdatedAt")
                VALUES
                    ('legacy-event', 'Lezione di Analisi', 'Lesson',
                     '2026-11-10 09:00:00+00:00', '2026-11-10 11:00:00+00:00', 0, 'legacy-course',
                     'Analisi', 'Aula 1', '', 'Manual', 'Unverified', 'UTC',
                     NULL, 0, 0, '', '2026-11-01 09:00:00+00:00', '2026-11-01 09:00:00+00:00');
                """);
            await context.Database.ExecuteSqlRawAsync("""
                INSERT INTO "StudySessions"
                    ("Id", "CourseId", "Topic", "PlannedMinutes", "StartedAt", "LastResumedAt", "PausedAt",
                     "EndedAt", "AccumulatedFocusSeconds", "AccumulatedPauseSeconds", "State", "Outcome",
                     "Notes", "NotesArePrivate", "CreatedAt", "UpdatedAt")
                VALUES
                    ('legacy-session', 'legacy-course', 'Limiti', 25,
                     '2026-11-02 09:00:00+00:00', NULL, NULL, '2026-11-02 09:20:00+00:00',
                     1200, 0, 2, '', 'Nota privata', 1,
                     '2026-11-02 09:00:00+00:00', '2026-11-02 09:20:00+00:00');
                """);
        }

        await database.MigrateAsync();

        await using (var migrated = database.CreateContext())
        {
            Assert.Equal("Informatica", (await migrated.ProfileSettings.SingleAsync()).DegreeProgramName);
            Assert.Equal("Analisi", (await migrated.Courses.SingleAsync()).Name);
            var topic = await migrated.SyllabusTopics.SingleAsync();
            Assert.Equal("legacy-topic", topic.Id);
            Assert.Equal(3, topic.Priority);
            Assert.Equal(3, topic.Difficulty);
            Assert.Null(topic.PreparationLevel);
            Assert.Equal(0, topic.Position);
            Assert.Equal(string.Empty, topic.ParentTopicId);
            Assert.False(topic.IsArchived);
            var calendarEvent = await migrated.CalendarEvents.SingleAsync();
            Assert.Equal("legacy-event", calendarEvent.Id);
            Assert.Equal(string.Empty, calendarEvent.PlanId);
            Assert.Equal(string.Empty, calendarEvent.SyllabusTopicId);
            Assert.Equal(0, calendarEvent.ActualMinutes);
            Assert.Null(calendarEvent.CompletedAt);
            var session = await migrated.StudySessions.SingleAsync();
            Assert.Equal(string.Empty, session.CalendarEventId);
            Assert.Equal(string.Empty, session.SyllabusTopicId);
            Assert.True(await migrated.PlannerSettings.AnyAsync() == false);
            Assert.True(await migrated.StudyPlans.AnyAsync() == false);
            Assert.True(await migrated.PlannerReschedules.AnyAsync() == false);
        }

        var loaded = await new SqliteStudyDataStore(database.Factory).LoadAsync();
        Assert.Equal(50, loaded.PlannerSettings.PreferredSessionMinutes);
        Assert.Empty(loaded.PlannerSettings.Availability);
        Assert.Empty(loaded.StudyPlans);
        Assert.Equal("Limiti", Assert.Single(loaded.SyllabusTopics).Title);
        Assert.Equal("Lezione di Analisi", Assert.Single(loaded.CalendarEvents).Title);
        Assert.Equal("legacy-session", Assert.Single(loaded.StudySessions).Id);
        Assert.True(loaded.StudySessions[0].NotesArePrivate);
    }
}
