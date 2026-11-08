using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public enum StudyDataImportStrategy
{
    Merge,
    Replace
}

public sealed class StudyDataImportPreview
{
    internal StudyDataImportPreview(
        StudyData importedData,
        DateTimeOffset? generatedAt,
        string payloadSha256,
        IReadOnlyDictionary<string, int> itemCounts,
        IReadOnlyDictionary<string, int> duplicateCounts,
        IReadOnlyList<string> warnings)
    {
        ImportedData = importedData;
        GeneratedAt = generatedAt;
        PayloadSha256 = payloadSha256;
        ItemCounts = itemCounts;
        DuplicateCounts = duplicateCounts;
        Warnings = warnings;
    }

    internal StudyData ImportedData { get; }
    public string SchemaVersion => PrivacyService.ExportSchemaVersion;
    public DateTimeOffset? GeneratedAt { get; }
    public string PayloadSha256 { get; }
    public IReadOnlyDictionary<string, int> ItemCounts { get; }
    public IReadOnlyDictionary<string, int> DuplicateCounts { get; }
    public IReadOnlyList<string> Warnings { get; }
    public int TotalItems => ItemCounts.Values.Sum();
    public int TotalDuplicates => DuplicateCounts.Values.Sum();
}

public sealed record StudyDataImportResult(
    StudyDataImportStrategy Strategy,
    int ImportedRecords,
    int AddedRecords,
    string? SafetyBackupPath,
    DateTimeOffset AppliedAt);

