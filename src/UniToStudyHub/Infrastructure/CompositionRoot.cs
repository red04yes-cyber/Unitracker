using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Domain.Services;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Security;
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
        var resolvedDatabasePath = databasePath ?? StudyHubDatabase.GetDatabasePath();
        var dataDirectory = Path.GetDirectoryName(resolvedDatabasePath) ?? ".";
        LocalFileSecurity.EnsurePrivateDirectory(dataDirectory);
        AppDiagnostics.Configure(Path.Combine(dataDirectory, "logs"));

        services.AddDbContextFactory<StudyHubDbContext>(options =>
            options.UseSqlite(StudyHubDatabase.CreateConnectionString(resolvedDatabasePath)));
        services.AddSingleton(new CareerCalculationOptions());
        services.AddSingleton<CareerCalculatorService>();
        services.AddSingleton(sp => new LegacyJsonMigrator(
            sp.GetRequiredService<IDbContextFactory<StudyHubDbContext>>()));
        services.AddSingleton<IStudyDataStore, SqliteStudyDataStore>();
        services.AddSingleton<IMoodleTokenStore>(_ => new MoodleTokenVault(dataDirectory));
        services.AddSingleton<INotionTokenStore>(_ => new NotionTokenVault(dataDirectory));
        services.AddSingleton(_ => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });
        services.AddSingleton<NotionApiClient>();
        services.AddSingleton<NotionApplicationService>();
        services.AddSingleton<MoodleApplicationService>();
        services.AddSingleton<AcademicProfileService>();
        services.AddSingleton<CareerApplicationService>();
        services.AddSingleton<SyllabusTrackerService>();
        services.AddSingleton<QuestionBankService>();
        services.AddSingleton<ExamReadinessService>();
        services.AddSingleton<TimeBudgetService>();
        services.AddSingleton<CalendarApplicationService>();
        services.AddSingleton<CalendarEventStore>();
        services.AddSingleton<IcsImportService>();
        services.AddSingleton<FocusSessionService>();
        services.AddSingleton<StudyTaskService>();
        services.AddSingleton<ActivityLedgerService>();
        services.AddSingleton<PomodoroService>();
        services.AddSingleton<StudyDataExportService>();
        services.AddSingleton<StudyPlannerService>();
        services.AddSingleton<StudyMissionService>();
        services.AddSingleton<ErrorJournalService>();
        services.AddSingleton<FlashcardService>();
        services.AddSingleton<DistractionService>();
        services.AddSingleton<EnergyService>();
        services.AddSingleton<ProgressService>();
        services.AddSingleton<ErrorPatternService>();
        services.AddSingleton<WeeklyReviewService>();
        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<PdfImportService>();
        services.AddSingleton<PdfIntelligenceService>();
        services.AddSingleton<AiSettingsService>();
        services.AddSingleton<IAiProvider, UnavailableAiProvider>();
        services.AddSingleton<AiStudyService>();
        services.AddSingleton<StudyDirectorService>();
        services.AddSingleton(sp => new BackupService(
            sp.GetRequiredService<IDbContextFactory<StudyHubDbContext>>(),
            resolvedDatabasePath,
            Path.Combine(dataDirectory, "backups"),
            new DpapiBackupKeyProtector()));
        services.AddSingleton(_ => new AiCredentialStore(dataDirectory));
        services.AddSingleton(sp => new PrivacyService(
            sp.GetRequiredService<IStudyDataStore>(),
            sp.GetRequiredService<IMoodleTokenStore>(),
            sp.GetRequiredService<AiCredentialStore>(),
            sp.GetRequiredService<BackupService>(),
            dataDirectory,
            notionTokens: sp.GetRequiredService<INotionTokenStore>()));
        return services;
    }
}
