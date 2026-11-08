using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using UniToStudyHub.Tests.Security;
using Xunit;

namespace UniToStudyHub.Tests.Services;

public sealed class StudyDataImportServiceTests
{
    private static StudyData Data(params (string Id, string Name)[] courses)
    {
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>(courses.Select(x =>
                new Course { Id = x.Id, Name = x.Name, Cfu = 6 }))
        };
        return data;
    }

    private static byte[] Export(StudyData data) =>
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = PrivacyService.ExportSchemaVersion,
            generatedAt = DateTimeOffset.Parse("2026-11-08T12:00:00Z"),
            data
        });

    [Fact]
    public async Task PreviewReportsCountsAndExistingIdConflictsWithoutChangingTheStore()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data(("course-1", "Locale")));
        var service = new StudyDataImportService(store);

        var preview = await service.PreviewAsync(Export(Data(
            ("course-1", "Esportato"),
            ("course-2", "Nuovo"))));

        Assert.Equal(2, preview.ItemCounts["Corsi"]);
        Assert.Equal(1, preview.DuplicateCounts["Corsi"]);
        Assert.Contains(preview.Warnings, warning => warning.Contains("non sovrascriveranno", StringComparison.Ordinal));
        Assert.Equal("Locale", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task MergeAddsMissingRecordsAndPreservesExistingRecordsAndSettings()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var existing = Data(("course-1", "Locale"));
        existing.DegreeProgramName = "Informatica";
        existing.MoodleBaseUrl = "https://moodle.local.example";
        await store.SaveAsync(existing);

        var imported = Data(("course-1", "Esportato"), ("course-2", "Nuovo"));
        imported.Grades.Add(new Grade
        {
            Id = "grade-2", CourseId = "course-2", CourseName = "Nuovo",
            Value = 28, Cfu = 6, Date = new DateTime(2026, 6, 1)
        });
        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(imported));

        var result = await service.ApplyAsync(preview, StudyDataImportStrategy.Merge);

        var saved = await store.LoadAsync();
        Assert.Equal(2, saved.Courses.Count);
        Assert.Equal("Locale", saved.Courses.Single(x => x.Id == "course-1").Name);
        Assert.Equal("Nuovo", saved.Courses.Single(x => x.Id == "course-2").Name);
        Assert.Single(saved.Grades);
        Assert.Equal("Informatica", saved.DegreeProgramName);
        Assert.Equal("https://moodle.local.example", saved.MoodleBaseUrl);
        Assert.Equal(3, result.AddedRecords);
        Assert.Null(result.SafetyBackupPath);
    }

    [Fact]
    public async Task ReplaceCreatesSafetyBackupBeforeReplacingTheDatabase()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(Data(("course-local", "Locale")));
        var backupService = new BackupService(database.Factory, path,
            Path.Combine(Path.GetDirectoryName(path)!, "backups"), new TestBackupKeyProtector());
        var service = new StudyDataImportService(store, backupService);
        var preview = await service.PreviewAsync(Export(Data(("course-import", "Importato"))));

        var result = await service.ApplyAsync(preview, StudyDataImportStrategy.Replace,
            "test passphrase");

        Assert.NotNull(result.SafetyBackupPath);
        Assert.True(File.Exists(result.SafetyBackupPath));
        Assert.Equal("Importato", (await store.LoadAsync()).Courses.Single().Name);

        await backupService.RestoreAsync(result.SafetyBackupPath!, "test passphrase");
        Assert.Equal("Locale", (await store.LoadAsync()).Courses.Single().Name);
    }

    [Fact]
    public async Task ReplaceIsRejectedWhenNoBackupServiceIsConfigured()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var service = new StudyDataImportService(store);
        var preview = await service.PreviewAsync(Export(Data(("course-1", "Importato"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(preview, StudyDataImportStrategy.Replace));
        Assert.Empty((await store.LoadAsync()).Courses);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"schemaVersion\":\"99.0\",\"data\":{}}")]
    [InlineData("{\"schemaVersion\":\"1.0\",\"data\":null}")]
    public async Task RejectsMalformedOrUnsupportedExport(string json)
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var service = new StudyDataImportService(new SqliteStudyDataStore(database.Factory));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.PreviewAsync(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task RejectsInvalidRelationshipsAndOversizedPayloads()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var service = new StudyDataImportService(new SqliteStudyDataStore(database.Factory));
        var invalid = Data(("course-1", "Analisi"));
        invalid.Grades.Add(new Grade
        {
            Id = "grade-1", CourseId = "missing", Value = 28, Cfu = 6
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(Export(invalid)));

        var oversized = new byte[StudyDataImportService.MaximumImportBytes + 1];
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PreviewAsync(oversized));
    }
}
