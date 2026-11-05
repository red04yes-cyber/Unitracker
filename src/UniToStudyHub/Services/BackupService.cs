using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniToStudyHub.Infrastructure.Data;

namespace UniToStudyHub.Services;

public sealed record BackupInfo(
    string FilePath,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string Sha256,
    bool IntegrityVerified);

/// <summary>
/// Human-readable sidecar that makes the backup format and checksum explicit.
/// It contains no credentials and the SQLite file remains independently
/// verifiable if the sidecar is missing.
/// </summary>
public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAt,
    string Sha256,
    long SizeBytes);

/// <summary>
/// Creates consistent SQLite snapshots with VACUUM INTO, verifies them with
/// PRAGMA integrity_check, and only replaces the live database after the new
/// file has been restored and verified. The Moodle token is stored separately
/// in DPAPI and therefore is not copied into these backups.
/// </summary>
public sealed class BackupService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    string databasePath,
    string? backupDirectory = null)
{
    private readonly IDbContextFactory<StudyHubDbContext> _factory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    private readonly string _databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
    private readonly string _backupDirectory = Path.GetFullPath(backupDirectory ??
        Path.Combine(Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Database path has no folder.", nameof(databasePath)), "backups"));

    public const int CurrentFormatVersion = 1;
    public string BackupDirectory => _backupDirectory;

    public async Task<BackupInfo> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_backupDirectory);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        var target = Path.Combine(_backupDirectory, $"unitracker-{stamp}-{Guid.NewGuid():N}.db");
        if (File.Exists(target)) throw new IOException("A backup with the generated name already exists.");

        await using var db = await _factory.CreateDbContextAsync(cancellationToken);
        var connection = db.Database.GetDbConnection();
        var openedHere = false;
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync(cancellationToken);
                openedHere = true;
            }
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "VACUUM INTO @path";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@path";
                parameter.Value = target;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }
        catch
        {
            if (File.Exists(target)) File.Delete(target);
            throw;
        }
        finally
        {
            if (openedHere) await connection.CloseAsync();
        }

        var verified = await VerifyBackupAsync(target, cancellationToken);
        var info = new BackupInfo(target, DateTimeOffset.UtcNow, new FileInfo(target).Length, ComputeSha256(target), verified);
        await WriteManifestAsync(info, cancellationToken);
        return info;
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_backupDirectory)) return Array.Empty<BackupInfo>();
        return Directory.EnumerateFiles(_backupDirectory, "unitracker-*.db")
            .Select(path => new BackupInfo(
                path,
                new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                new FileInfo(path).Length,
                ComputeSha256(path),
                false))
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
    }

    public async Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath)) return false;
        try
        {
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Restores a previously verified backup. A safety backup of the live
    /// database is created first. The caller must ensure no other app process
    /// has the database open.
    /// </summary>
    public async Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("Backup not found.", backupPath);
        if (!await VerifyBackupAsync(backupPath, cancellationToken))
            throw new InvalidDataException("The selected backup failed SQLite integrity verification.");
        var manifest = await ReadManifestAsync(backupPath, cancellationToken);
        if (manifest is not null)
        {
            if (manifest.FormatVersion > CurrentFormatVersion)
                throw new InvalidDataException("The selected backup was created by a newer backup format.");
            if (!string.Equals(manifest.Sha256, ComputeSha256(backupPath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The selected backup does not match its checksum manifest.");
        }

        var safety = await CreateBackupAsync(cancellationToken);
        var temporary = _databasePath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(backupPath, temporary, overwrite: true);
            if (!await VerifyBackupAsync(temporary, cancellationToken))
                throw new InvalidDataException("The restored copy failed integrity verification.");

            // SQLite pools idle connections by path. They would otherwise keep
            // serving the pre-restore inode after the file is replaced.
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Move(temporary, _databasePath, overwrite: true);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }

        if (!await VerifyBackupAsync(_databasePath, cancellationToken))
            throw new InvalidDataException(
                $"The restored database failed verification. The safety backup is available at {safety.FilePath}.");
    }

    public static string ManifestPath(string backupPath) => backupPath + ".manifest.json";

    public async Task<BackupManifest?> ReadManifestAsync(string backupPath, CancellationToken cancellationToken = default)
    {
        var path = ManifestPath(backupPath);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The backup manifest is not valid JSON.", ex);
        }
    }

    private async Task WriteManifestAsync(BackupInfo info, CancellationToken cancellationToken)
    {
        var manifest = new BackupManifest(CurrentFormatVersion, info.CreatedAt, info.Sha256, info.SizeBytes);
        await using var stream = File.Create(ManifestPath(info.FilePath));
        await JsonSerializer.SerializeAsync(stream, manifest, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
