using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

/// <summary>
/// Imports the legacy <c>%LOCALAPPDATA%\UniToStudyHub\data.json</c> exactly
/// once. The original file is never deleted or rewritten, a backup is created
/// before parsing, and the import is committed atomically with its marker.
/// </summary>
public sealed class LegacyJsonMigrator
{
    public const string MigrationKey = "legacy-studydata-json-v1";
    public const long MaximumFileBytes = 64 * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IDbContextFactory<StudyHubDbContext> _factory;
    private readonly string _legacyPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LegacyJsonMigrator(IDbContextFactory<StudyHubDbContext> factory, string? legacyPath = null)
    {
        _factory = factory;
        _legacyPath = legacyPath ?? Path.Combine(StudyHubDatabase.GetDataDirectory(), "data.json");
    }

    public async Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_legacyPath)) return false;

            await using var db = await _factory.CreateDbContextAsync(cancellationToken);
            if (await db.LegacyImports.AnyAsync(x => x.Key == MigrationKey, cancellationToken))
                return false;

            var hasExistingData = await db.ProfileSettings.AnyAsync(cancellationToken) ||
                await db.Courses.AnyAsync(cancellationToken) ||
                await db.Grades.AnyAsync(cancellationToken) ||
                await db.PlannedExams.AnyAsync(cancellationToken) ||
                await db.Announcements.AnyAsync(cancellationToken) ||
                await db.CareerExams.AnyAsync(cancellationToken);
            if (hasExistingData)
                throw new InvalidOperationException(
                    "SQLite already contains user data but has no legacy-import marker. No data was overwritten and data.json was preserved.");

            var folder = Path.GetDirectoryName(_legacyPath);
            if (string.IsNullOrWhiteSpace(folder))
                throw new InvalidDataException(
                    $"The legacy path '{SecurityRedaction.ForLog(_legacyPath)}' has no containing folder.");

            var legacyInfo = new FileInfo(_legacyPath);
            if (legacyInfo.Length > MaximumFileBytes)
                throw new InvalidDataException(
                    $"The legacy data file exceeds the {MaximumFileBytes / 1024 / 1024} MB import limit. " +
                    "It was preserved; nothing was imported.");

            byte[] bytes;
            try
            {
                Directory.CreateDirectory(folder);
                await using var source = new FileStream(_legacyPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                bytes = await BoundedFileReader.ReadAllBytesAsync(source, MaximumFileBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException(
                    "The legacy data file exceeded its read limit or changed while being read. " +
                    "It was preserved; nothing was imported.", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidDataException(
                    "The legacy data file could not be read. It was preserved: " +
                    SecurityRedaction.ForLog(_legacyPath), ex);
            }

            var backupPath = Path.Combine(
                folder,
                $"data.pre-sqlite-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            try
            {
                await File.WriteAllBytesAsync(backupPath, bytes, cancellationToken);
            }
            catch (IOException ex)
            {
                throw new InvalidDataException(
                    "A backup of the legacy data file could not be created; nothing was imported. Source: " +
                    SecurityRedaction.ForLog(_legacyPath), ex);
            }

            // The recovery copy is written before the content is judged, so even
            // an empty or invalid legacy file leaves the user something to keep.
            if (bytes.Length == 0)
                throw new InvalidDataException(
                    "The legacy data file is empty. It was preserved together with its backup; nothing was imported.");

            var sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
            StudyData? legacy;
            try
            {
                await using var stream = new MemoryStream(bytes, writable: false);
                legacy = await JsonSerializer.DeserializeAsync<StudyData>(stream, JsonOptions, cancellationToken);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                throw new InvalidDataException(
                    "Legacy data file is invalid. The original file is preserved; backup: " +
                    SecurityRedaction.ForLog(Path.GetFileName(backupPath)), ex);
            }

            if (legacy is null)
                throw new InvalidDataException(
                    "Legacy data file does not contain a StudyData object. The original file is preserved; backup: " +
                    SecurityRedaction.ForLog(Path.GetFileName(backupPath)) + ".");

            try
            {
                StudyDataEntityMapper.NormalizeAndValidate(legacy);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
            {
                throw new InvalidDataException(
                    "Legacy data validation failed. The original file is preserved; backup: " +
                    SecurityRedaction.ForLog(Path.GetFileName(backupPath)), ex);
            }

            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                StudyDataEntityMapper.AddToContext(db, legacy);
                db.LegacyImports.Add(new LegacyImportEntity
                {
                    Key = MigrationKey,
                    SourceHash = sourceHash,
                    ImportedAt = DateTimeOffset.UtcNow
                });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (DbUpdateException ex)
            {
                throw new InvalidDataException(
                    "Legacy data could not be imported; the transaction was rolled back. The original file is " +
                    "preserved; backup: " + SecurityRedaction.ForLog(Path.GetFileName(backupPath)), ex);
            }

            // Keep data.json unchanged: it remains a recovery copy until the user removes it.
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }
}
