using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
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

public enum PrivacyResetScope
{
    StudyLab,
    Calendar,
    Documents,
    MoodleConnection
}

public sealed record PrivacyResetResult(
    PrivacyResetScope Scope,
    int DeletedRecords,
    int DeletedFiles,
    bool CredentialsRemoved,
    string? SafetyBackupPath);

public sealed record PrivacyDiagnosticPreview(
    DateTimeOffset GeneratedAt,
    string ApplicationVersion,
    string OperatingSystem,
    string Runtime,
    int AvailableLogFiles,
    long AvailableLogBytes,
    bool IncludesPersonalDatabase = false,
    bool IncludesCredentials = false);

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
        string? moodleFilesDirectory = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tokens = tokens;
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

    /// <summary>
    /// Preview metadata for an opt-in diagnostic bundle. No database content,
    /// credentials, user name, machine name or file-system path is included.
    /// </summary>
    public PrivacyDiagnosticPreview PreviewDiagnosticBundle()
    {
        var files = GetSafeDiagnosticLogFiles();
        var bytes = files.Sum(file => new FileInfo(file).Length);
        return new PrivacyDiagnosticPreview(
            DateTimeOffset.UtcNow,
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            SecurityRedaction.ForLog(RuntimeInformation.OSDescription, 160),
            RuntimeInformation.FrameworkDescription,
            files.Count,
            bytes);
    }

    /// <summary>
    /// Creates a redacted diagnostic archive only after explicit user consent.
    /// The archive contains app/runtime metadata and optionally local redacted
    /// logs; it never includes StudyData, database files, PDFs or credentials.
    /// </summary>
    public async Task<byte[]> CreateDiagnosticBundleAsync(
        bool userConsent,
        bool includeLogs,
        CancellationToken cancellationToken = default)
    {
        if (!userConsent)
            throw new InvalidOperationException("È necessario il consenso esplicito prima di creare il pacchetto diagnostico.");

        var preview = PreviewDiagnosticBundle();
        var metadata = new
        {
            schemaVersion = "1.0",
            generatedAt = preview.GeneratedAt,
            applicationVersion = preview.ApplicationVersion,
            operatingSystem = preview.OperatingSystem,
            runtime = preview.Runtime,
            includedLogFiles = includeLogs ? preview.AvailableLogFiles : 0,
            includesPersonalDatabase = false,
            includesCredentials = false,
            includesDocuments = false
        };

        await using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var metadataEntry = archive.CreateEntry("diagnostics.json", CompressionLevel.Optimal);
            await using (var stream = metadataEntry.Open())
                await JsonSerializer.SerializeAsync(stream, metadata, ExportJson, cancellationToken).ConfigureAwait(false);

            if (includeLogs)
            {
                var logs = GetSafeDiagnosticLogFiles();
                var combined = new StringBuilder();
                foreach (var logPath in logs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var info = new FileInfo(logPath);
                    if (info.Length > 512 * 1024 || combined.Length >= 2 * 1024 * 1024) continue;
                    var content = await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false);
                    foreach (var line in content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (combined.Length >= 2 * 1024 * 1024) break;
                        combined.AppendLine(SecurityRedaction.ForLog(line, 2000));
                    }
                }

                var logEntry = archive.CreateEntry("redacted-application.log", CompressionLevel.Optimal);
                await using var stream = logEntry.Open();
                await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                await writer.WriteAsync(combined.ToString().AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Selective, confirmed reset. The optional safety copy is created before
    /// any record or credential is removed; SQLite replacement remains atomic.
    /// </summary>
    public async Task<PrivacyResetResult> ResetAsync(
        PrivacyResetScope scope,
        bool confirmed,
        bool createSafetyBackup = false,
        string? backupPassword = null,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
            throw new InvalidOperationException("L'operazione richiede una conferma esplicita.");
        var data = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var before = CountRecords(data);
        string? safetyBackupPath = null;
        if (createSafetyBackup)
        {
            if (_backups is null)
                throw new InvalidOperationException("Il servizio di backup non è configurato.");
            var backup = await _backups.CreateBackupAsync(backupPassword, cancellationToken).ConfigureAwait(false);
            safetyBackupPath = backup.FilePath;
        }

        var credentialsRemoved = false;
        var deleteMoodleToken = false;
        var deleteManagedMoodleFiles = false;
        var deletedFiles = 0;
        switch (scope)
        {
            case PrivacyResetScope.StudyLab:
                data.StudySessions.Clear();
                data.StudyTasks.Clear();
                data.StudyActivities.Clear();
                data.StudyMissions.Clear();
                data.PomodoroTimer = null;
                data.PomodoroCycles.Clear();
                data.ActivityRevisions.Clear();
                data.ErrorJournalEntries.Clear();
                data.Flashcards.Clear();
                data.FlashcardReviews.Clear();
                data.Distractions.Clear();
                data.EnergyCheckIns.Clear();
                break;
            case PrivacyResetScope.Calendar:
                data.CalendarEvents.Clear();
                break;
            case PrivacyResetScope.Documents:
                data.PdfDocuments.Clear();
                deleteManagedMoodleFiles = true;
                break;
            case PrivacyResetScope.MoodleConnection:
                MoodleApplicationService.ClearLocalMoodleReferences(data);
                deleteMoodleToken = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scope));
        }

        await _store.SaveAsync(data, cancellationToken).ConfigureAwait(false);
        if (deleteMoodleToken) credentialsRemoved = _tokens?.Delete() == true;
        if (deleteManagedMoodleFiles) deletedFiles = DeleteMoodleFiles();
        var after = CountRecords(data);
        return new PrivacyResetResult(scope, Math.Max(0, before - after), deletedFiles,
            credentialsRemoved, safetyBackupPath);
    }

    private static IReadOnlyList<string> GetSafeDiagnosticLogFiles()
    {
        var root = Path.GetFullPath(AppDiagnostics.Directory);
        if (!Directory.Exists(root)) return Array.Empty<string>();
        try
        {
            return Directory.EnumerateFiles(root, "unitracker*.log", SearchOption.TopDirectoryOnly)
                .Where(logPath => LocalFileSecurity.IsInside(root, logPath))
                .Where(logPath => (File.GetAttributes(logPath) & FileAttributes.ReparsePoint) == 0)
                .Where(logPath => new FileInfo(logPath).Length <= 512 * 1024)
                .OrderBy(logPath => logPath, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return Array.Empty<string>();
        }
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
