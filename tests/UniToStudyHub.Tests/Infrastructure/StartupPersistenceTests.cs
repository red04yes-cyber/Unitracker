using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Services;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class StartupPersistenceTests
{
    [Fact]
    public async Task SecondStartupReopensDatabaseWithoutReimportingOrLosingData()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var folder = Path.GetDirectoryName(database.DatabasePath)
            ?? throw new InvalidOperationException("The temporary database has no folder.");
        var legacyPath = Path.Combine(folder, "data.json");
        var originalBytes = JsonSerializer.SerializeToUtf8Bytes(TestStudyData.CreateValid());
        await File.WriteAllBytesAsync(legacyPath, originalBytes);

        // First startup: schema is already migrated by the fixture, then the
        // legacy import and the initial read run against the file database.
        var firstMigrator = new LegacyJsonMigrator(database.Factory, legacyPath);
        Assert.True(await firstMigrator.MigrateAsync());
        var firstStore = new SqliteStudyDataStore(database.Factory);
        Assert.Equal(2, (await firstStore.LoadAsync()).Courses.Count);

        // Simulate the next process start: migrate again, create new services,
        // and verify that the marker prevents a second import.
        await database.MigrateAsync();
        var secondMigrator = new LegacyJsonMigrator(database.Factory, legacyPath);
        Assert.False(await secondMigrator.MigrateAsync());

        var secondStore = new SqliteStudyDataStore(database.Factory);
        var loaded = await secondStore.LoadAsync();

        Assert.Equal(new DateTime(2027, 10, 1), loaded.GraduationTarget);
        Assert.Equal("https://moodle.example.edu", loaded.MoodleBaseUrl);
        Assert.Equal(2, loaded.Courses.Count);
        Assert.Single(loaded.Grades);
        Assert.Single(loaded.Exams);
        Assert.Single(loaded.Announcements);
        Assert.Single(loaded.CareerExams);
        Assert.Single(loaded.CareerExams[0].Attempts);
        Assert.Equal(28, loaded.CareerExams[0].Attempts[0].NumericGrade);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(legacyPath));
        Assert.Single(Directory.GetFiles(folder, "data.pre-sqlite-*.json"));
        Assert.Single(await database.QueryScalarsAsync("SELECT Key FROM LegacyImports"));
    }
}
