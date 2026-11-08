using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Security;
using UniToStudyHub.Services;

namespace UniToStudyHub;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ThemeManager.ApplySavedTheme();

            // Keep every startup operation inside this boundary. In particular,
            // path/ACL setup and DI registration can fail before database startup.
            var dataDirectory = StudyHubDatabase.GetDataDirectory();
            AppDiagnostics.Configure(Path.Combine(dataDirectory, "logs"));
            AppDiagnostics.Info("startup", "Application startup started.");

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddUniToStudyHubCore();
                    services.AddUniToStudyHubPresentation();
                    services.AddSingleton<MainWindow>();
                })
                .Build();

            await _host.StartAsync();

            await using var scope = _host.Services.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StudyHubDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await StudyHubDatabase.UpgradePreliminaryMigrationHistoryAsync(db);
                await db.Database.MigrateAsync();
            }

            var migrator = scope.ServiceProvider.GetRequiredService<LegacyJsonMigrator>();
            await migrator.MigrateAsync();

            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();

            var window = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            AppDiagnostics.Info("startup", "Main window shown successfully.");
        }
        catch (Exception ex)
        {
            AppDiagnostics.Error("startup", "Application startup failed.", ex);
            var logPath = AppDiagnostics.CurrentLogPath ??
                          Path.Combine(AppDiagnostics.Directory, "unitracker.log");
            MessageBox.Show(
                "UniTo StudyHub non è riuscito ad avviarsi. I dati originali non sono stati eliminati.\n\n" +
                $"Errore: {ex.GetType().Name}: {ex.Message}\n\n" +
                $"Dettagli diagnostici (se il file è stato creato): {logPath}",
                "Errore di avvio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _host?.StopAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _host?.Dispose();
            base.OnExit(e);
        }
    }
}
