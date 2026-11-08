using System.IO;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using UniToStudyHub.Infrastructure;
using UniToStudyHub.Infrastructure.Data;
using UniToStudyHub.Presentation.Composition;
using UniToStudyHub.Presentation.ViewModels;
using UniToStudyHub.Security;
using UniToStudyHub.Services;
using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace UniToStudyHub;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;


    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = "#17243B",
            ["SidebarBrush"] = "#17243B",
            ["SidebarMutedBrush"] = "#AFC0D8",
            ["InkBrush"] = "#17243B",
            ["MutedBrush"] = "#718096",
            ["AccentBrush"] = "#3157D5",
            ["AccentSoftBrush"] = "#EAF0FF",
            ["PanelBrush"] = "#FFFFFF",
            ["PageBrush"] = "#F3F6FB",
            ["BorderBrush"] = "#E4EAF2",
            ["SubtleBrush"] = "#EEF2F8",
            ["SuccessBrush"] = "#21865D",
            ["SuccessSoftBrush"] = "#E8F5EE",
            ["WarningBrush"] = "#C98224",
            ["WarningSoftBrush"] = "#FFF4E5",
            ["AiBrush"] = "#7654C8",
            ["AiSoftBrush"] = "#F1EDFC"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = "#0B1220",
            ["SidebarBrush"] = "#0B1220",
            ["SidebarMutedBrush"] = "#A9B8CC",
            ["InkBrush"] = "#E8EDF5",
            ["MutedBrush"] = "#A7B2C2",
            ["AccentBrush"] = "#91AFFF",
            ["AccentSoftBrush"] = "#263B67",
            ["PanelBrush"] = "#1A2638",
            ["PageBrush"] = "#101827",
            ["BorderBrush"] = "#35445B",
            ["SubtleBrush"] = "#25344A",
            ["SuccessBrush"] = "#63C294",
            ["SuccessSoftBrush"] = "#1E3B32",
            ["WarningBrush"] = "#EAB56A",
            ["WarningSoftBrush"] = "#44331D",
            ["AiBrush"] = "#B7A1FF",
            ["AiSoftBrush"] = "#342B50"
        };

    private void ConfigureThemeSynchronization()
    {
        ApplicationThemeManager.Changed += (theme, _) => ApplyCustomThemePalette(theme);
        ApplicationThemeManager.ApplySystemTheme();
        ApplyCustomThemePalette(ApplicationThemeManager.GetAppTheme());
    }

    private void ApplyCustomThemePalette(Wpf.Ui.Controls.ApplicationTheme theme)
    {
        if (theme == Wpf.Ui.Controls.ApplicationTheme.HighContrast)
        {
            ApplyHighContrastPalette();
            return;
        }

        ApplyPalette(theme == Wpf.Ui.Controls.ApplicationTheme.Dark ? DarkPalette : LightPalette);
    }

    private void ApplyHighContrastPalette()
    {
        var palette = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = SystemColors.ControlColor.ToString(),
            ["SidebarBrush"] = SystemColors.ControlColor.ToString(),
            ["SidebarMutedBrush"] = SystemColors.GrayTextColor.ToString(),
            ["InkBrush"] = SystemColors.WindowTextColor.ToString(),
            ["MutedBrush"] = SystemColors.GrayTextColor.ToString(),
            ["AccentBrush"] = SystemColors.HighlightColor.ToString(),
            ["AccentSoftBrush"] = SystemColors.HighlightColor.ToString(),
            ["PanelBrush"] = SystemColors.WindowColor.ToString(),
            ["PageBrush"] = SystemColors.WindowColor.ToString(),
            ["BorderBrush"] = SystemColors.WindowTextColor.ToString(),
            ["SubtleBrush"] = SystemColors.ControlColor.ToString(),
            ["SuccessBrush"] = SystemColors.HighlightColor.ToString(),
            ["SuccessSoftBrush"] = SystemColors.ControlColor.ToString(),
            ["WarningBrush"] = SystemColors.HighlightColor.ToString(),
            ["WarningSoftBrush"] = SystemColors.ControlColor.ToString(),
            ["AiBrush"] = SystemColors.HighlightColor.ToString(),
            ["AiSoftBrush"] = SystemColors.ControlColor.ToString()
        };

        ApplyPalette(palette);
    }

    private void ApplyPalette(IReadOnlyDictionary<string, string> palette)
    {
        foreach (var entry in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(entry.Value)!;
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            Resources[entry.Key] = brush;
        }
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ConfigureThemeSynchronization();

            // Keep every startup operation inside this boundary. In particular,
            // path/ACL setup and DI registration can fail before database startup.
            var dataDirectory = StudyHubDatabase.GetDataDirectory();
            AppDiagnostics.Configure(Path.Combine(dataDirectory, "logs"));
            AppDiagnostics.Info("startup", "Application startup started.");

            var services = new ServiceCollection();
            services.AddUniToStudyHubCore();
            services.AddUniToStudyHubViewModels();
            services.AddSingleton<MainWindow>();

            _serviceProvider = services.BuildServiceProvider(validateScopes: true);
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
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
