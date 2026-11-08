using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Security;

namespace UniToStudyHub.Services;

public sealed record BackupInfo(
    string FilePath,
    DateTimeOffset CreatedAt,
    long SizeBytes,
    string Sha256,
    bool IntegrityVerified,
    bool IsEncrypted = false,
    string KeyProtection = "none",
    int FormatVersion = BackupService.CurrentFormatVersion,
    string? ContainerSha256 = null);

/// <summary>
/// Sidecar description of a backup. It contains no credential and no personal
/// content: only the format, timestamps and hashes needed to detect corruption
/// or tampering. A missing sidecar never prevents the container from being
/// authenticated, because the GCM tag covers its own header.
/// </summary>
public sealed record BackupManifest(
    int FormatVersion,
    DateTimeOffset CreatedAt,
    string Sha256,
    long SizeBytes,
    string Encryption = "none",
    string KeyProtection = "none",
    string? CiphertextSha256 = null,
    long? PayloadLength = null);

/// <summary>
/// Creates consistent SQLite snapshots with VACUUM INTO, encrypts them with
/// AES-256-GCM and only replaces the live database after the new file has been
/// decrypted, authenticated and verified with PRAGMA integrity_check.
///
/// The key is random per backup and either wrapped by Windows DPAPI for the
/// current account or derived from a user passphrase (PBKDF2-HMAC-SHA256).
/// Nothing is hard-coded, and neither the Moodle token (DPAPI vault) nor the AI
/// key ever enters the snapshot. An interrupted write leaves a <c>.part</c>
/// staging file that is never listed or restorable and is removed on the next
/// run. Legacy unencrypted v1 backups remain verifiable and restorable, but the
/// service always creates encrypted v2 backups.
/// </summary>
public sealed class BackupService
{
    public const int CurrentFormatVersion = 2;
    public const int LegacyFormatVersion = 1;
    public const string ContainerExtension = ".utbk";
    public const long MaximumPayloadBytes = 512L * 1024 * 1024;

    private readonly IDbContextFactory<StudyHubDbContext> _factory;
    private readonly string _databasePath;
    private readonly string _backupDirectory;
    private readonly string _stagingDirectory;
    private readonly string _verificationDirectory;
    private readonly IBackupKeyProtector? _keyProtector;

    public BackupService(
        IDbContextFactory<StudyHubDbContext> contextFactory,
        string databasePath,
        string? backupDirectory = null,
        IBackupKeyProtector? keyProtector = null)
    {
        _factory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
        _databasePath = Path.GetFullPath(databasePath ?? throw new ArgumentNullException(nameof(databasePath)));
        _backupDirectory = Path.GetFullPath(backupDirectory ??
            Path.Combine(Path.GetDirectoryName(databasePath)
                         ?? throw new ArgumentException("Database path has no folder.", nameof(databasePath)),
                "backups"));
        _stagingDirectory = Path.Combine(_backupDirectory, ".staging");
        _verificationDirectory = Path.Combine(_backupDirectory, ".verify");
        _keyProtector = keyProtector;
    }

    public string BackupDirectory => _backupDirectory;

