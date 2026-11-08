using System.IO;
using System.Windows;
using System.Windows.Media;

namespace UniToStudyHub;

internal static class ThemeManager
{
    private static readonly string PreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "UniToStudyHub",
        "theme.txt");

    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Palette =
        new Dictionary<string, (string Light, string Dark)>
        {
            ["NavyBrush"] = ("#17243B", "#111B2B"),
            ["SidebarBrush"] = ("#17243B", "#0B1423"),
            ["SidebarTextBrush"] = ("#F7FAFF", "#F0F5FC"),
            ["SidebarMutedBrush"] = ("#A7B7CD", "#93A6BF"),
            ["SidebarHoverBrush"] = ("#223652", "#1B2C43"),
            ["SidebarActiveBrush"] = ("#2B4168", "#173B55"),
            ["SidebarBadgeBrush"] = ("#263955", "#1B2D43"),
            ["InkBrush"] = ("#18243A", "#EEF4FC"),
            ["MutedBrush"] = ("#65738B", "#A2B2C8"),
            ["AccentBrush"] = ("#2866D8", "#16BDE5"),
            ["AccentTextBrush"] = ("#2866D8", "#66DDF5"),
            ["AccentSoftBrush"] = ("#EAF0FF", "#193B52"),
            ["PanelBrush"] = ("#FFFFFF", "#172538"),
            ["PageBrush"] = ("#F4F6FA", "#0D1726"),
            ["BorderBrush"] = ("#E4EAF2", "#2A3D55"),
            ["SubtleBrush"] = ("#F0F3F8", "#1C2C42"),
            ["SuccessBrush"] = ("#21865D", "#63D9A4"),
            ["SuccessSoftBrush"] = ("#E8F5EE", "#173E36"),
            ["WarningBrush"] = ("#A96716", "#F1BE62"),
            ["WarningSoftBrush"] = ("#FFF4E5", "#40351F"),
            ["AiBrush"] = ("#7654C8", "#BBA4FF"),
            ["AiSoftBrush"] = ("#F1EDFC", "#342C50"),
            ["AccentCyanBrush"] = ("#168DB5", "#16BDE5"),
            ["ElectricBlueBrush"] = ("#2866D8", "#3978F6"),
            ["BrickAccentBrush"] = ("#A94740", "#D36A61"),
            ["GoldAccentBrush"] = ("#A7771E", "#E7B84F"),
            ["GlassBrush"] = ("#F2FFFFFF", "#D91A2A40"),
            ["GlassBorderBrush"] = ("#D9DDE7F0", "#554D6680")
        };

    public static bool IsDarkMode { get; private set; }

    public static void ApplySavedTheme()
    {
        var saved = "dark";
        try
        {
            if (File.Exists(PreferencePath))
                saved = File.ReadAllText(PreferencePath).Trim();
        }
        catch
        {
            // A missing or unreadable preference must never block startup.
        }

        Apply(string.Equals(saved, "dark", StringComparison.OrdinalIgnoreCase));
    }

    public static void ToggleTheme()
    {
        Apply(!IsDarkMode);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath)!);
            File.WriteAllText(PreferencePath, IsDarkMode ? "dark" : "light");
        }
        catch
        {
            // Theme changes remain active for this session if persistence fails.
        }
    }

    private static void Apply(bool dark)
    {
        IsDarkMode = dark;
        foreach (var (key, colors) in Palette)
        {
            var hex = dark ? colors.Dark : colors.Light;
            var color = (Color)ColorConverter.ConvertFromString(hex);
            Application.Current.Resources[key] = new SolidColorBrush(color);
        }
    }
}
