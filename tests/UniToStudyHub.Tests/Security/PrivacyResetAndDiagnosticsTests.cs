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
            await File.WriteAllTextAsync(Path.Combine(logs, "unitracker.log.1"),
                $"rotated marker token={Token}");

            var service = new PrivacyService(new InMemoryStudyDataStore());
            var preview = service.PreviewDiagnosticBundle();
            Assert.Equal(2, preview.AvailableLogFiles);
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
            Assert.Contains("rotated marker", log, StringComparison.Ordinal);
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
    public void ClearDeletesCurrentAndBothRotatedLogs()
    {
        var previousDirectory = AppDiagnostics.Directory;
        var previousEnabled = AppDiagnostics.IsEnabled;
        var logs = Path.Combine(Path.GetTempPath(), "unitracker-clear-" + Guid.NewGuid().ToString("N"));
        var names = new[] { "unitracker.log", "unitracker.log.1", "unitracker.log.2" };
        try
        {
            AppDiagnostics.Configure(logs);
            AppDiagnostics.IsEnabled = true;
            Directory.CreateDirectory(logs);
            foreach (var name in names)
                File.WriteAllText(Path.Combine(logs, name), "redacted diagnostic data");

            Assert.Equal(3, AppDiagnostics.Clear());
            Assert.All(names, name => Assert.False(File.Exists(Path.Combine(logs, name))));
        }
        finally
        {
            AppDiagnostics.IsEnabled = previousEnabled;
            AppDiagnostics.Configure(previousDirectory);
            if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticsDoNotWriteOrScrubThroughSymlinkedLogFile()
    {
        var previousDirectory = AppDiagnostics.Directory;
        var previousEnabled = AppDiagnostics.IsEnabled;
        var root = Path.Combine(Path.GetTempPath(), "unitracker-log-link-" + Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "logs");
        var external = Path.Combine(root, "external.log");
        var link = Path.Combine(logs, "unitracker.log");
        const string original = "external file must remain byte-for-byte unchanged";
        try
        {
            Directory.CreateDirectory(logs);
            await File.WriteAllTextAsync(external, original);
            File.CreateSymbolicLink(link, external);
            AppDiagnostics.Configure(logs);
            AppDiagnostics.IsEnabled = true;

            AppDiagnostics.Info("security", "this message must not be appended through a link");

            Assert.Null(AppDiagnostics.CurrentLogPath);
            Assert.Empty(AppDiagnostics.GetManagedLogFiles());
            Assert.Equal(0, AppDiagnostics.Clear());
            Assert.Equal(original, await File.ReadAllTextAsync(external));
            Assert.NotNull(new FileInfo(link).LinkTarget);
        }
        finally
        {
            AppDiagnostics.IsEnabled = previousEnabled;
            AppDiagnostics.Configure(previousDirectory);
            if (new FileInfo(link).LinkTarget is not null) File.Delete(link);
            if (Directory.Exists(logs)) Directory.Delete(logs, recursive: true);
            if (File.Exists(external)) File.Delete(external);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task DiagnosticsDoNotFollowSymlinkedLogDirectory()
    {
        if (!OperatingSystem.IsLinux()) return;

        var previousDirectory = AppDiagnostics.Directory;
        var previousEnabled = AppDiagnostics.IsEnabled;
        var root = Path.Combine(Path.GetTempPath(), "unitracker-log-root-link-" + Guid.NewGuid().ToString("N"));
        var externalDirectory = Path.Combine(root, "external");
        var linkedDirectory = Path.Combine(root, "logs");
        var externalLog = Path.Combine(externalDirectory, "unitracker.log");
        const string original = "external log directory must remain unchanged";
        try
        {
            Directory.CreateDirectory(externalDirectory);
            await File.WriteAllTextAsync(externalLog, original);
            Directory.CreateSymbolicLink(linkedDirectory, externalDirectory);
            AppDiagnostics.Configure(linkedDirectory);
            AppDiagnostics.IsEnabled = true;

            AppDiagnostics.Info("security", "this message must not be written through a directory link");

            Assert.Null(AppDiagnostics.CurrentLogPath);
            Assert.Empty(AppDiagnostics.GetManagedLogFiles());
            Assert.Equal(0, AppDiagnostics.Clear());
            Assert.Equal(original, await File.ReadAllTextAsync(externalLog));
        }
        finally
        {
            AppDiagnostics.IsEnabled = previousEnabled;
            AppDiagnostics.Configure(previousDirectory);
            if (new DirectoryInfo(linkedDirectory).LinkTarget is not null)
            {
                if (OperatingSystem.IsWindows()) Directory.Delete(linkedDirectory);
                else File.Delete(linkedDirectory);
            }
            if (Directory.Exists(externalDirectory)) Directory.Delete(externalDirectory, recursive: true);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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
