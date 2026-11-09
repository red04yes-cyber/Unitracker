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
            ["NavyBrush"] = "#F5F2FA",
            ["SidebarBrush"] = "#F7F5FB",
            ["SidebarMutedBrush"] = "#6D667A",
            ["SidebarDividerBrush"] = "#E7E1EF",
            ["InkBrush"] = "#211A2B",
            ["MutedBrush"] = "#716A7D",
            ["AccentBrush"] = "#7047C9",
            ["AccentFillBrush"] = "#7950D6",
            ["AccentSoftBrush"] = "#EEE7FA",
            ["PanelBrush"] = "#FFFFFF",
            ["ElevatedPanelBrush"] = "#FCFAFF",
            ["InputBrush"] = "#F4F1F8",
            ["PageBrush"] = "#F7F5FA",
            ["BorderBrush"] = "#E8E2EF",
            ["SubtleBrush"] = "#F0ECF5",
            ["HoverBrush"] = "#EEE7F8",
            ["GlassBrush"] = "#E6FFFFFF",
            ["SuccessBrush"] = "#258A68",
            ["SuccessSoftBrush"] = "#E7F5EF",
            ["WarningBrush"] = "#A86B1F",
            ["WarningSoftBrush"] = "#FBF1E3",
            ["DangerBrush"] = "#B94357",
            ["DangerSoftBrush"] = "#F9E9ED",
            ["InfoBrush"] = "#326C9E",
            ["InfoSoftBrush"] = "#E8F1F9",
            ["AiBrush"] = "#7047C9",
            ["AiSoftBrush"] = "#EEE7FA",
            ["FocusBrush"] = "#7950D6"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NavyBrush"] = "#100E18",
            ["SidebarBrush"] = "#100E18",
            ["SidebarMutedBrush"] = "#A7A1B5",
            ["SidebarDividerBrush"] = "#292536",
            ["InkBrush"] = "#F3F0FA",
            ["MutedBrush"] = "#AAA4B8",
            ["AccentBrush"] = "#B79CFF",
            ["AccentFillBrush"] = "#8B5CF6",
            ["AccentSoftBrush"] = "#302442",
            ["PanelBrush"] = "#1B1825",
            ["ElevatedPanelBrush"] = "#211D2D",
            ["InputBrush"] = "#14111D",
            ["PageBrush"] = "#100E17",
            ["BorderBrush"] = "#302B3A",
            ["SubtleBrush"] = "#24202F",
            ["HoverBrush"] = "#2B2439",
            ["GlassBrush"] = "#D91B1724",
            ["SuccessBrush"] = "#66D1A8",
            ["SuccessSoftBrush"] = "#1C382F",
            ["WarningBrush"] = "#E8BA72",
            ["WarningSoftBrush"] = "#3D2E1F",
            ["DangerBrush"] = "#FF858F",
            ["DangerSoftBrush"] = "#43252D",
            ["InfoBrush"] = "#8FC8FF",
            ["InfoSoftBrush"] = "#1D3042",
            ["AiBrush"] = "#C6A8FF",
            ["AiSoftBrush"] = "#302441",
            ["FocusBrush"] = "#B79CFF"
        };

    private void ConfigureThemeSynchronization()
    {
        ApplicationThemeManager.Changed += (theme, _) => ApplyCustomThemePalette(theme);
        ApplicationThemeManager.ApplySystemTheme();
        var initialTheme = SystemParameters.HighContrast
            ? ApplicationTheme.HighContrast
            : ApplicationTheme.Dark;
        ApplicationThemeManager.Apply(initialTheme);
        ApplyCustomThemePalette(initialTheme);
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
            ["ElevatedPanelBrush"] = SystemColors.WindowColor.ToString(),
            ["InputBrush"] = SystemColors.WindowColor.ToString(),
            ["HoverBrush"] = SystemColors.HighlightColor.ToString(),
            ["GlassBrush"] = SystemColors.WindowColor.ToString(),
            ["DangerBrush"] = SystemColors.WindowTextColor.ToString(),
            ["DangerSoftBrush"] = SystemColors.ControlColor.ToString(),
            ["InfoBrush"] = SystemColors.HighlightColor.ToString(),
            ["InfoSoftBrush"] = SystemColors.ControlColor.ToString(),
            ["FocusBrush"] = SystemColors.HighlightColor.ToString()
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
