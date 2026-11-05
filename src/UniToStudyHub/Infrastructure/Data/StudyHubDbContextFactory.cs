using System.Data;
using System.Data.Common;
using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using UniToStudyHub.Security;

namespace UniToStudyHub.Infrastructure.Data;

/// <summary>
/// Truthful description of how the database is protected. The application does
/// not encrypt SQLite: claiming otherwise would be a security lie. Encryption at
/// rest relies on BitLocker/EFS plus the per-user ACL and DPAPI vault; encrypted
/// backups are handled separately by <c>BackupService</c>.
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
    /// Connection string without credentials. SQLite has no password, so nothing
    /// secret belongs here; foreign keys are enabled explicitly and pooling is
    /// kept enabled only for the live database file.
    /// </summary>
    public static string CreateConnectionString(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = Microsoft.Data.Sqlite.SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = true
        };
        return builder.ToString();
    }

    public static DbContextOptions<StudyHubDbContext> CreateOptions(string? databasePath = null) =>
        new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(CreateConnectionString(databasePath ?? GetDatabasePath()))
            .Options;

    public static DatabaseProtectionStatus DescribeProtection() =>
        new(
            EncryptedAtRest: false,
            PerUserAcl: OperatingSystem.IsWindows(),
            Algorithm: "none",
            Recommendation:
            "Il database SQLite non è cifrato dall'applicazione. Abilita BitLocker o EFS per il profilo " +
            "utente e usa le copie di sicurezza cifrate per l'archivio. Non dichiarare il database cifrato.");

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
