using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class SqliteStudyDataStoreTests
{
    private static SqliteStudyDataStore Store(SqliteTestDatabase database) =>
        new(database.Factory);

    [Fact]
    public async Task MigrationHistoryContainsBothVersionedMigrations()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        await using var db = database.CreateContext();

        var migrations = db.Database.GetMigrations().ToList();

        Assert.Contains("20261104000100_InitialCreate", migrations);
        Assert.Contains("20261104000200_AddCourseRelationships", migrations);
    }

    [Fact]
    public async Task MigrationCreatesSchemaAndIsIdempotent()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();

        await database.MigrateAsync();
        await database.MigrateAsync();

        var tables = await database.QueryScalarsAsync(
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name");
        Assert.Contains("Courses", tables);
        Assert.Contains("Grades", tables);
        Assert.Contains("PlannedExams", tables);
        Assert.Contains("Announcements", tables);
        Assert.Contains("CareerExams", tables);
        Assert.Contains("ExamAttempts", tables);
        Assert.Contains("ProfileSettings", tables);
        Assert.Contains("LegacyImports", tables);

        var indexes = await database.QueryScalarsAsync(
            "SELECT name FROM sqlite_master WHERE type='index' AND name='IX_CareerExams_CourseId'");
        Assert.Single(indexes);

        var foreignKeys = await database.QueryScalarsAsync(
            "SELECT \"table\" FROM pragma_foreign_key_list('Grades')");
        Assert.Contains("Courses", foreignKeys);

        var store = Store(database);
        await store.SaveAsync(TestStudyData.CreateValid());
        await database.MigrateAsync();

        var loaded = await store.LoadAsync();
        Assert.Equal(2, loaded.Courses.Count);
        Assert.Single(loaded.Grades);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsEveryCollectionAndSetting()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = Store(database);
        var source = TestStudyData.CreateValid();

        await store.SaveAsync(source);
        var loaded = await store.LoadAsync();

        Assert.Equal(source.GraduationTarget, loaded.GraduationTarget);
        Assert.Equal(source.MoodleBaseUrl, loaded.MoodleBaseUrl);
        Assert.Equal(2, loaded.Courses.Count);
        Assert.Equal("course-1", loaded.Courses.Single(x => x.Name == "Analisi").Id);
        Assert.Equal(6, loaded.Courses.Single(x => x.Name == "Analisi").Cfu);
        Assert.Single(loaded.Grades);
        Assert.Equal(28, loaded.Grades[0].Value);
        Assert.Single(loaded.Exams);
        Assert.Equal("Estiva 2026", loaded.Exams[0].PlannedSession);
        Assert.Single(loaded.Announcements);
        Assert.Single(loaded.CareerExams);
        Assert.Equal("career-1", loaded.CareerExams[0].Id);
        Assert.Single(loaded.CareerExams[0].Attempts);
        Assert.Equal("Completed", loaded.CareerExams[0].Attempts[0].Status);
        Assert.Equal(28, loaded.CareerExams[0].Attempts[0].NumericGrade);
        Assert.NotNull(loaded.CareerExams[0].Attempts[0].RecordedAt);
    }

    [Fact]
    public async Task SaveRollsBackOnDatabaseFailure()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = Store(database);
        await store.SaveAsync(TestStudyData.CreateValid());

        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_courses BEFORE INSERT ON Courses BEGIN SELECT RAISE(ABORT, 'simulated'); END;");
        }

        var modified = TestStudyData.CreateValid();
        modified.Courses[0].Name = "Nome modificato";

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(modified));
        Assert.Contains("rolled back", ex.Message, StringComparison.OrdinalIgnoreCase);

        var loaded = await store.LoadAsync();
        Assert.Equal(2, loaded.Courses.Count);
        Assert.Contains(loaded.Courses, x => x.Name == "Analisi");
        Assert.DoesNotContain(loaded.Courses, x => x.Name == "Nome modificato");
    }

    [Fact]
    public async Task SaveRejectsDuplicateIdsBeforeDeletingExistingData()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = Store(database);
        await store.SaveAsync(TestStudyData.CreateValid());

        var invalid = TestStudyData.CreateValid();
        invalid.Courses.Add(new Course { Id = "course-1", Name = "Duplicato", Cfu = 6 });

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(invalid));

        var loaded = await store.LoadAsync();
        Assert.Equal(2, loaded.Courses.Count);
        Assert.DoesNotContain(loaded.Courses, x => x.Name == "Duplicato");
    }

    [Fact]
    public async Task ExistingDatabaseIsUpgradedWithoutDataLoss()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261104000100_InitialCreate");

        await using (var db = database.CreateContext())
        {
            db.Courses.Add(new CourseEntity { Id = "course-1", Name = "Analisi", Code = "MAT1", Cfu = 6 });
            db.Grades.Add(new GradeEntity
            {
                Id = "grade-1", CourseId = "course-1", CourseName = "Analisi",
                Value = 28, Cfu = 6, Date = new DateTime(2026, 1, 10)
            });
            db.PlannedExams.Add(new PlannedExamEntity
            {
                Id = "planned-1", CourseId = "course-1", CourseName = "Analisi",
                Name = "Analisi", Cfu = 6, PlannedSession = "Estiva 2026"
            });
            db.CareerExams.Add(new CareerExamEntity
            {
                Id = "career-1", CourseId = "course-1", Name = "Analisi", Cfu = 6,
                Attempts = new List<ExamAttemptEntity>
                {
                    new()
                    {
                        Id = "attempt-1", ExamId = "career-1",
                        AttemptedAt = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero),
                        Status = 3, Outcome = 1, GradeDecision = 1, NumericGrade = 28,
                        RecordedAt = new DateTimeOffset(2026, 1, 11, 10, 0, 0, TimeSpan.Zero)
                    }
                }
            });
            db.ProfileSettings.Add(new ProfileSettingsEntity
            {
                Id = 1, GraduationTarget = new DateTime(2027, 10, 1),
                MoodleBaseUrl = "https://moodle.example.edu"
            });
            await db.SaveChangesAsync();
        }

        await database.MigrateAsync();

        await using (var db = database.CreateContext())
        {
            Assert.Equal(1, await db.Courses.CountAsync());
            Assert.Equal("Analisi", (await db.Courses.SingleAsync()).Name);
            Assert.Equal(1, await db.Grades.CountAsync());
            Assert.Equal(28, (await db.Grades.SingleAsync()).Value);
            Assert.Equal(1, await db.PlannedExams.CountAsync());
            Assert.Equal(1, await db.CareerExams.CountAsync());
            Assert.Equal(1, await db.ExamAttempts.CountAsync());
            Assert.Equal("https://moodle.example.edu", (await db.ProfileSettings.SingleAsync()).MoodleBaseUrl);
        }

        var foreignKeys = await database.QueryScalarsAsync(
            "SELECT \"table\" FROM pragma_foreign_key_list('Grades')");
        Assert.Contains("Courses", foreignKeys);
    }

    [Fact]
    public async Task PreliminaryMigrationHistoryIsRenamedBeforeTheUpgrade()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync(migrate: false);
        await database.MigrateAsync("20261104000100_InitialCreate");

        await using (var db = database.CreateContext())
        {
            db.Courses.Add(new CourseEntity { Id = "course-1", Name = "Analisi", Cfu = 6 });
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE __EFMigrationsHistory SET MigrationId = '202611040001_InitialCreate' " +
                "WHERE MigrationId = '20261104000100_InitialCreate'");
        }

        await using (var db = database.CreateContext())
            await StudyHubDatabase.UpgradePreliminaryMigrationHistoryAsync(db);

        await database.MigrateAsync();

        var history = await database.QueryScalarsAsync(
            "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId");
        Assert.Contains("20261104000100_InitialCreate", history);
        Assert.Contains("20261104000200_AddCourseRelationships", history);
        Assert.DoesNotContain("202611040001_InitialCreate", history);

        await using var check = database.CreateContext();
        Assert.Equal("Analisi", (await check.Courses.SingleAsync()).Name);
    }

    [Fact]
    public async Task DeletingCareerExamCascadesAttempts()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();

        await using (var db = database.CreateContext())
        {
            db.Courses.Add(new CourseEntity { Id = "course-1", Name = "Analisi", Cfu = 6 });
            db.CareerExams.Add(new CareerExamEntity
            {
                Id = "career-1", CourseId = "course-1", Name = "Analisi", Cfu = 6,
                Attempts = new List<ExamAttemptEntity>
                {
                    new()
                    {
                        Id = "attempt-1", ExamId = "career-1",
                        AttemptedAt = DateTimeOffset.UtcNow,
                        Status = 5, Outcome = 0, GradeDecision = 0
                    }
                }
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var exam = await db.CareerExams.Include(x => x.Attempts).SingleAsync();
            db.CareerExams.Remove(exam);
            await db.SaveChangesAsync();
            Assert.Empty(await db.ExamAttempts.ToListAsync());
        }
    }

    [Fact]
    public async Task CourseWithDependentGradeCannotBeDeleted()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();

        await using (var db = database.CreateContext())
        {
            db.Courses.Add(new CourseEntity { Id = "course-1", Name = "Analisi", Cfu = 6 });
            db.Grades.Add(new GradeEntity
            {
                Id = "grade-1", CourseId = "course-1", CourseName = "Analisi",
                Value = 28, Cfu = 6, Date = new DateTime(2026, 1, 10)
            });
            await db.SaveChangesAsync();
        }

        await using (var db = database.CreateContext())
        {
            var course = await db.Courses.SingleAsync();
            db.Courses.Remove(course);
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
    }

    [Fact]
    public async Task ConcurrentSavesAreSerializedAndLeaveAConsistentDataset()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var store = Store(database);

        var saves = Enumerable.Range(0, 8).Select(async index =>
        {
            var data = TestStudyData.CreateValid();
            data.Courses = new System.Collections.ObjectModel.ObservableCollection<Course>
            {
                new() { Id = $"course-{index}", Name = $"Corso {index}", Cfu = 6 }
            };
            data.Grades.Clear();
            data.Exams.Clear();
            data.Announcements.Clear();
            data.CareerExams.Clear();
            await store.SaveAsync(data);
        });

        await Task.WhenAll(saves);

        var loaded = await store.LoadAsync();
        Assert.Single(loaded.Courses);
        Assert.StartsWith("course-", loaded.Courses[0].Id);
        Assert.Empty(loaded.Grades);
        Assert.Empty(loaded.CareerExams);
    }
}
