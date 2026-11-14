using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

[Collection("AppDiagnostics")]
public sealed class LegacyJsonMigratorTests
{
    [Fact]
    public async Task Migration_ImportsAllLegacyCollections_AndKeepsSource()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var temp = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "data.json");
        try
        {
            var data = new StudyData
            {
                GraduationTarget = new DateTime(2027, 10, 1),
                MoodleBaseUrl = "https://moodle.example.edu",
                Courses = new ObservableCollection<Course>
                {
                    new() { Id = "duplicate-course", Name = "Analisi", Code = "MAT1", Cfu = 6 },
                    new() { Id = "course-2", Name = "Fisica", Code = "FIS1", Cfu = 9 }
                },
                Grades = new ObservableCollection<Grade>
                {
                    new() { Id = "grade-1", CourseId = "duplicate-course", CourseName = "Analisi", Value = 28, Cfu = 6, Date = new DateTime(2026, 1, 10) }
                },
                Exams = new ObservableCollection<Exam>
                {
                    new() { Id = "planned-1", CourseId = "duplicate-course", CourseName = "Analisi", Name = "Analisi", Cfu = 6, EstimatedStudyHours = 80 }
                },
                Announcements = new ObservableCollection<Announcement>
                {
                    new() { Id = "announcement-1", SourceKey = "moodle:1", CourseName = "Analisi", Title = "Materiale nuovo", Summary = "Dispense", Url = "https://moodle.example.edu/mod/resource/view.php?id=1", UpdatedAt = new DateTime(2026, 1, 10), IsNew = true }
                },
                CareerExams = new ObservableCollection<CareerExamData>
                {
                    new()
                    {
                        Id = "career-exam-1", CourseId = "duplicate-course", Name = "Analisi", Cfu = 6,
                        Attempts = new ObservableCollection<ExamAttemptData>
                        {
                            new() { Id = "attempt-1", AttemptedAt = new DateTimeOffset(2026, 1, 10, 10, 0, 0, TimeSpan.Zero), Status = "Completed", Outcome = "Passed", GradeDecision = "Accepted", NumericGrade = 30, IsHonors = true, RecordedAt = new DateTimeOffset(2026, 1, 11, 10, 0, 0, TimeSpan.Zero) }
                        }
                    }
                }
            };
            var json = JsonSerializer.Serialize(data);
            await File.WriteAllTextAsync(path, json);
            var migrator = new LegacyJsonMigrator(fixture.Factory, path);

            Assert.True(await migrator.MigrateAsync());

            await using var db = fixture.CreateContext();
            Assert.Equal(2, await db.Courses.CountAsync());
            Assert.Equal(1, await db.Grades.CountAsync());
            Assert.Equal(1, await db.PlannedExams.CountAsync());
            Assert.Equal(1, await db.Announcements.CountAsync());
            Assert.Equal(1, await db.CareerExams.CountAsync());
            Assert.Equal(1, await db.ExamAttempts.CountAsync());
            var settings = await db.ProfileSettings.SingleAsync();
            Assert.Equal(data.GraduationTarget, settings.GraduationTarget);
            Assert.Equal(data.MoodleBaseUrl, settings.MoodleBaseUrl);
            Assert.True(File.Exists(path));
            Assert.Single(Directory.GetFiles(temp, "data.pre-sqlite-*.json"));

            Assert.False(await migrator.MigrateAsync());
            Assert.Single(Directory.GetFiles(temp, "data.pre-sqlite-*.json"));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task CorruptJson_ThrowsAndLeavesOriginalUntouched()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var temp = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "data.json");
        const string invalid = "{not-valid-json";
        try
        {
            await File.WriteAllTextAsync(path, invalid);
            var migrator = new LegacyJsonMigrator(fixture.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.Equal(invalid, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(temp, "data.pre-sqlite-*.json"));
            await using var db = fixture.CreateContext();
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Empty(await db.LegacyImports.ToListAsync());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public void DuplicateLegacyIdentifiersAreRejectedWithoutRewritingTheSourceData()
    {
        const string duplicateId = "duplicate-course";
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = duplicateId, Name = "Analisi", Cfu = 6 },
                new() { Id = duplicateId, Name = "Fisica", Cfu = 9 }
            }
        };

        Assert.Throws<InvalidDataException>(() => StudyDataEntityMapper.NormalizeAndValidate(data));
        Assert.All(data.Courses, course => Assert.Equal(duplicateId, course.Id));
    }

    [Fact]
    public async Task EmptyJsonIsRejectedAndOriginalIsPreserved()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var temp = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "data.json");
        try
        {
            await File.WriteAllTextAsync(path, string.Empty);
            var migrator = new LegacyJsonMigrator(fixture.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.True(File.Exists(path));
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(path));
            Assert.Single(Directory.GetFiles(temp, "data.pre-sqlite-*.json"));
            await using var db = fixture.CreateContext();
            Assert.Empty(await db.ProfileSettings.ToListAsync());
            Assert.Empty(await db.Courses.ToListAsync());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    [Fact]
    public async Task OversizedLegacyFileIsRejectedAndOriginalIsPreserved()
    {
        await using var fixture = await DatabaseFixture.CreateAsync();
        var temp = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "data.json");
        try
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                stream.SetLength(LegacyJsonMigrator.MaximumFileBytes + 1);
            var migrator = new LegacyJsonMigrator(fixture.Factory, path);

            await Assert.ThrowsAsync<InvalidDataException>(() => migrator.MigrateAsync());

            Assert.True(File.Exists(path));
            Assert.Equal(LegacyJsonMigrator.MaximumFileBytes + 1, new FileInfo(path).Length);
            Assert.Empty(Directory.GetFiles(temp, "data.pre-sqlite-*.json"));
            await using var db = fixture.CreateContext();
            Assert.Empty(await db.Courses.ToListAsync());
            Assert.Empty(await db.LegacyImports.ToListAsync());
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private sealed class DatabaseFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<StudyHubDbContext> _options;
        public IDbContextFactory<StudyHubDbContext> Factory { get; }

        private DatabaseFixture(SqliteConnection connection, DbContextOptions<StudyHubDbContext> options)
        {
            _connection = connection;
            _options = options;
            Factory = new TestContextFactory(options);
        }

        public StudyHubDbContext CreateContext() => new(_options);

        public static async Task<DatabaseFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<StudyHubDbContext>().UseSqlite(connection).Options;
            var fixture = new DatabaseFixture(connection, options);
            await using var db = fixture.CreateContext();
            await db.Database.MigrateAsync();
            return fixture;
        }

        public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

        private sealed class TestContextFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
        {
            public StudyHubDbContext CreateDbContext() => new(options);
            public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
                Task.FromResult(CreateDbContext());
        }
    }
}