    public async Task<BackupInfo> CreateBackupAsync(CancellationToken cancellationToken = default) =>
        await CreateBackupAsync(password: null, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Creates an encrypted backup. An empty password selects DPAPI protection
    /// for the current Windows user; a passphrase makes the file portable.
    /// </summary>
    public async Task<BackupInfo> CreateBackupAsync(string? password, CancellationToken cancellationToken = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        var stamp = createdAt.ToString("yyyyMMdd-HHmmss-fff");
        var target = Path.Combine(_backupDirectory, $"unitracker-{stamp}-{Guid.NewGuid():N}{ContainerExtension}");
        if (File.Exists(target)) throw new IOException("A backup with the generated name already exists.");

        LocalFileSecurity.EnsurePrivateDirectory(_backupDirectory);
        LocalFileSecurity.CleanupStaleTemporaryFiles(_backupDirectory, TimeSpan.FromHours(1),
            "*.part", "*.tmp");
        LocalFileSecurity.EnsurePrivateDirectory(_stagingDirectory);
        LocalFileSecurity.CleanupStaleTemporaryFiles(_stagingDirectory, TimeSpan.FromHours(1), "*.db", "*.tmp");

        var staging = Path.Combine(_stagingDirectory, $"snapshot-{Guid.NewGuid():N}.db");
        byte[] payload = Array.Empty<byte>();
        try
        {
            await SnapshotDatabaseAsync(staging, cancellationToken).ConfigureAwait(false);
            if (!await VerifySqliteFileAsync(staging, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The database snapshot failed SQLite integrity verification.");
            var stagingInfo = new FileInfo(staging);
            if (stagingInfo.Length <= 0 || stagingInfo.Length > MaximumPayloadBytes)
                throw new InvalidDataException(
                    "The database is too large for an encrypted backup (limit 512 MB).");

            payload = await File.ReadAllBytesAsync(staging, cancellationToken).ConfigureAwait(false);
            var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
            var container = BackupCrypto.Encrypt(payload, password, _keyProtector);
            var containerHash = Convert.ToHexString(SHA256.HashData(container));
            var keyProtection = string.IsNullOrEmpty(password)
                ? _keyProtector?.Name ?? "unavailable"
                : "pbkdf2-sha256";

            await LocalFileSecurity.WriteAllBytesAtomicallyAsync(target, container, cancellationToken)
                .ConfigureAwait(false);

            var manifest = new BackupManifest(CurrentFormatVersion, createdAt, payloadHash, payload.LongLength,
                "aes-256-gcm", keyProtection, containerHash, payload.LongLength);
            await WriteManifestAsync(target, manifest, cancellationToken).ConfigureAwait(false);

            return new BackupInfo(target, createdAt, container.LongLength, payloadHash, true, true,
                keyProtection, CurrentFormatVersion, containerHash);
        }
        catch
        {
            LocalFileSecurity.TryDelete(target);
            LocalFileSecurity.TryDelete(ManifestPath(target));
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
            LocalFileSecurity.ScrubAndDelete(staging);
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(_backupDirectory)) return Array.Empty<BackupInfo>();
        var results = new List<BackupInfo>();
        foreach (var path in Directory.EnumerateFiles(_backupDirectory, "unitracker-*", SearchOption.TopDirectoryOnly))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(ContainerExtension, StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".db", StringComparison.OrdinalIgnoreCase)) continue;
            var manifest = TryReadManifest(path);
            var containerHash = ComputeSha256(path);
            results.Add(new BackupInfo(
                path,
                manifest?.CreatedAt ?? new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero),
                new FileInfo(path).Length,
                manifest?.Sha256 ?? containerHash,
                false,
                extension.Equals(ContainerExtension, StringComparison.OrdinalIgnoreCase),
                manifest?.KeyProtection ?? "none",
                manifest?.FormatVersion ?? LegacyFormatVersion,
                containerHash));
        }
        return results.OrderByDescending(x => x.CreatedAt).ToList();
    }

    public Task<bool> VerifyBackupAsync(string backupPath, CancellationToken cancellationToken = default) =>
        VerifyBackupAsync(backupPath, password: null, cancellationToken);