/// <summary>
/// Validates a full-data export before it can change the local database.
/// Merge is additive and keeps existing records on ID conflicts; Replace is
/// destructive and requires a verified safety backup before the transaction.
/// API tokens and AI keys are not part of StudyData and are never imported.
/// </summary>
public sealed class StudyDataImportService
{
    public const int MaximumImportBytes = 64 * 1024 * 1024;
    public const int MaximumImportedRecords = 100_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64
    };

    private readonly IStudyDataStore _store;
    private readonly BackupService? _backups;

    public StudyDataImportService(IStudyDataStore store, BackupService? backups = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _backups = backups;
    }

    public async Task<StudyDataImportPreview> PreviewAsync(
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken = default)
    {
        if (payload.IsEmpty || payload.Length > MaximumImportBytes)
            throw new InvalidDataException("Il file di importazione è vuoto o supera il limite di 64 MB.");

        StudyData imported;
        DateTimeOffset? generatedAt;
        try
        {
            using var document = JsonDocument.Parse(payload, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("schemaVersion", out var version) ||
                version.ValueKind != JsonValueKind.String ||
                !string.Equals(version.GetString(), PrivacyService.ExportSchemaVersion, StringComparison.Ordinal) ||
                !root.TryGetProperty("data", out var dataElement) ||
                dataElement.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Il file non è un'esportazione UniTo StudyHub supportata.");

            generatedAt = root.TryGetProperty("generatedAt", out var timestamp) &&
                          timestamp.ValueKind == JsonValueKind.String &&
                          timestamp.TryGetDateTimeOffset(out var parsed)
                ? parsed
                : null;
            imported = dataElement.Deserialize<StudyData>(JsonOptions)
                       ?? throw new InvalidDataException("Il file non contiene dati di studio validi.");
            StudyDataEntityMapper.NormalizeAndValidate(imported);
        }
        catch (InvalidDataException) { throw; }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or
                                   NullReferenceException or OverflowException)
        {
            throw new InvalidDataException("Il file di importazione contiene dati non validi o incoerenti.", ex);
        }

        var counts = CountItems(imported);
        if (counts.Values.Sum() > MaximumImportedRecords)
            throw new InvalidDataException("Il file contiene più di 100.000 record: importazione rifiutata.");

        var existing = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        var duplicates = CountDuplicates(existing, imported);
        var warnings = new List<string>();
        if (duplicates.Values.Sum() > 0)
            warnings.Add("I record con identificativo già presente non sovrascriveranno i dati locali durante l'unione.");
        if (imported.PdfDocuments.Count > 0)
            warnings.Add("I riferimenti ai PDF vengono importati come metadati; i file fisici non vengono copiati.");
        if (!string.IsNullOrWhiteSpace(imported.MoodleBaseUrl))
            warnings.Add("L'indirizzo Moodle è un'impostazione, non un token: verifica l'istanza prima di riconnetterti.");

        return new StudyDataImportPreview(imported, generatedAt,
            Convert.ToHexString(SHA256.HashData(payload.Span)), counts, duplicates, warnings);
    }

    public async Task<StudyDataImportResult> ApplyAsync(
        StudyDataImportPreview preview,
        StudyDataImportStrategy strategy,
        string? backupPassword = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var imported = Clone(preview.ImportedData);
        var existing = await _store.LoadAsync(cancellationToken).ConfigureAwait(false);
        string? safetyBackupPath = null;
        StudyData candidate;

        if (strategy == StudyDataImportStrategy.Replace)
        {
            if (_backups is null)
                throw new InvalidOperationException("Per sostituire i dati è necessario configurare il servizio di backup.");
            var backup = await _backups.CreateBackupAsync(backupPassword, cancellationToken).ConfigureAwait(false);
            safetyBackupPath = backup.FilePath;
            candidate = imported;
        }
        else if (strategy == StudyDataImportStrategy.Merge)
        {
            candidate = Merge(existing, imported);
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(strategy));
        }

        StudyDataEntityMapper.NormalizeAndValidate(candidate);
        var before = CountItems(existing).Values.Sum();
        var after = CountItems(candidate).Values.Sum();
        await _store.SaveAsync(candidate, cancellationToken).ConfigureAwait(false);
        return new StudyDataImportResult(strategy, preview.TotalItems,
            strategy == StudyDataImportStrategy.Merge ? Math.Max(0, after - before) : after,
            safetyBackupPath, DateTimeOffset.UtcNow);
    }

    private static StudyData Clone(StudyData data)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, JsonOptions);
        return JsonSerializer.Deserialize<StudyData>(bytes, JsonOptions)
               ?? throw new InvalidDataException("Impossibile preparare i dati per l'importazione.");
    }

    private static StudyData Merge(StudyData current, StudyData incoming)
    {
        var result = Clone(current);
        MergeById(result.Courses, incoming.Courses, x => x.Id);
        MergeById(result.Grades, incoming.Grades, x => x.Id);
        MergeById(result.Exams, incoming.Exams, x => x.Id);
        MergeById(result.Announcements, incoming.Announcements, x => x.Id);
        MergeById(result.CareerExams, incoming.CareerExams, x => x.CourseId);
        MergeById(result.CoursePrerequisites, incoming.CoursePrerequisites,
            x => x.CourseId + "\u001f" + x.PrerequisiteCourseId);
        MergeById(result.SyllabusTopics, incoming.SyllabusTopics, x => x.Id);
        MergeById(result.Questions, incoming.Questions, x => x.Id);
        MergeById(result.CalendarEvents, incoming.CalendarEvents, x => x.Id);
        MergeById(result.StudySessions, incoming.StudySessions, x => x.Id);
        MergeById(result.StudyTasks, incoming.StudyTasks, x => x.Id);
        MergeById(result.StudyActivities, incoming.StudyActivities, x => x.Id);
        MergeById(result.StudyMissions, incoming.StudyMissions, x => x.Id);
        MergeById(result.PomodoroCycles, incoming.PomodoroCycles, x => x.Id);
        MergeById(result.ActivityRevisions, incoming.ActivityRevisions, x => x.Id);
        MergeById(result.ErrorJournalEntries, incoming.ErrorJournalEntries, x => x.Id);
        MergeById(result.Flashcards, incoming.Flashcards, x => x.Id);
        MergeById(result.FlashcardReviews, incoming.FlashcardReviews, x => x.Id);
        MergeById(result.Distractions, incoming.Distractions, x => x.Id);
        MergeById(result.EnergyCheckIns, incoming.EnergyCheckIns, x => x.Id);
        MergeById(result.PdfDocuments, incoming.PdfDocuments, x => x.Id);

        // Local profile, timer, Moodle endpoint and Pomodoro preferences are
        // settings rather than imported study records; merge never overwrites them.
        return result;
    }

    private static void MergeById<T>(
        ObservableCollection<T> target,
        IEnumerable<T> incoming,
        Func<T, string> key)
    {
        var keys = target.Select(key).ToHashSet(StringComparer.Ordinal);
        foreach (var item in incoming)
        {
            var id = key(item);
            if (!string.IsNullOrWhiteSpace(id) && keys.Add(id))
                target.Add(item);
        }
    }

    private static IReadOnlyDictionary<string, int> CountItems(StudyData data) =>
        new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Corsi"] = data.Courses.Count,
            ["Voti"] = data.Grades.Count,
            ["Esami pianificati"] = data.Exams.Count,
            ["Avvisi"] = data.Announcements.Count,
            ["Carriera esami"] = data.CareerExams.Count,
            ["Propedeuticità"] = data.CoursePrerequisites.Count,
            ["Argomenti syllabus"] = data.SyllabusTopics.Count,
            ["Domande"] = data.Questions.Count,
            ["Eventi calendario"] = data.CalendarEvents.Count,
            ["Sessioni studio"] = data.StudySessions.Count,
            ["Attività"] = data.StudyActivities.Count,
            ["Task"] = data.StudyTasks.Count,
            ["Missioni"] = data.StudyMissions.Count,
            ["Cicli Pomodoro"] = data.PomodoroCycles.Count,
            ["Revisioni"] = data.ActivityRevisions.Count,
            ["Errori"] = data.ErrorJournalEntries.Count,
            ["Flashcard"] = data.Flashcards.Count,
            ["Ripassi flashcard"] = data.FlashcardReviews.Count,
            ["Distrazioni"] = data.Distractions.Count,
            ["Check-in energia"] = data.EnergyCheckIns.Count,
            ["Documenti PDF"] = data.PdfDocuments.Count
        };

    private static IReadOnlyDictionary<string, int> CountDuplicates(StudyData current, StudyData incoming)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        Add("Corsi", current.Courses, incoming.Courses, x => x.Id);
        Add("Voti", current.Grades, incoming.Grades, x => x.Id);
        Add("Esami pianificati", current.Exams, incoming.Exams, x => x.Id);
        Add("Avvisi", current.Announcements, incoming.Announcements, x => x.Id);
        Add("Carriera esami", current.CareerExams, incoming.CareerExams, x => x.CourseId);
        Add("Propedeuticità", current.CoursePrerequisites, incoming.CoursePrerequisites,
            x => x.CourseId + "\u001f" + x.PrerequisiteCourseId);
        Add("Argomenti syllabus", current.SyllabusTopics, incoming.SyllabusTopics, x => x.Id);
        Add("Domande", current.Questions, incoming.Questions, x => x.Id);
        Add("Eventi calendario", current.CalendarEvents, incoming.CalendarEvents, x => x.Id);
        Add("Sessioni studio", current.StudySessions, incoming.StudySessions, x => x.Id);
        Add("Attività", current.StudyActivities, incoming.StudyActivities, x => x.Id);
        Add("Task", current.StudyTasks, incoming.StudyTasks, x => x.Id);
        Add("Missioni", current.StudyMissions, incoming.StudyMissions, x => x.Id);
        Add("Cicli Pomodoro", current.PomodoroCycles, incoming.PomodoroCycles, x => x.Id);
        Add("Revisioni", current.ActivityRevisions, incoming.ActivityRevisions, x => x.Id);
        Add("Errori", current.ErrorJournalEntries, incoming.ErrorJournalEntries, x => x.Id);
        Add("Flashcard", current.Flashcards, incoming.Flashcards, x => x.Id);
        Add("Ripassi flashcard", current.FlashcardReviews, incoming.FlashcardReviews, x => x.Id);
        Add("Distrazioni", current.Distractions, incoming.Distractions, x => x.Id);
        Add("Check-in energia", current.EnergyCheckIns, incoming.EnergyCheckIns, x => x.Id);
        Add("Documenti PDF", current.PdfDocuments, incoming.PdfDocuments, x => x.Id);
        return result;

        void Add<T>(string name, IEnumerable<T> oldItems, IEnumerable<T> newItems, Func<T, string> key)
        {
            var keys = oldItems.Select(key).ToHashSet(StringComparer.Ordinal);
            result[name] = newItems.Count(item => keys.Contains(key(item)));
        }
    }
}
