using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class LegacyJsonMigratorValidationTests
{
    private static (string Folder, string Path) CreateTemp()
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return (folder, Path.Combine(folder, "data.json"));
    }

    private static void DeleteTemp(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string Serialize(StudyData data) => JsonSerializer.Serialize(data);

    [Fact]
    public async Task EmptyObjectImportsTheDefaultProfileExactlyOnce()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            await File.WriteAllTextAsync(path, "{}");
            var migrator = new LegacyJsonMigrator(database.Factory, path);

            Assert.True(await migrator.MigrateAsync());
            Assert.False(await migrator.MigrateAsync());

            await using var db = database.CreateContext();
            var settings = await db.ProfileSettings.SingleAsync();
            Assert.Equal(new DateTime(2027, 10, 1), settings.GraduationTarget);
            Assert.Equal(string.Empty, settings.MoodleBaseUrl);
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Single(await db.LegacyImports.ToListAsync());
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task DuplicateAttemptIdsAcrossExamsAreRejectedAndSourceIsPreserved()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            var data = TestStudyData.CreateValid();
            data.CareerExams.Add(new CareerExamData
            {
                Id = "career-2", CourseId = "course-2", Name = "Fisica", Cfu = 9,
                Attempts = new ObservableCollection<ExamAttemptData>
                {
                    new()
                    {
                        Id = "attempt-1",
                        AttemptedAt = new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.Zero),
                        Status = "Completed",
                        Outcome = "Passed",
                        GradeDecision = "Accepted",
                        NumericGrade = 30,
                        RecordedAt = new DateTimeOffset(2026, 2, 11, 10, 0, 0, TimeSpan.Zero)
                    }
                }
            });

            var json = Serialize(data);
            await File.WriteAllTextAsync(path, json);
            var migrator = new LegacyJsonMigrator(database.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.Equal(json, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(folder, "data.pre-sqlite-*.json"));
            await using var db = database.CreateContext();
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Empty(await db.LegacyImports.ToListAsync());
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task OrphanGradeReferenceIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            var data = TestStudyData.CreateValid();
            data.Grades.Add(new Grade
            {
                Id = "grade-orphan", CourseId = "missing-course", CourseName = "Orfana",
                Value = 25, Cfu = 6, Date = new DateTime(2026, 1, 12)
            });
            await File.WriteAllTextAsync(path, Serialize(data));

            var migrator = new LegacyJsonMigrator(database.Factory, path);
            var ex = await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.Contains("missing-course", ex.InnerException?.Message ?? ex.Message);
            Assert.True(File.Exists(path));
            await using var db = database.CreateContext();
            Assert.Empty(await db.Courses.ToListAsync());
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task ScheduledAttemptCarryingAResultIsRejected()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            var data = TestStudyData.CreateValid();
            data.CareerExams[0].Attempts.Add(new ExamAttemptData
            {
                Id = "attempt-scheduled", AttemptedAt = DateTimeOffset.UtcNow,
                Status = "Scheduled", Outcome = "Pending", GradeDecision = "Pending",
                NumericGrade = 28
            });
            await File.WriteAllTextAsync(path, Serialize(data));

            var migrator = new LegacyJsonMigrator(database.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            await using var db = database.CreateContext();
            Assert.Empty(await db.CareerExams.ToListAsync());
            Assert.Empty(await db.LegacyImports.ToListAsync());
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task DatabaseFailureRollsBackTheWholeImportAndKeepsTheFile()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "CREATE TRIGGER fail_courses BEFORE INSERT ON Courses BEGIN SELECT RAISE(ABORT, 'simulated'); END;");
        }

        var (folder, path) = CreateTemp();
        try
        {
            var json = Serialize(TestStudyData.CreateValid());
            await File.WriteAllTextAsync(path, json);
            var migrator = new LegacyJsonMigrator(database.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.Equal(json, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(folder, "data.pre-sqlite-*.json"));
            await using var db = database.CreateContext();
            Assert.Empty(await db.ProfileSettings.ToListAsync());
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Empty(await db.ExamAttempts.ToListAsync());
            Assert.Empty(await db.LegacyImports.ToListAsync());
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task SuccessfulImportPreservesOriginalBytesAndCreatesAnIdenticalBackup()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(TestStudyData.CreateValid());
            await File.WriteAllBytesAsync(path, bytes);
            var migrator = new LegacyJsonMigrator(database.Factory, path);

            Assert.True(await migrator.MigrateAsync());

            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            var backup = Assert.Single(Directory.GetFiles(folder, "data.pre-sqlite-*.json"));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(backup));

            await using var db = database.CreateContext();
            var marker = await db.LegacyImports.SingleAsync();
            Assert.Equal(LegacyJsonMigrator.MigrationKey, marker.Key);
            Assert.Equal(64, marker.SourceHash.Length);
        }
        finally
        {
            DeleteTemp(folder);
        }
    }

    [Fact]
    public async Task NullCollectionsAreNormalizedAndImported()
    {
        await using var database = await SqliteTestDatabase.CreateInMemoryAsync();
        var (folder, path) = CreateTemp();
        try
        {
            const string json = """
                {
                  "Courses": null,
                  "Grades": null,
                  "Exams": null,
                  "Announcements": null,
                  "CareerExams": null,
                  "GraduationTarget": "2027-10-01T00:00:00",
                  "MoodleBaseUrl": "https://moodle.example.edu"
                }
                """;
            await File.WriteAllTextAsync(path, json);
            var migrator = new LegacyJsonMigrator(database.Factory, path);

            Assert.True(await migrator.MigrateAsync());

            await using var db = database.CreateContext();
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Equal("https://moodle.example.edu", (await db.ProfileSettings.SingleAsync()).MoodleBaseUrl);
        }
        finally
        {
            DeleteTemp(folder);
        }
    }
}
