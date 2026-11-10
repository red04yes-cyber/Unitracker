using System.IO;
using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Privacy defaults. There is no analytics, crash reporter or background
/// network call in this application: <see cref="TelemetryEnabled"/> is a
/// constant so a future change has to be explicit and documented.
/// </summary>
public static class PrivacyDefaults
{
    public const bool TelemetryEnabled = false;
    public const int MaximumBackups = 10;
    public static readonly TimeSpan BackupRetention = TimeSpan.FromDays(90);
}

public sealed record PrivacyInventory(
    int Courses,
    int Grades,
    int CareerExams,
    int CalendarEvents,
    int StudySessions,
    int StudyActivities,
    int PersonalNotes,
    int PdfDocuments,
    int PdfPages,
    int MoodleFiles,
    int Flashcards,
    int Questions,
    int ErrorJournalEntries,
    bool MoodleLinked,
    bool AiConfigured,
    bool TelemetryEnabled,
    string StorageDirectory,
    DatabaseProtectionStatus DatabaseProtection);

public sealed record PrivacyDeletionResult(
    int DeletedRecords,
    int DeletedBackups,
    bool CredentialsRemoved,
    int DeletedLegacyFiles,
    int DeletedLogFiles,
    int DeletedMoodleFiles);

/// <summary>
/// Local, user-driven privacy operations: inventory, complete export and
/// deletion. Nothing here talks to a remote service and no deleted content is
/// ever written to a log.
/// </summary>
public sealed class PrivacyService
{
    public const string ExportSchemaVersion = "1.0";

    private static readonly JsonSerializerOptions ExportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly IStudyDataStore _store;
    private readonly IMoodleTokenStore? _tokens;
    private readonly INotionTokenStore? _notionTokens;
    private readonly AiCredentialStore? _aiKeys;
    private readonly BackupService? _backups;
    private readonly string? _dataDirectory;
    private readonly string _moodleFilesDirectory;

