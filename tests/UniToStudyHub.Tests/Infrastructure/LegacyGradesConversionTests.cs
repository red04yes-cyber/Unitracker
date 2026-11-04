using System.Collections.ObjectModel;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class LegacyGradesConversionTests
{
    [Fact]
    public async Task LegacyGrades_AreImportedAsOrderedExamAttempts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StudyHubDbContext>().UseSqlite(connection).Options;
        var factory = new TestFactory(options);
        await using (var db = new StudyHubDbContext(options))
            await db.Database.MigrateAsync();

        var temp = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var path = Path.Combine(temp, "data.json");
        try
        {
            var data = new StudyData
            {
                Courses = new ObservableCollection<Course> { new() { Id = "course-1", Name = "Analisi", Cfu = 6 } },
                Grades = new ObservableCollection<Grade>
                {
                    new() { Id = "grade-old", CourseId = "course-1", CourseName = "Analisi", Value = 21, Cfu = 6, Date = new DateTime(2026, 1, 10) },
                    new() { Id = "grade-new", CourseId = "course-1", CourseName = "Analisi", Value = 27, Cfu = 6, Date = new DateTime(2026, 2, 10) }
                }
            };
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(data));

            Assert.True(await new LegacyJsonMigrator(factory, path).MigrateAsync());

            await using var check = new StudyHubDbContext(options);
            var exam = await check.CareerExams.Include(x => x.Attempts).SingleAsync();
            Assert.Equal(2, exam.Attempts.Count);
            Assert.Equal(new[] { "grade-old", "grade-new" }, exam.Attempts.OrderBy(x => x.AttemptedAt).Select(x => x.Id));
            Assert.All(exam.Attempts, x => Assert.Equal("Completed", Enum.GetName(typeof(UniToStudyHub.Domain.Enums.AttemptStatus), x.Status)));
        }
        finally { Directory.Delete(temp, recursive: true); }
    }

    private sealed class TestFactory(DbContextOptions<StudyHubDbContext> options) : IDbContextFactory<StudyHubDbContext>
    {
        public StudyHubDbContext CreateDbContext() => new(options);
        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}