using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Services;

namespace UniToStudyHub.Infrastructure;

/// <summary>
/// Core service registrations shared by the WPF startup and the tests.
/// Presentation types are registered separately so the core can be resolved
/// without touching WPF.
/// </summary>
public static class CompositionRoot
{
    public static IServiceCollection AddUniToStudyHubCore(this IServiceCollection services, string? databasePath = null)
    {
        services.AddDbContextFactory<StudyHubDbContext>(options =>
            options.UseSqlite($"Data Source={databasePath ?? StudyHubDatabase.GetDatabasePath()}"));
        services.AddSingleton(new CareerCalculationOptions());
        services.AddSingleton<CareerCalculatorService>();
        services.AddSingleton(sp => new LegacyJsonMigrator(
            sp.GetRequiredService<IDbContextFactory<StudyHubDbContext>>()));
        services.AddSingleton<IStudyDataStore, SqliteStudyDataStore>();
        services.AddSingleton<MoodleApplicationService>();
        return services;
    }
}
