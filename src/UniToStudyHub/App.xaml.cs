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
            ["NavyBrush"] = "#1B3154",
            ["SidebarBrush"] = "#17243B",
            ["SidebarMutedBrush"] = "#C0CDE0",
            ["SidebarDividerBrush"] = "#50627D",
            ["InkBrush"] = "#1B2434",
            ["MutedBrush"] = "#52627A",
            ["AccentBrush"] = "#3157D5",
            ["AccentFillBrush"] = "#3157D5",
            ["AccentSoftBrush"] = "#E9F0FF",
            ["PanelBrush"] = "#FFFFFF",
            ["PageBrush"] = "#F3F6FC",
            ["BorderBrush"] = "#DAE2F0",
            ["SubtleBrush"] = "#EDF2FA",
            ["SuccessBrush"] = "#187A50",
            ["SuccessSoftBrush"] = "#E7F5ED",
            ["WarningBrush"] = "#9A5B10",
            ["WarningSoftBrush"] = "#FFF3E1",
            ["AiBrush"] = "#6B52C8",
            ["AiSoftBrush"] = "#F0ECFF",
            ["GlassSurfaceBrush"] = "#EBFFFFFF",
            ["GlassSidebarBrush"] = "#D917243B",
            ["GlassTopbarBrush"] = "#E6FFFFFF",
            ["GlassBorderBrush"] = "#BFD5E0F0",
            ["GlassHoverBrush"] = "#22FFFFFF",
            ["FocusRingBrush"] = "#3157D5"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = "#0D1728",
            ["SidebarBrush"] = "#0C1628",
            ["SidebarMutedBrush"] = "#C0CBE0",
            ["SidebarDividerBrush"] = "#34445C",
            ["InkBrush"] = "#F1F5FB",
            ["MutedBrush"] = "#B1BED0",
            ["AccentBrush"] = "#91AAEF",
            ["AccentFillBrush"] = "#3157D5",
            ["AccentSoftBrush"] = "#1E3156",
            ["PanelBrush"] = "#1B2940",
            ["PageBrush"] = "#101B2C",
            ["BorderBrush"] = "#34445C",
            ["SubtleBrush"] = "#202F47",
            ["SuccessBrush"] = "#65C99A",
            ["SuccessSoftBrush"] = "#183C32",
            ["WarningBrush"] = "#E8B66C",
            ["WarningSoftBrush"] = "#3A3023",
            ["AiBrush"] = "#B5A8F5",
            ["AiSoftBrush"] = "#302B4F",
            ["GlassSurfaceBrush"] = "#EB1B2940",
            ["GlassSidebarBrush"] = "#D90C1628",
            ["GlassTopbarBrush"] = "#E6101B2C",
            ["GlassBorderBrush"] = "#80566B88",
            ["GlassHoverBrush"] = "#1AFFFFFF",
            ["FocusRingBrush"] = "#91AAEF"
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
            ["AiSoftBrush"] = SystemColors.ControlColor.ToString(),
            ["GlassSurfaceBrush"] = SystemColors.WindowColor.ToString(),
            ["GlassSidebarBrush"] = SystemColors.ControlColor.ToString(),
            ["GlassTopbarBrush"] = SystemColors.WindowColor.ToString(),
            ["GlassBorderBrush"] = SystemColors.WindowTextColor.ToString(),
            ["GlassHoverBrush"] = SystemColors.HighlightColor.ToString(),
            ["FocusRingBrush"] = SystemColors.HighlightColor.ToString()
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
