using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Services;

namespace UniToStudyHub;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        services.AddUniToStudyHubCore();
        services.AddUniToStudyHubViewModels();
        services.AddSingleton<MainWindow>();

        _serviceProvider = services.BuildServiceProvider(validateScopes: true);
        try
        {
            await using var scope = _serviceProvider.CreateAsyncScope();

            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<StudyHubDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await StudyHubDatabase.UpgradePreliminaryMigrationHistoryAsync(db);
                await db.Database.MigrateAsync();
            }

            var migrator = scope.ServiceProvider.GetRequiredService<LegacyJsonMigrator>();
            await migrator.MigrateAsync();

            var viewModel = _serviceProvider.GetRequiredService<MainViewModel>();
            await viewModel.InitializeAsync();

            var window = _serviceProvider.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"UniTo StudyHub non è riuscito a inizializzare il profilo locale. I dati originali non sono stati eliminati.\n\nDettagli: {ex.Message}",
                "Errore di avvio",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
