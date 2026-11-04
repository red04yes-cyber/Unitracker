using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Infrastructure.Data.Entities;
using UniToStudyHub.Models;

namespace UniToStudyHub.Services;

public sealed class LegacyJsonMigrator
{
    public const string MigrationKey = "legacy-studydata-json-v1";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly IDbContextFactory<StudyHubDbContext> _factory;
    private readonly string _legacyPath;

    public LegacyJsonMigrator(IDbContextFactory<StudyHubDbContext> factory, string? legacyPath = null)
    {
        _factory = factory;
        _legacyPath = legacyPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UniToStudyHub", "data.json");
    }

    public async Task<bool> MigrateAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_legacyPath)) return false;
        var bytes = await File.ReadAllBytesAsync(_legacyPath, cancellationToken);
        var folder = Path.GetDirectoryName(_legacyPath)!;
        Directory.CreateDirectory(folder);
        var backupPath = Path.Combine(folder, $"data.pre-sqlite-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
        await File.WriteAllBytesAsync(backupPath, bytes, cancellationToken);
        var sourceHash = Convert.ToHexString(SHA256.HashData(bytes));

        StudyData? legacy;
        try
        {
            await using var stream = new MemoryStream(bytes, writable: false);
            legacy = await JsonSerializer.DeserializeAsync<StudyData>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Legacy data file is invalid. Original file preserved; backup: {backupPath}", ex);
        }
        if (legacy is null) throw new InvalidDataException($"Legacy data file contains no StudyData object. Original preserved; backup: {backupPath}.");
        StudyDataEntityMapper.NormalizeAndValidate(legacy);

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        if (await db.LegacyImports.AnyAsync(x => x.Key == MigrationKey, cancellationToken)) return false;
        var hasExistingData = await db.Courses.AnyAsync(cancellationToken) ||
            await db.Grades.AnyAsync(cancellationToken) ||
            await db.PlannedExams.AnyAsync(cancellationToken) ||
            await db.Announcements.AnyAsync(cancellationToken) ||
            await db.CareerExams.AnyAsync(cancellationToken);
        if (hasExistingData)
            throw new InvalidOperationException("The SQLite database already contains data but has no legacy-import marker. No records were overwritten.");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        StudyDataEntityMapper.AddToContext(db, legacy);
        db.LegacyImports.Add(new LegacyImportEntity
        {
            Key = MigrationKey,
            SourceHash = sourceHash,
            ImportedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // The original data.json is deliberately retained unchanged for recovery.
        return true;
    }
}
