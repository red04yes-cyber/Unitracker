using System.Data;
using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UniToStudyHub.Security;

namespace UniToStudyHub.Infrastructure.Data;

/// <summary>
/// The live SQLite database uses SQLCipher-compatible AES-256 encryption. Its
/// random key is protected with Windows DPAPI for the current user; the database
/// file and SQLite journal/WAL pages are encrypted at rest.
/// </summary>
public sealed record DatabaseProtectionStatus(
    bool EncryptedAtRest,
    bool PerUserAcl,
    string Algorithm,
    string Recommendation);

public static class StudyHubDatabase
{
    // The preliminary branch used 12-digit ids. EF Core treats those as invalid
    // ids (it expects yyyyMMddHHmmss) and slices their names incorrectly, so a
    // database created before the correction keeps working through this rename.
    public const string PreliminaryInitialMigrationId = "202611040001_InitialCreate";
    public const string InitialMigrationId = "20261104000100_InitialCreate";
    public const string PreliminaryRelationshipsMigrationId = "202611040002_AddCourseRelationships";
    public const string RelationshipsMigrationId = "20261104000200_AddCourseRelationships";

    public const string DataDirectoryEnvironmentVariable = "UNITRACKER_DATA_DIR";

    /// <summary>
    /// Returns the profile directory used by the app. Tests and the Windows CI
    /// smoke run can redirect it to an isolated temporary directory; normal
    /// startup keeps using %LOCALAPPDATA%\UniToStudyHub. The folder is created
    /// and restricted to the current user, and abandoned temporary files from
    /// an interrupted save are removed.
    /// </summary>
    public static string GetDataDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(DataDirectoryEnvironmentVariable);
        var folder = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UniToStudyHub")
            : Path.GetFullPath(configured);
        LocalFileSecurity.EnsurePrivateDirectory(folder);
        LocalFileSecurity.CleanupStaleTemporaryFiles(folder, TimeSpan.FromHours(1),
            "*.tmp", "*.part", "*.restore-*.tmp");
        return folder;
    }

    public static string GetDatabasePath()
    {
        var folder = GetDataDirectory();
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "unitracker.db");
        if (File.Exists(path)) LocalFileSecurity.TryRestrictFile(path);
        return path;
    }

    /// <summary>
    /// Returns a SQLCipher v4 connection string. The key is random and stored
    /// only as a DPAPI-protected secret for the current Windows account.
    /// </summary>
    public static string CreateConnectionString(
        string databasePath,
        Microsoft.Data.Sqlite.SqliteOpenMode mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        EnsureEncryptedDatabase(fullPath);
        return CreateEncryptedConnectionString(fullPath, mode);
    }

    /// <summary>Builds a keyed connection string without converting the target file.</summary>
    public static string CreateEncryptedConnectionString(
        string databasePath,
        Microsoft.Data.Sqlite.SqliteOpenMode mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        var key = new DatabaseKeyVault(ResolveKeyDirectory(fullPath)).GetOrCreateKey();
        try
        {
            var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = new Uri(fullPath).AbsoluteUri + "?cipher=sqlcipher&legacy=4",
                Mode = mode,
                Password = key,
                ForeignKeys = true,
                Pooling = mode != Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly
            };
            return builder.ToString();
        }
        finally
        {
            // The key remains in the connection string only as long as required
            // to create a DbContext. It is never logged or persisted in plaintext.
        }
    }

    private static string ResolveKeyDirectory(string databasePath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(databasePath))!);
        var first = current.FullName;
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "secrets", DatabaseKeyVault.SecretName + ".secret");
            if (File.Exists(candidate)) return current.FullName;
            current = current.Parent;
        }
        return first;
    }

    /// <summary>
    /// One-time, crash-safe conversion of the previous plaintext SQLite file.
    /// The old file is kept until the encrypted copy passes integrity_check.
    /// An encrypted or unknown file is never overwritten automatically.
    /// </summary>
    public static void EnsureEncryptedDatabase(string databasePath)
    {
        if (!File.Exists(databasePath) || new FileInfo(databasePath).Length == 0) return;
        using (var probe = new FileStream(databasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            Span<byte> header = stackalloc byte[16];
            var read = probe.Read(header);
            if (read < 16 || !header.SequenceEqual("SQLite format 3\0"u8)) return;
        }

        var directory = Path.GetDirectoryName(databasePath)!;
        var temporary = databasePath + ".encrypt-" + Guid.NewGuid().ToString("N") + ".tmp";
        LocalFileSecurity.EnsurePrivateDirectory(directory);
        var key = new DatabaseKeyVault(directory).GetOrCreateKey();
        try
        {
            var sourceBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var source = new Microsoft.Data.Sqlite.SqliteConnection(sourceBuilder.ToString());
            source.Open();

            var destinationBuilder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            {
                DataSource = new Uri(temporary).AbsoluteUri + "?cipher=sqlcipher&legacy=4",
                Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
                Password = key,
                Pooling = false
            };
            using (var destination = new Microsoft.Data.Sqlite.SqliteConnection(destinationBuilder.ToString()))
            {
                destination.Open();
                source.BackupDatabase(destination);
                using var check = destination.CreateCommand();
                check.CommandText = "PRAGMA integrity_check";
                if (!string.Equals(check.ExecuteScalar()?.ToString(), "ok", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("La conversione cifrata del database non ha superato il controllo di integrità.");
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            LocalFileSecurity.TryScrubAndDelete(databasePath + "-wal");
            LocalFileSecurity.TryDelete(databasePath + "-shm");
            LocalFileSecurity.TryRestrictFile(temporary);
            File.Replace(temporary, databasePath, destinationBackupFileName: null);
            LocalFileSecurity.TryRestrictFile(databasePath);
        }
        catch
        {
            LocalFileSecurity.ScrubAndDelete(temporary);
            throw;
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        }
    }

    public static DbContextOptions<StudyHubDbContext> CreateOptions(string? databasePath = null) =>
        new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(CreateConnectionString(databasePath ?? GetDatabasePath()))
            .Options;

    public static DatabaseProtectionStatus DescribeProtection() =>
        new(
            EncryptedAtRest: OperatingSystem.IsWindows(),
            PerUserAcl: OperatingSystem.IsWindows(),
            Algorithm: OperatingSystem.IsWindows() ? "SQLCipher v4 · AES-256-CBC + HMAC-SHA512 · chiave DPAPI" : "non disponibile",
            Recommendation: OperatingSystem.IsWindows()
                ? "Database cifrato con chiave casuale protetta da DPAPI per l'account Windows corrente. Conserva una copia di sicurezza cifrata: la chiave DPAPI non è trasferibile a un altro account o PC."
                : "La cifratura del database richiede Windows DPAPI.");

    /// <summary>
    /// Renames migration-history rows created by the preliminary 12-digit
    /// migrations. It is a no-op on a new database and never touches user tables.
    /// </summary>
    public static async Task UpgradePreliminaryMigrationHistoryAsync(
        StudyHubDbContext db,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        var connection = db.Database.GetDbConnection();
        var opened = false;
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            using var check = connection.CreateCommand();
            check.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
            var historyExists = Convert.ToInt32(await check.ExecuteScalarAsync(cancellationToken)) > 0;
            if (!historyExists) return;

            await RenameHistoryAsync(connection, PreliminaryInitialMigrationId, InitialMigrationId, cancellationToken);
            await RenameHistoryAsync(connection, PreliminaryRelationshipsMigrationId, RelationshipsMigrationId, cancellationToken);
        }
        finally
        {
            if (opened) await connection.CloseAsync();
        }
    }

    private static async Task RenameHistoryAsync(
        DbConnection connection,
        string oldId,
        string newId,
        CancellationToken cancellationToken)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "UPDATE __EFMigrationsHistory SET MigrationId = @newId " +
            "WHERE MigrationId = @oldId AND NOT EXISTS " +
            "(SELECT 1 FROM __EFMigrationsHistory WHERE MigrationId = @newId)";
        var newIdParameter = command.CreateParameter();
        newIdParameter.ParameterName = "@newId";
        newIdParameter.Value = newId;
        command.Parameters.Add(newIdParameter);
        var oldIdParameter = command.CreateParameter();
        oldIdParameter.ParameterName = "@oldId";
        oldIdParameter.Value = oldId;
        command.Parameters.Add(oldIdParameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class StudyHubDbContextFactory : IDesignTimeDbContextFactory<StudyHubDbContext>
{
    public StudyHubDbContext CreateDbContext(string[] args) =>
        new(StudyHubDatabase.CreateOptions());
}
