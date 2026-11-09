using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using UniToStudyHub.Models;
using UniToStudyHub.Security;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Security;

[Collection("AppDiagnostics")]
public sealed class PrivacyAndMoodleSecurityTests
{
    private const string Token = "abcdef0123456789abcdef0123456789";
    private const string AiKey = "sk-test-0123456789abcdef";

    [Fact]
    public async Task InventoryReportsLocalDataAndTelemetryOff()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "c1", Name = "Analisi", Cfu = 6, Notes = "nota privata" }
            }
        });
        var tokens = new InMemoryMoodleTokenStore();
        tokens.Save(Token);
        var dataDirectory = Path.GetDirectoryName(Assert.IsType<string>(database.DatabasePath))!;
        var service = new PrivacyService(store, tokens, new AiCredentialStore(dataDirectory, new TestSecretProtector()),
            null, dataDirectory);

        var inventory = await service.DescribeAsync();

        Assert.Equal(1, inventory.Courses);
        Assert.Equal(1, inventory.PersonalNotes);
        Assert.True(inventory.MoodleLinked);
        Assert.False(inventory.TelemetryEnabled);
        Assert.Equal(dataDirectory, inventory.StorageDirectory);
    }

    [Fact]
    public async Task FullExportIncludesPrivateNotesButNoCredentials()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "c1", Name = "Analisi", Cfu = 6, Notes = "nota privata da esportare" }
            }
        });
        var dataDirectory = Path.GetDirectoryName(Assert.IsType<string>(database.DatabasePath))!;
        var tokens = new InMemoryMoodleTokenStore();
        tokens.Save(Token);
        var aiKeys = new AiCredentialStore(dataDirectory, new TestSecretProtector());
        aiKeys.SaveKey("default", AiKey);
        var service = new PrivacyService(store, tokens, aiKeys, null, dataDirectory);

        var json = Encoding.UTF8.GetString(await service.ExportEverythingAsync());

        Assert.Contains("nota privata da esportare", json, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, json, StringComparison.Ordinal);
        Assert.DoesNotContain(AiKey, json, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(json);
        Assert.Equal(PrivacyService.ExportSchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.False(document.RootElement.GetProperty("inventory").GetProperty("telemetryEnabled").GetBoolean());
    }

    [Fact]
    public async Task DeleteAllRemovesDataCredentialsBackupsAndLegacyFiles()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var dataDirectory = Path.GetDirectoryName(path)!;
        var backupsDirectory = Path.Combine(dataDirectory, "backups");
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "c1", Name = "Analisi", Cfu = 6, Notes = "nota privata" }
            }
        });
        var tokens = new InMemoryMoodleTokenStore();
        tokens.Save(Token);
        var aiKeys = new AiCredentialStore(dataDirectory, new TestSecretProtector());
        aiKeys.SaveKey("default", AiKey);
        var backups = new BackupService(database.Factory, path, backupsDirectory, new TestBackupKeyProtector());
        await backups.CreateBackupAsync("correct horse battery staple");

        var legacyJson = Path.Combine(dataDirectory, "data.json");
        var legacyBackup = Path.Combine(dataDirectory, "data.pre-sqlite-20260101.json");
        var legacyCalendar = Path.Combine(dataDirectory, "calendar-events.json");
        var moodleFilesDirectory = Path.Combine(dataDirectory, "Moodle");
        Directory.CreateDirectory(moodleFilesDirectory);
        var moodleFile = Path.Combine(moodleFilesDirectory, "dispensa.pdf");
        await File.WriteAllTextAsync(moodleFile, "contenuto Moodle");
        await File.WriteAllTextAsync(legacyJson, "{}");
        await File.WriteAllTextAsync(legacyBackup, "{}");
        await File.WriteAllTextAsync(legacyCalendar, "[]");
        AppDiagnostics.IsEnabled = true;
        var logsDirectory = Path.Combine(dataDirectory, "logs");
        AppDiagnostics.Configure(logsDirectory);
        AppDiagnostics.Info("privacy", "riga di prova");

        var service = new PrivacyService(store, tokens, aiKeys, backups, dataDirectory, moodleFilesDirectory);
        var result = await service.DeleteAllAsync(includeBackups: true);

        Assert.True(result.DeletedRecords > 0);
        Assert.True(result.CredentialsRemoved);
        Assert.Equal(1, result.DeletedBackups);
        Assert.True(result.DeletedLegacyFiles >= 3);
        Assert.True(result.DeletedLogFiles >= 1);
        Assert.Equal(1, result.DeletedMoodleFiles);
        Assert.False(File.Exists(moodleFile));
        Assert.False(Directory.Exists(moodleFilesDirectory));
        Assert.False(Directory.Exists(logsDirectory) && Directory.EnumerateFiles(logsDirectory).Any());
        Assert.Empty((await store.LoadAsync()).Courses);
        Assert.False(tokens.HasToken);
        Assert.Empty(aiKeys.List());
        Assert.Empty(backups.ListBackups());
        Assert.False(File.Exists(legacyJson));
        Assert.False(File.Exists(legacyBackup));
        Assert.False(File.Exists(legacyCalendar));
        Assert.Equal(0, (await service.DescribeAsync()).Courses);
    }

    [Fact]
    public async Task RetentionKeepsTheConfiguredNumberOfBackups()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        var store = new SqliteStudyDataStore(database.Factory);
        await store.SaveAsync(new StudyData
        {
            Courses = new ObservableCollection<Course> { new() { Id = "c1", Name = "Analisi", Cfu = 6 } }
        });
        var backups = new BackupService(database.Factory, path,
            Path.Combine(Path.GetDirectoryName(path)!, "backups"), new TestBackupKeyProtector());
        await backups.CreateBackupAsync("correct horse battery staple");
        await backups.CreateBackupAsync("correct horse battery staple");
        await backups.CreateBackupAsync("correct horse battery staple");
        var service = new PrivacyService(store, null, null, backups, Path.GetDirectoryName(path));

        var deleted = await service.ApplyRetentionAsync();

        Assert.Equal(0, deleted);
        Assert.Equal(3, backups.ListBackups().Count);
        Assert.True(PrivacyDefaults.MaximumBackups >= 3);
    }

    [Fact]
    public async Task DisconnectRemovesTheTokenAndPreventsReuse()
    {
        var tokens = new InMemoryMoodleTokenStore();
        tokens.Save(Token);
        var service = new MoodleApplicationService(tokens);
        Assert.True(service.IsConnected);

        Assert.True(await service.DisconnectAsync());

        Assert.False(service.IsConnected);
        Assert.Null(tokens.Load());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetEnrolledCoursesAsync("https://moodle.example.edu"));
    }

    [Fact]
    public async Task UnsafeMoodleBaseUrlsAreRejectedBeforeAnyRequest()
    {
        var service = new MoodleApplicationService(new InMemoryMoodleTokenStore());

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.VerifyAndStoreTokenAsync("http://moodle.example.edu", Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.VerifyAndStoreTokenAsync("https://user:pass@moodle.example.edu", Token));
        await Assert.ThrowsAsync<ArgumentException>(
            () => service.VerifyAndStoreTokenAsync("https://moodle.example.edu/webservice?token=x", Token));
    }

    [Fact]
    public void MoodleClientRejectsLoopbackUnlessExplicitlyOptedIn()
    {
        Assert.Throws<ArgumentException>(() =>
            new MoodleClient("http://127.0.0.1:8080", Token));
        Assert.Throws<ArgumentException>(() =>
            new MoodleClient("https://127.0.0.1:8443", Token));

        using var localDevelopmentClient = new MoodleClient(
            "http://127.0.0.1:8080",
            Token,
            new HttpClientHandler(),
            allowLoopbackHttp: true);

        Assert.True(localDevelopmentClient.BaseUri.IsLoopback);
    }

    [Fact]
    public void InvalidTokensAreRejected()
    {
        Assert.Throws<ArgumentException>(() => MoodleTokenVault.Normalize("short"));
        Assert.Throws<ArgumentException>(() => MoodleTokenVault.Normalize("token with spaces"));
        Assert.Throws<ArgumentException>(() => MoodleTokenVault.Normalize(""));
        Assert.Equal(Token, MoodleTokenVault.Normalize(" " + Token + " "));
    }

    [Fact]
    public void ClearLocalMoodleReferencesKeepsAcademicHistory()
    {
        var data = new StudyData
        {
            MoodleBaseUrl = "https://moodle.example.edu",
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "c1", Name = "Analisi", Cfu = 6, MoodleCourseId = 42 }
            },
            Grades = new ObservableCollection<Grade>
            {
                new() { Id = "g1", CourseId = "c1", CourseName = "Analisi", Value = 28, Cfu = 6 }
            }
        };

        Assert.True(MoodleApplicationService.ClearLocalMoodleReferences(data));
        Assert.Equal(string.Empty, data.MoodleBaseUrl);
        Assert.Null(data.Courses[0].MoodleCourseId);
        Assert.Single(data.Grades);
        Assert.False(MoodleApplicationService.ClearLocalMoodleReferences(data));
    }

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData("..\\..\\windows\\system32\\evil.exe", "evil.exe")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("bad\u0001name.pdf", "bad_name.pdf")]
    [InlineData("..", "Senza-nome")]
    [InlineData("", "Senza-nome")]
    public void DownloadedNamesCannotEscapeTheCourseFolder(string input, string expected)
    {
        Assert.Equal(expected, DocumentSyncService.SafeName(input));
    }
}
