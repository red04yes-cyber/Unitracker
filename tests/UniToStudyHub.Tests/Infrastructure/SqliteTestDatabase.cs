using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using UniToStudyHub.Infrastructure.Data;

namespace UniToStudyHub.Tests.Infrastructure;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection? _connection;
    private readonly string? _folder;

    public string? DatabasePath { get; }
    public DbContextOptions<StudyHubDbContext> Options { get; }
    public IDbContextFactory<StudyHubDbContext> Factory { get; }

    private SqliteTestDatabase(
        SqliteConnection? connection,
        string? folder,
        string? databasePath,
        DbContextOptions<StudyHubDbContext> options)
    {
        _connection = connection;
        _folder = folder;
        DatabasePath = databasePath;
        Options = options;
        Factory = new TestContextFactory(options);
    }

    public static async Task<SqliteTestDatabase> CreateInMemoryAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite(connection)
            .Options;
        var database = new SqliteTestDatabase(connection, null, null, options);
        await database.MigrateAsync();
        return database;
    }

    public static async Task<SqliteTestDatabase> CreateFileAsync(bool migrate = true)
    {
        var folder = Path.Combine(Path.GetTempPath(), "UnitrackerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "unitracker.db");
        var database = new SqliteTestDatabase(null, folder, path, StudyHubDatabase.CreateOptions(path));
        if (migrate) await database.MigrateAsync();
        return database;
    }

    public StudyHubDbContext CreateContext() => new(Options);

    public async Task MigrateAsync(string? targetMigration = null)
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync(targetMigration);
    }

    public async Task<List<string>> QueryScalarsAsync(string sql)
    {
        if (DatabasePath is not null)
        {
            await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await ReadAsync(command);
        }

        using var memoryCommand = _connection!.CreateCommand();
        memoryCommand.CommandText = sql;
        return await ReadAsync(memoryCommand);
    }

    private static async Task<List<string>> ReadAsync(SqliteCommand command)
    {
        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            values.Add(reader.GetString(0));
        return values;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();

        if (_folder is not null && Directory.Exists(_folder))
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException)
            {
                // A lingering SQLite handle must not turn a passing test into a failure.
            }
        }
    }

    private sealed class TestContextFactory(DbContextOptions<StudyHubDbContext> options)
        : IDbContextFactory<StudyHubDbContext>
    {
        public StudyHubDbContext CreateDbContext() => new(options);

        public Task<StudyHubDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}
