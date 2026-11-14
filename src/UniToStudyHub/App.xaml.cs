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
            ["SidebarDividerBrush"] = "#35445B",
            ["InkBrush"] = "#1B1530",
            ["MutedBrush"] = "#6F6788",
            ["AccentBrush"] = "#6D3FE0",
            ["AccentFillBrush"] = "#7C4DEB",
            ["AccentSoftBrush"] = "#EFE9FF",
            ["PanelBrush"] = "#FFFFFF",
            ["PageBrush"] = "#F6F4FB",
            ["BorderBrush"] = "#E4DEF0",
            ["SubtleBrush"] = "#F0ECF8",
            ["SuccessBrush"] = "#21865D",
            ["SuccessSoftBrush"] = "#E8F5EE",
            ["WarningBrush"] = "#C98224",
            ["WarningSoftBrush"] = "#FFF4E5",
            ["AiBrush"] = "#7C4DEB",
            ["AiSoftBrush"] = "#F1EBFF"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = "#0A0912",
            ["SidebarBrush"] = "#0B0A14",
            ["SidebarMutedBrush"] = "#A59DC0",
            ["SidebarDividerBrush"] = "#241F36",
            ["InkBrush"] = "#F5F2FF",
            ["MutedBrush"] = "#9A93B3",
            ["AccentBrush"] = "#B79CFF",
            ["AccentFillBrush"] = "#8B5CF6",
            ["AccentSoftBrush"] = "#271D45",
            ["PanelBrush"] = "#171423",
            ["PageBrush"] = "#0E0C17",
            ["BorderBrush"] = "#2B2540",
            ["SubtleBrush"] = "#201B31",
            ["SuccessBrush"] = "#5DD6A3",
            ["SuccessSoftBrush"] = "#16302A",
            ["WarningBrush"] = "#F2B866",
            ["WarningSoftBrush"] = "#352A1D",
            ["AiBrush"] = "#C4A8FF",
            ["AiSoftBrush"] = "#2A2044"
        };

    private void ConfigureThemeSynchronization()
    {
        ApplicationThemeManager.Changed += (theme, _) => ApplyCustomThemePalette(theme);
        ApplicationThemeManager.ApplySystemTheme();
        ApplyCustomThemePalette(ApplicationThemeManager.GetAppTheme());
    }

    private void ApplyCustomThemePalette(ApplicationTheme theme)
    {
        if (theme == ApplicationTheme.HighContrast)
        {
            ApplyHighContrastPalette();
            return;
        }

        ApplyPalette(theme == ApplicationTheme.Dark ? DarkPalette : LightPalette);
    }

    private void ApplyHighContrastPalette()
    {
        var palette = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = SystemColors.ControlColor.ToString(),
            ["SidebarBrush"] = SystemColors.ControlColor.ToString(),
            ["SidebarMutedBrush"] = SystemColors.GrayTextColor.ToString(),
            ["SidebarDividerBrush"] = SystemColors.WindowTextColor.ToString(),
            ["InkBrush"] = SystemColors.WindowTextColor.ToString(),
            ["MutedBrush"] = SystemColors.GrayTextColor.ToString(),
            ["AccentBrush"] = SystemColors.HighlightColor.ToString(),
            ["AccentFillBrush"] = SystemColors.HighlightColor.ToString(),
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
            // Preserve the sanitized exception chain in local diagnostics. WPF
            // XamlParseException often wraps the actionable resource/control
            // failure in InnerException, which is needed to debug startup.
            for (var current = ex; current is not null; current = current.InnerException)
            {
                AppDiagnostics.Error("startup", $"Startup exception detail: {current.GetType().Name}: {current.Message}");
            }
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
