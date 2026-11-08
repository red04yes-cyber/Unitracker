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
            ["SidebarBrush"] = ("#17243B", "#0B1220"),
            ["SidebarMutedBrush"] = ("#AFC0D8", "#9AAAC2"),
            ["InkBrush"] = ("#17243B", "#E8EEF8"),
            ["MutedBrush"] = ("#718096", "#A1AEC2"),
            ["AccentBrush"] = ("#3157D5", "#91A9FF"),
            ["AccentSoftBrush"] = ("#EAF0FF", "#24345B"),
            ["PanelBrush"] = ("#FFFFFF", "#151F30"),
            ["PageBrush"] = ("#F3F6FB", "#0D1524"),
            ["BorderBrush"] = ("#E4EAF2", "#2A374C"),
            ["SubtleBrush"] = ("#EEF2F8", "#202C40"),
            ["SuccessBrush"] = ("#21865D", "#5CC99A"),
            ["SuccessSoftBrush"] = ("#E8F5EE", "#193B34"),
            ["WarningBrush"] = ("#C98224", "#E8AE58"),
            ["WarningSoftBrush"] = ("#FFF4E5", "#46351F"),
            ["AiBrush"] = ("#7654C8", "#B39AF5"),
            ["AiSoftBrush"] = ("#F1EDFC", "#30264A")
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
