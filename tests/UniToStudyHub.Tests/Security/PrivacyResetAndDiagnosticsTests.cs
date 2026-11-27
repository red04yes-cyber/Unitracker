using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Text;
using UniToStudyHub.Domain.Enums;
using UniToStudyHub.Models;
using UniToStudyHub.Security;
using UniToStudyHub.Services;
using UniToStudyHub.Tests.Infrastructure;
using Xunit;

namespace UniToStudyHub.Tests.Security;

[Collection("AppDiagnostics")]
public sealed class PrivacyResetAndDiagnosticsTests
{
    private const string Token = "abcdef0123456789abcdef0123456789";

    [RequiresSymbolicLinksFact]
    public async Task PrivacyInventoryAndDocumentsResetRejectMoodleRootLink
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-moodle-link-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(root, "outside");
        var linkedRoot = Path.Combine(root, "managed-moodle");
        var externalFile = Path.Combine(target, "materiale.pdf");
        var externalEmptyDirectory = Path.Combine(target, "empty");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(externalEmptyDirectory);
        File.WriteAllText(externalFile, "outside content");
        Directory.CreateSymbolicLink(linkedRoot, target);

        try
        {
            await using var database = await SqliteTestDatabase.CreateFileAsync();
            var service = new PrivacyService(
                new SqliteStudyDataStore(database.Factory),
                moodleFilesDirectory: linkedRoot);

            var inventory = await service.DescribeAsync();
            Assert.Equal(0, inventory.MoodleFiles);

            var reset = await service.ResetAsync(PrivacyResetScope.Documents, confirmed: true);
            Assert.Equal(0, reset.DeletedFiles);
            Assert.Equal("outside content", await File.ReadAllTextAsync(externalFile));
            Assert.True(Directory.Exists(externalEmptyDirectory));
            Assert.NotNull(new DirectoryInfo(linkedRoot).LinkTarget);
        }
        finally
        {
            DeleteDirectoryLink(linkedRoot);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [RequiresSymbolicLinksFact]
    public async Task PrivacyInventoryAndDocumentsResetSkipLinkedFilesAndSubdirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-moodle-links-" + Guid.NewGuid().ToString("N"));
        var managed = Path.Combine(root, "managed");
        var outside = Path.Combine(root, "outside");
        var linkedDirectory = Path.Combine(managed, "linked-directory");
        var linkedFile = Path.Combine(managed, "linked-file.pdf");
        var managedFile = Path.Combine(managed, "managed.pdf");
        var externalFile = Path.Combine(outside, "external.pdf");
        Directory.CreateDirectory(managed);
        Directory.CreateDirectory(outside);
        File.WriteAllText(managedFile, "managed");
        File.WriteAllText(externalFile, "external");
        Directory.CreateSymbolicLink(linkedDirectory, outside);
        File.CreateSymbolicLink(linkedFile, externalFile);

        try
        {
            await using var database = await SqliteTestDatabase.CreateFileAsync();
            var service = new PrivacyService(
                new SqliteStudyDataStore(database.Factory),
                moodleFilesDirectory: managed);

            var inventory = await service.DescribeAsync();
            Assert.Equal(1, inventory.MoodleFiles);

            var reset = await service.ResetAsync(PrivacyResetScope.Documents, confirmed: true);
            Assert.Equal(1, reset.DeletedFiles);
            Assert.False(File.Exists(managedFile));
            Assert.Equal("external", await File.ReadAllTextAsync(externalFile));
            Assert.True(Directory.Exists(outside));
            Assert.NotNull(new DirectoryInfo(linkedDirectory).LinkTarget);
            Assert.NotNull(new FileInfo(linkedFile).LinkTarget);
        }
        finally
        {
            DeleteDirectoryLink(linkedDirectory);
            File.Delete(linkedFile);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void DeleteDirectoryLink(string path)
    {
        try
        {
            if (new DirectoryInfo(path).LinkTarget is not null)
                Directory.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task StudyLabResetRequiresConfirmationAndPreservesCareerData()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var data = new StudyData
        {
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "course-1", Name = "Analisi", Cfu = 6 }
            },
            Grades = new ObservableCollection<Grade>
            {
                new() { Id = "grade-1", CourseId = "course-1", CourseName = "Analisi", Value = 28, Cfu = 6 }
            }
        };
        data.StudyActivities.Add(new StudyActivityData
        {
            Id = "activity-1",
            Type = StudyActivityType.Study,
            Title = "Sessione",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            EndedAt = DateTimeOffset.UtcNow,
            TimeZoneId = "UTC",
            EffectiveSeconds = 1800
        });
        await store.SaveAsync(data);
        var service = new PrivacyService(store);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResetAsync(PrivacyResetScope.StudyLab, confirmed: false));
        Assert.Single((await store.LoadAsync()).StudyActivities);

        var result = await service.ResetAsync(PrivacyResetScope.StudyLab, confirmed: true);
        var saved = await store.LoadAsync();

        Assert.Equal(1, result.DeletedRecords);
        Assert.Empty(saved.StudyActivities);
        Assert.Single(saved.Courses);
        Assert.Single(saved.Grades);
    }

