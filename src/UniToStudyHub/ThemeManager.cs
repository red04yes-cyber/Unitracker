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
            ["NavyBrush"] = ("#17243B", "#111B2D"),
            ["SidebarBrush"] = ("#17243B", "#0C1423"),
            ["SidebarTextBrush"] = ("#F7FAFF", "#EEF3FC"),
            ["SidebarMutedBrush"] = ("#A7B7CD", "#91A1B9"),
            ["SidebarHoverBrush"] = ("#223652", "#1B2A43"),
            ["SidebarActiveBrush"] = ("#2B4168", "#23365A"),
            ["SidebarBadgeBrush"] = ("#263955", "#1B2D4D"),
            ["InkBrush"] = ("#18243A", "#ECF1F9"),
            ["MutedBrush"] = ("#65738B", "#9AAAC0"),
            ["AccentBrush"] = ("#3457D5", "#4565EE"),
            ["AccentTextBrush"] = ("#3457D5", "#A4B8FF"),
            ["AccentSoftBrush"] = ("#EAF0FF", "#26375F"),
            ["PanelBrush"] = ("#FFFFFF", "#151F30"),
            ["PageBrush"] = ("#F4F6FA", "#0E1626"),
            ["BorderBrush"] = ("#E4EAF2", "#2A3850"),
            ["SubtleBrush"] = ("#F0F3F8", "#1B293D"),
            ["SuccessBrush"] = ("#21865D", "#67D7A4"),
            ["SuccessSoftBrush"] = ("#E8F5EE", "#193D34"),
            ["WarningBrush"] = ("#A96716", "#F3BD63"),
            ["WarningSoftBrush"] = ("#FFF4E5", "#45351F"),
            ["AiBrush"] = ("#7654C8", "#BEA7FF"),
            ["AiSoftBrush"] = ("#F1EDFC", "#342B50")
        };

    public static bool IsDarkMode { get; private set; }

    public static void ApplySavedTheme()
    {
        var saved = "light";
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