    /// <summary>
    /// Full verification: container authentication, manifest hash, SQLite
    /// integrity. Returns false instead of throwing so a corrupted file can be
    /// reported without exposing internals.
    /// </summary>
    public async Task<bool> VerifyBackupAsync(
        string backupPath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath)) return false;
        var extension = Path.GetExtension(backupPath);
        if (extension.Equals(".db", StringComparison.OrdinalIgnoreCase))
        {
            if (!await VerifySqliteFileAsync(backupPath, cancellationToken).ConfigureAwait(false)) return false;
            var legacyManifest = await ReadManifestAsync(backupPath, cancellationToken).ConfigureAwait(false);
            if (legacyManifest is null || string.IsNullOrWhiteSpace(legacyManifest.Sha256)) return true;
            return string.Equals(legacyManifest.Sha256, ComputeSha256(backupPath),
                StringComparison.OrdinalIgnoreCase);
        }
        if (!extension.Equals(ContainerExtension, StringComparison.OrdinalIgnoreCase)) return false;

        string? plaintextPath = null;
        byte[] plaintext = Array.Empty<byte>();
        try
        {
            var manifest = await ReadManifestAsync(backupPath, cancellationToken).ConfigureAwait(false);
            if (manifest is not null)
            {
                if (manifest.FormatVersion > CurrentFormatVersion) return false;
                if (!string.IsNullOrWhiteSpace(manifest.CiphertextSha256) &&
                    !string.Equals(manifest.CiphertextSha256, ComputeSha256(backupPath),
                        StringComparison.OrdinalIgnoreCase)) return false;
            }

            LocalFileSecurity.EnsurePrivateDirectory(_verificationDirectory);
            plaintext = await DecryptContainerAsync(backupPath, password, cancellationToken).ConfigureAwait(false);
            if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Sha256) &&
                !string.Equals(manifest.Sha256, Convert.ToHexString(SHA256.HashData(plaintext)),
                    StringComparison.OrdinalIgnoreCase)) return false;

            plaintextPath = Path.Combine(_verificationDirectory, $"verify-{Guid.NewGuid():N}.db");
            await File.WriteAllBytesAsync(plaintextPath, plaintext, cancellationToken).ConfigureAwait(false);
            LocalFileSecurity.TryRestrictFile(plaintextPath);
            var valid = await VerifySqliteFileAsync(plaintextPath, cancellationToken).ConfigureAwait(false);
            if (manifest?.PayloadLength is { } expectedLength && expectedLength != plaintext.LongLength)
                valid = false;
            return valid;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException
                                       or CryptographicException or ArgumentException)
        {
            AppDiagnostics.Warning("backup", "Verifica della copia non riuscita.", ex);
            return false;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            LocalFileSecurity.ScrubAndDelete(plaintextPath);
        }
    }

    public Task RestoreAsync(string backupPath, CancellationToken cancellationToken = default) =>
        RestoreAsync(backupPath, password: null, cancellationToken);

    /// <summary>
    /// Restores a verified backup. The backup is decrypted and verified before
    /// the live database is touched, a safety backup is created first, and the
    /// replacement happens through a same-directory temporary file. A corrupt
    /// backup can therefore never replace a valid database.
    /// </summary>
    public async Task RestoreAsync(
        string backupPath,
        string? password,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(backupPath))
            throw new FileNotFoundException("The selected backup does not exist.", backupPath);

        var manifest = await ReadManifestAsync(backupPath, cancellationToken).ConfigureAwait(false);
        if (manifest is not null && manifest.FormatVersion > CurrentFormatVersion)
            throw new InvalidDataException("The selected backup was created by a newer backup format.");

        var isContainer = Path.GetExtension(backupPath).Equals(ContainerExtension, StringComparison.OrdinalIgnoreCase);
        if (isContainer && manifest is not null && !string.IsNullOrWhiteSpace(manifest.CiphertextSha256) &&
            !string.Equals(manifest.CiphertextSha256, ComputeSha256(backupPath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The selected backup does not match its checksum manifest.");

        var temporary = _databasePath + ".restore-" + Guid.NewGuid().ToString("N") + ".tmp";
        byte[] plaintext = Array.Empty<byte>();
        try
        {
            if (isContainer)
            {
                plaintext = await DecryptContainerAsync(backupPath, password, cancellationToken).ConfigureAwait(false);
                if (manifest is not null && !string.IsNullOrWhiteSpace(manifest.Sha256) &&
                    !string.Equals(manifest.Sha256, Convert.ToHexString(SHA256.HashData(plaintext)),
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The selected backup does not match its checksum manifest.");
                await File.WriteAllBytesAsync(temporary, plaintext, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                File.Copy(backupPath, temporary, overwrite: true);
            }
            // Older v1 backups contain a plaintext SQLite file. Convert the
            // temporary restore candidate before it can replace the live DB.
            StudyHubDatabase.EnsureEncryptedDatabase(temporary);
            LocalFileSecurity.TryRestrictFile(temporary);

            if (!await VerifySqliteFileAsync(temporary, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("The selected backup failed SQLite integrity verification.");

            var safety = await CreateBackupAsync(password, cancellationToken).ConfigureAwait(false);

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            LocalFileSecurity.ScrubAndDelete(_databasePath + "-wal");
            LocalFileSecurity.TryDelete(_databasePath + "-shm");
            if (File.Exists(_databasePath))
            {
                try
                {
                    File.Replace(temporary, _databasePath, destinationBackupFileName: null);
                }
                catch (Exception ex) when (ex is PlatformNotSupportedException or IOException
                                               or UnauthorizedAccessException)
                {
                    File.Move(temporary, _databasePath, overwrite: true);
                }
            }
            else
            {
                File.Move(temporary, _databasePath);
            }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            LocalFileSecurity.TryRestrictFile(_databasePath);

            if (!await VerifySqliteFileAsync(_databasePath, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException(
                    "The restored database failed verification. The safety backup is available at " +
                    Path.GetFileName(safety.FilePath) + ".");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
            LocalFileSecurity.ScrubAndDelete(temporary);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    /// <summary>
    /// Applies the local retention policy: keeps the newest
    /// <paramref name="maximumBackups"/> copies and removes anything older than
    /// <paramref name="maximumAge"/>, always keeping at least one backup.
    /// </summary>
    public Task<int> PruneAsync(
        int maximumBackups,
        TimeSpan maximumAge,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumBackups < 1) throw new ArgumentOutOfRangeException(nameof(maximumBackups));
        var backups = ListBackups();
        var threshold = DateTimeOffset.UtcNow - maximumAge;
        var deleted = 0;
        for (var index = 0; index < backups.Count; index++)
        {
            var backup = backups[index];
            if (index < maximumBackups && backup.CreatedAt >= threshold) continue;
            if (index == 0) continue; // never delete the newest copy
            LocalFileSecurity.ScrubAndDelete(backup.FilePath);
            LocalFileSecurity.TryDelete(ManifestPath(backup.FilePath));
            if (!File.Exists(backup.FilePath)) deleted++;
        }
        return Task.FromResult(deleted);
    }

    public static string ManifestPath(string backupPath) => backupPath + ".manifest.json";

    public async Task<BackupManifest?> ReadManifestAsync(
        string backupPath,
        CancellationToken cancellationToken = default)
    {
        var path = ManifestPath(backupPath);
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            // ConfigureAwait(false) keeps the synchronous ListBackups() path safe
            // when it is called from the WPF UI thread.
            return manifest is null || manifest.FormatVersion <= 0 ? null : manifest;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or IOException)
        {
            throw new InvalidDataException("The backup manifest is not valid JSON.", ex);
        }
    }

    private BackupManifest? TryReadManifest(string backupPath)
    {
        try
        {
            return ReadManifestAsync(backupPath).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private async Task<byte[]> DecryptContainerAsync(
        string backupPath,
        string? password,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(backupPath, cancellationToken).ConfigureAwait(false);
        try
        {
            if (!BackupCrypto.LooksLikeContainer(bytes))
                throw new InvalidDataException("The selected file is not an encrypted UniTracker backup.");
            return BackupCrypto.Decrypt(bytes, password, _keyProtector);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private async Task SnapshotDatabaseAsync(string target, CancellationToken cancellationToken)
    {
        await using var db = await _factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var source = db.Database.GetDbConnection();
        var openedHere = false;
        try
        {
            if (source.State != ConnectionState.Open)
            {
                await source.OpenAsync(cancellationToken).ConfigureAwait(false);
                openedHere = true;
            }

            if (source is not Microsoft.Data.Sqlite.SqliteConnection sqliteSource)
                throw new InvalidOperationException("The configured SQLite provider does not support encrypted snapshots.");

            // VACUUM INTO creates a consistent one-file snapshot. SQLCipher
            // may preserve encryption in the snapshot; VerifySqliteFileAsync
            // validates either the encrypted or plaintext SQLite header. The
            // outer AES-256-GCM backup container encrypts the complete payload.
            await using var snapshot = sqliteSource.CreateCommand();
            snapshot.CommandText = "VACUUM INTO $target";
            snapshot.Parameters.Add(new Microsoft.Data.Sqlite.SqliteParameter("$target", target));
            await snapshot.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (!await VerifySqliteFileAsync(target, cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("Lo snapshot SQLite non ha superato il controllo di integrità.");

            LocalFileSecurity.TryRestrictFile(target);
        }
        catch
        {
            LocalFileSecurity.ScrubAndDelete(target);
            throw;
        }
        finally
        {
            if (openedHere) await source.CloseAsync().ConfigureAwait(false);
        }
    }

    private static bool IsPlaintextSqliteFile(string path)
    {
        if (!File.Exists(path)) return false;
        Span<byte> header = stackalloc byte[16];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return stream.Read(header) == 16 && header.SequenceEqual("SQLite format 3\\0"u8);
    }

    private static async Task<bool> VerifySqliteFileAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var isPlaintext = IsPlaintextSqliteFile(path);
            var connectionString = isPlaintext
                ? new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = path,
                    Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                    Pooling = false
                }.ToString()
                : StudyHubDatabase.CreateEncryptedConnectionString(
                    path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly);
            await using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA integrity_check";
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(result?.ToString(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or IOException
                                       or InvalidOperationException)
        {
            return false;
        }
    }

    private static Task WriteManifestAsync(
        string backupPath,
        BackupManifest manifest,
        CancellationToken cancellationToken) =>
        LocalFileSecurity.WriteAllTextAtomicallyAsync(
            ManifestPath(backupPath),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