    [Fact]
    public async Task MoodleResetRemovesTokenAndReferencesButPreservesGrades()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var tokens = new InMemoryMoodleTokenStore();
        tokens.Save(Token);
        await store.SaveAsync(new StudyData
        {
            MoodleBaseUrl = "https://moodle.example.edu",
            Courses = new ObservableCollection<Course>
            {
                new() { Id = "course-1", Name = "Analisi", Cfu = 6, MoodleCourseId = 42 }
            },
            Grades = new ObservableCollection<Grade>
            {
                new() { Id = "grade-1", CourseId = "course-1", CourseName = "Analisi", Value = 28, Cfu = 6 }
            }
        });
        var service = new PrivacyService(store, tokens);

        var result = await service.ResetAsync(PrivacyResetScope.MoodleConnection, confirmed: true);
        var saved = await store.LoadAsync();

        Assert.True(result.CredentialsRemoved);
        Assert.False(tokens.HasToken);
        Assert.Equal(string.Empty, saved.MoodleBaseUrl);
        Assert.Null(saved.Courses.Single().MoodleCourseId);
        Assert.Single(saved.Grades);
    }

    [Fact]
    public async Task DiagnosticBundleRequiresConsentAndContainsOnlyRedactedLogs()
    {
        var previousDirectory = AppDiagnostics.Directory;
        var previousEnabled = AppDiagnostics.IsEnabled;
        var logs = Path.Combine(Path.GetTempPath(), "unitracker-diagnostics-" + Guid.NewGuid().ToString("N"));
        try
        {
            AppDiagnostics.Configure(logs);
            AppDiagnostics.IsEnabled = true;
            AppDiagnostics.Info("privacy",
                $"token={Token}; email=student@example.edu; path=C:\\Users\\Student\\Documents\\private.txt");

            var service = new PrivacyService(new InMemoryStudyDataStore());
            var preview = service.PreviewDiagnosticBundle();
            Assert.Equal(1, preview.AvailableLogFiles);
            Assert.False(preview.IncludesCredentials);
            Assert.False(preview.IncludesPersonalDatabase);

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.CreateDiagnosticBundleAsync(userConsent: false, includeLogs: true));

            var bytes = await service.CreateDiagnosticBundleAsync(userConsent: true, includeLogs: true);
            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            var logEntry = Assert.Single(archive.Entries, x => x.FullName == "redacted-application.log");
            using var reader = new StreamReader(logEntry.Open(), Encoding.UTF8);
            var log = await reader.ReadToEndAsync();

            Assert.DoesNotContain(Token, log, StringComparison.Ordinal);
            Assert.DoesNotContain("student@example.edu", log, StringComparison.Ordinal);
            Assert.DoesNotContain("Student", log, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", log, StringComparison.Ordinal);
            Assert.Contains("diagnostics.json", archive.Entries.Select(x => x.FullName));
        }
        finally
        {
            AppDiagnostics.IsEnabled = previousEnabled;
            AppDiagnostics.Configure(previousDirectory);
            if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
        }
    }

    [Fact]
    public async Task CompleteDeletionRevokesPersistedAiConsent()
    {
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var store = new SqliteStudyDataStore(database.Factory);
        var preferencesDirectory = Path.Combine(Path.GetTempPath(), "unitracker-ai-prefs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(preferencesDirectory);
        try
        {
            var preferences = new AiProviderPreferencesStore(preferencesDirectory);
            preferences.Save(new AiProviderPreferences("gemini", "gemini-2.5-flash", true));
            var service = new PrivacyService(store, aiPreferences: preferences);

            await service.DeleteAllAsync(includeBackups: false);

            var saved = Assert.IsType<AiProviderPreferences>(preferences.Load());
            Assert.Equal("gemini", saved.Provider);
            Assert.Equal("gemini-2.5-flash", saved.Model);
            Assert.False(saved.ConsentGranted);
        }
        finally
        {
            if (Directory.Exists(preferencesDirectory))
                Directory.Delete(preferencesDirectory, recursive: true);
        }
    }

    private sealed class InMemoryStudyDataStore : IStudyDataStore
    {
        public Task<StudyData> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudyData());
        public Task SaveAsync(StudyData data, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
public sealed class RequiresSymbolicLinksFactAttribute : FactAttribute
{
    public RequiresSymbolicLinksFactAttribute()
    {
        if (!SymbolicLinkTestSupport.IsSupported)
            Skip = "The test environment does not permit file and directory symbolic links.";
    }
}

internal static class SymbolicLinkTestSupport
{
    private static readonly Lazy<bool> SupportProbe = new(Probe);

    public static bool IsSupported => SupportProbe.Value;

    private static bool Probe()
    {
        var root = Path.Combine(Path.GetTempPath(), "unitracker-link-probe-" + Guid.NewGuid().ToString("N"));
        var targetDirectory = Path.Combine(root, "target");
        var directoryLink = Path.Combine(root, "directory-link");
        var targetFile = Path.Combine(targetDirectory, "target.txt");
        var fileLink = Path.Combine(root, "file-link.txt");
        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(targetFile, "probe");
            Directory.CreateSymbolicLink(directoryLink, targetDirectory);
            File.CreateSymbolicLink(fileLink, targetFile);
            return new DirectoryInfo(directoryLink).LinkTarget is not null &&
                   new FileInfo(fileLink).LinkTarget is not null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   PlatformNotSupportedException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            try { File.Delete(fileLink); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(directoryLink); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
            catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

