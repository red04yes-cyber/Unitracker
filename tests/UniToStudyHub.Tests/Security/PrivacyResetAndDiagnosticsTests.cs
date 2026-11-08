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

    private sealed class InMemoryStudyDataStore : IStudyDataStore
    {
        public Task<StudyData> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new StudyData());
        public Task SaveAsync(StudyData data, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
