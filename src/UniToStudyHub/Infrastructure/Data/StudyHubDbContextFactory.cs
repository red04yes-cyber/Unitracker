using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace UniToStudyHub.Infrastructure.Data;

public static class StudyHubDatabase
{
    public static string GetDatabasePath()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UniToStudyHub");
        Directory.CreateDirectory(folder);
        return Path.Combine(folder, "unitracker.db");
    }

    public static DbContextOptions<StudyHubDbContext> CreateOptions(string? databasePath = null) =>
        new DbContextOptionsBuilder<StudyHubDbContext>()
            .UseSqlite($"Data Source={databasePath ?? GetDatabasePath()}")
            .Options;
}

public sealed class StudyHubDbContextFactory : IDesignTimeDbContextFactory<StudyHubDbContext>
{
    public StudyHubDbContext CreateDbContext(string[] args) =>
        new(StudyHubDatabase.CreateOptions());
}
