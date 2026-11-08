using UniToStudyHub.Infrastructure.Data;
using Xunit;

namespace UniToStudyHub.Tests.Infrastructure;

public sealed class StudyHubDatabaseTests
{
    [Fact]
    public async Task FileDatabaseIsEncryptedAndCanBeReopenedWithTheCurrentUserKey()
    {
        if (!OperatingSystem.IsWindows()) return;
        await using var database = await SqliteTestDatabase.CreateFileAsync();
        var path = Assert.IsType<string>(database.DatabasePath);
        await using (var db = database.CreateContext())
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TABLE EncryptionProbe (Value TEXT NOT NULL)");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO EncryptionProbe (Value) VALUES ('encrypted')");
        }

        var header = new byte[16];
        await using (var file = File.OpenRead(path))
            Assert.Equal(16, await file.ReadAsync(header));
        Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\\0"u8));

        await using var reopened = new Microsoft.Data.Sqlite.SqliteConnection(
            StudyHubDatabase.CreateEncryptedConnectionString(path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly));
        await reopened.OpenAsync();
        await using var command = reopened.CreateCommand();
        command.CommandText = "SELECT Value FROM EncryptionProbe";
        Assert.Equal("encrypted", await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task ExistingPlaintextDatabaseIsMigratedWithoutLosingRows()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "unitracker.db");
        try
        {
            await using (var plain = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                await plain.OpenAsync();
                await using var create = plain.CreateCommand();
                create.CommandText = "CREATE TABLE LegacyProbe (Value TEXT NOT NULL); INSERT INTO LegacyProbe VALUES ('keep-me')";
                await create.ExecuteNonQueryAsync();
            }

            var options = StudyHubDatabase.CreateOptions(path);
            await using var encrypted = new Microsoft.Data.Sqlite.SqliteConnection(
                StudyHubDatabase.CreateEncryptedConnectionString(path, Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly));
            await encrypted.OpenAsync();
            await using var read = encrypted.CreateCommand();
            read.CommandText = "SELECT Value FROM LegacyProbe";
            Assert.Equal("keep-me", await read.ExecuteScalarAsync());

            var header = new byte[16];
            await using var file = File.OpenRead(path);
            Assert.Equal(16, await file.ReadAsync(header));
            Assert.False(header.AsSpan().SequenceEqual("SQLite format 3\\0"u8));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void DataDirectoryOverrideIsUsedForIsolatedStartup()
    {
        var previous = Environment.GetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable);
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable, folder);

            var expected = Path.GetFullPath(folder);
            Assert.Equal(expected, StudyHubDatabase.GetDataDirectory());
            Assert.Equal(Path.Combine(expected, "unitracker.db"), StudyHubDatabase.GetDatabasePath());
            Assert.True(Directory.Exists(expected));
        }
        finally
        {
            Environment.SetEnvironmentVariable(StudyHubDatabase.DataDirectoryEnvironmentVariable, previous);
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