    public PrivacyService(
        IStudyDataStore store,
        IMoodleTokenStore? tokens = null,
        AiCredentialStore? aiKeys = null,
        BackupService? backups = null,
        string? dataDirectory = null,
        string? moodleFilesDirectory = null,
        INotionTokenStore? notionTokens = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tokens = tokens;
        _notionTokens = notionTokens;
        _aiKeys = aiKeys;
        _backups = backups;
        _dataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? null : Path.GetFullPath(dataDirectory);
        _moodleFilesDirectory = Path.GetFullPath(moodleFilesDirectory ??
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "UniToStudyHub", "Moodle"));
    }

    public async Task<PrivacyInventory> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var data = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new PrivacyInventory(
            data.Courses.Count,
            data.Grades.Count,
            data.CareerExams.Count,
            data.CalendarEvents.Count,
            data.StudySessions.Count,
            data.StudyActivities.Count,
            CountNotes(data),
            data.PdfDocuments.Count,
            data.PdfDocuments.Sum(document => document.Pages.Count),
            CountMoodleFiles(),
            data.Flashcards.Count,
            data.Questions.Count,
            data.ErrorJournalEntries.Count,
            _tokens?.HasToken == true,
            _aiKeys?.List().Count > 0,
            PrivacyDefaults.TelemetryEnabled,
            _dataDirectory ?? StudyHubDatabase.GetDataDirectory(),
            StudyHubDatabase.DescribeProtection(_dataDirectory is null ? null : Path.Combine(_dataDirectory, "unitracker.db")));
    }

    /// <summary>
    /// Complete personal-data export, including private notes, because this is
    /// the user's own data and an export is the counterpart of deletion. No
    /// credential is present: tokens and AI keys live outside StudyData.
    /// </summary>
    public async Task<byte[]> ExportEverythingAsync(CancellationToken cancellationToken = default)
    {
        var data = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var inventory = await DescribeAsync(cancellationToken).ConfigureAwait(false);
        var document = new
        {
            schemaVersion = ExportSchemaVersion,
            generatedAt = DateTimeOffset.UtcNow,
            notice = "Esportazione completa dei dati locali. Contiene note private; non contiene token o chiavi API.",
            inventory,
            data
        };
        return JsonSerializer.SerializeToUtf8Bytes(document, ExportJson);
    }

    /// <summary>
    /// Deletes every personal record and, on request, the derived copies
    /// (backups, legacy JSON, logs). Credentials are always removed. The
    /// operation is not reversible; callers must confirm explicitly.
    /// </summary>
    public async Task<PrivacyDeletionResult> DeleteAllAsync(
        bool includeBackups,
        CancellationToken cancellationToken = default)
    {
        var data = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var records = CountRecords(data);

        // Replaces the dataset in one validated transaction instead of deleting
        // rows ad hoc, so a failure leaves the previous data intact.
        await _store.SaveAsync(new StudyData(), cancellationToken).ConfigureAwait(false);

        var credentialsRemoved = false;
        if (_tokens is not null && _tokens.Delete()) credentialsRemoved = true;
        if (_notionTokens is not null && _notionTokens.Delete()) credentialsRemoved = true;
        if (_aiKeys is not null && _aiKeys.DeleteAll() > 0) credentialsRemoved = true;

        var deletedBackups = 0;
        if (includeBackups && _backups is not null)
        {
            foreach (var backup in _backups.ListBackups())
            {
                LocalFileSecurity.ScrubAndDelete(backup.FilePath);
                LocalFileSecurity.TryDelete(BackupService.ManifestPath(backup.FilePath));
                if (!File.Exists(backup.FilePath)) deletedBackups++;
            }
            if (Directory.Exists(_backups.BackupDirectory))
            {
                LocalFileSecurity.CleanupStaleTemporaryFiles(_backups.BackupDirectory, TimeSpan.Zero,
                    "*.part", "*.tmp", "*.db");
            }
        }

        var deletedLegacy = DeleteLegacyFiles();
        var deletedMoodleFiles = DeleteMoodleFiles();
        var deletedLogs = AppDiagnostics.Clear();
        return new PrivacyDeletionResult(records, deletedBackups, credentialsRemoved, deletedLegacy, deletedLogs,
            deletedMoodleFiles);
    }

    /// <summary>Applies the documented retention to encrypted backups.</summary>
    public Task<int> ApplyRetentionAsync(CancellationToken cancellationToken = default) =>
        _backups is null
            ? Task.FromResult(0)
            : _backups.PruneAsync(PrivacyDefaults.MaximumBackups, PrivacyDefaults.BackupRetention, cancellationToken);

    private int CountMoodleFiles()
    {
        if (!Directory.Exists(_moodleFilesDirectory)) return 0;
        try { return Directory.EnumerateFiles(_moodleFilesDirectory, "*", SearchOption.AllDirectories).Count(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }
    }

    private int DeleteMoodleFiles()
    {
        if (!Directory.Exists(_moodleFilesDirectory)) return 0;
        var deleted = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(_moodleFilesDirectory, "*", SearchOption.AllDirectories).ToList())
            {
                if (!LocalFileSecurity.IsInside(_moodleFilesDirectory, file)) continue;
                LocalFileSecurity.ScrubAndDelete(file);
                if (!File.Exists(file)) deleted++;
            }
            foreach (var directory in Directory.EnumerateDirectories(_moodleFilesDirectory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(path => path.Length))
            {
                try { if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            if (!Directory.EnumerateFileSystemEntries(_moodleFilesDirectory).Any())
                Directory.Delete(_moodleFilesDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppDiagnostics.Warning("privacy", "Alcuni file Moodle non sono stati eliminati.");
        }
        return deleted;
    }

    private int DeleteLegacyFiles()
    {
        if (_dataDirectory is null || !Directory.Exists(_dataDirectory)) return 0;
        var patterns = new[]
        {
            "data.json",
            "data.json.invalid-*",
            "data.pre-sqlite-*.json",
            "calendar-events.json",
            "calendar-events.json.migrated"
        };
        var deleted = 0;
        foreach (var pattern in patterns)
        {
            IEnumerable<string> candidates;
            try
            {
                candidates = Directory.EnumerateFiles(_dataDirectory, pattern, SearchOption.TopDirectoryOnly)
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var candidate in candidates)
            {
                if (!LocalFileSecurity.IsInside(_dataDirectory, candidate)) continue;
                LocalFileSecurity.ScrubAndDelete(candidate);
                if (!File.Exists(candidate)) deleted++;
            }
        }
        return deleted;
    }

    private static int CountRecords(StudyData data) =>
        data.Courses.Count + data.Grades.Count + data.Exams.Count + data.Announcements.Count +
        data.CareerExams.Count + data.CoursePrerequisites.Count + data.SyllabusTopics.Count +
        data.Questions.Count + data.StudySessions.Count + data.StudyTasks.Count +
        data.StudyActivities.Count + data.StudyMissions.Count + data.PomodoroCycles.Count +
        data.ActivityRevisions.Count + data.ErrorJournalEntries.Count + data.Flashcards.Count +
        data.FlashcardReviews.Count + data.Distractions.Count + data.EnergyCheckIns.Count +
        data.PdfDocuments.Count + data.CalendarEvents.Count;

    private static int CountNotes(StudyData data) =>
        data.StudyActivities.Count(activity => !string.IsNullOrWhiteSpace(activity.Note)) +
        data.StudySessions.Count(session => !string.IsNullOrWhiteSpace(session.Notes)) +
        data.Courses.Count(course => !string.IsNullOrWhiteSpace(course.Notes)) +
        data.Flashcards.Count(card => !string.IsNullOrWhiteSpace(card.Back));
}
