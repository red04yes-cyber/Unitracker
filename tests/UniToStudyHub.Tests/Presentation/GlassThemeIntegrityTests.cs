using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UniToStudyHub.Tests.Presentation;

public sealed class GlassThemeIntegrityTests
{
    private static readonly Regex PaletteEntry = new(
        @"\[""(?<key>[^""]+)""\]\s*=\s*""(?<value>#[0-9A-Fa-f]{6,8})""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Theory]
    [InlineData("LightPalette")]
    [InlineData("DarkPalette")]
    public void GlassCardTextMeetsWcagAaContrast(string paletteName)
    {
        var source = LoadEmbeddedText("UniToStudyHub.Tests.Presentation.App.xaml.cs");
        var palette = ReadPalette(source, paletteName);

        var page = RequiredColor(palette, "PageBrush");
        var surface = RequiredColor(palette, "GlassSurfaceBrush");
        var ink = RequiredColor(palette, "InkBrush");
        var muted = RequiredColor(palette, "MutedBrush");
        var composedSurface = CompositeArgbOverRgb(surface, page);

        var bodyContrast = ContrastRatio(ink, composedSurface);
        var mutedContrast = ContrastRatio(muted, composedSurface);

        Assert.True(bodyContrast >= 4.5,
            $"{paletteName} body text contrast is {bodyContrast:F2}:1; WCAG AA requires 4.5:1.");
        Assert.True(mutedContrast >= 4.5,
            $"{paletteName} muted text contrast is {mutedContrast:F2}:1; WCAG AA requires 4.5:1.");
    }

    [Fact]
    public void GlassSurfacesAreAppliedToTheShellAndCardsWithoutBlurEffects()
    {
        var app = LoadEmbeddedText("UniToStudyHub.Tests.Presentation.App.xaml");
        var mainWindow = LoadEmbeddedText("UniToStudyHub.Tests.Presentation.MainWindow.xaml");
        var control = LoadEmbeddedText("UniToStudyHub.Tests.Presentation.CollapsibleSection.xaml");
        var combined = app + mainWindow + control;

        Assert.Contains("GlassSurfaceBrush", app, StringComparison.Ordinal);
        Assert.Contains("GlassSidebarBrush", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GlassTopbarBrush", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GlassBorderBrush", mainWindow, StringComparison.Ordinal);
        Assert.Contains("GlassShadow", app, StringComparison.Ordinal);
        Assert.False(combined.Contains("BlurEffect", StringComparison.Ordinal),
            "The glass treatment must not add expensive per-element blur effects.");
    }

    [Fact]
    public void CollapsibleSectionHasKeyboardToggleAutomationNameAndBoundContent()
    {
        using var stream = OpenEmbeddedResource("UniToStudyHub.Tests.Presentation.CollapsibleSection.xaml");
        var document = XDocument.Load(stream);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        var toggle = document.Descendants(presentation + "ToggleButton").SingleOrDefault();
        var presenter = document.Descendants(presentation + "ContentPresenter").SingleOrDefault();

        Assert.NotNull(toggle);
        Assert.NotNull(toggle!.Attribute("IsChecked"));
        Assert.Contains(toggle.Attributes(),
            attribute => attribute.Name.LocalName == "AutomationProperties.Name");
        Assert.NotNull(presenter);
        Assert.NotNull(presenter!.Attribute("Content"));
    }

    private static IReadOnlyDictionary<string, string> ReadPalette(string source, string paletteName)
    {
        var declaration = Regex.Match(source,
            Regex.Escape(paletteName) +
            @"\s*=\s*new Dictionary<string, string>\(StringComparer\.Ordinal\)\s*\{(?<body>.*?)\n\s*\};",
            RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(declaration.Success, $"Could not find {paletteName} in App.xaml.cs.");

        return PaletteEntry.Matches(declaration.Groups["body"].Value)
            .ToDictionary(match => match.Groups["key"].Value,
                match => match.Groups["value"].Value,
                StringComparer.Ordinal);
    }

    private static string RequiredColor(IReadOnlyDictionary<string, string> palette, string key)
    {
        Assert.True(palette.TryGetValue(key, out var value), $"Palette is missing {key}.");
        return value!;
    }

    private static string CompositeArgbOverRgb(string foreground, string background)
    {
        Assert.StartsWith("#", foreground, StringComparison.Ordinal);
        Assert.Equal(9, foreground.Length);
        var alpha = Convert.ToInt32(foreground.Substring(1, 2), 16) / 255d;
        var foregroundChannels = ParseRgb(foreground.Substring(3));
        var backgroundChannels = ParseRgb(background);

        var composed = foregroundChannels.Zip(backgroundChannels,
            (front, back) => (int)Math.Round(front * alpha + back * (1 - alpha)));
        return "#" + string.Concat(composed.Select(channel => channel.ToString("X2")));
    }

    private static int[] ParseRgb(string color)
    {
        var value = color.StartsWith("#", StringComparison.Ordinal) ? color[1..] : color;
        Assert.Equal(6, value.Length);
        return
        [
            Convert.ToInt32(value.Substring(0, 2), 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16)
        ];
    }

    private static double ContrastRatio(string first, string second)
    {
        var firstLuminance = RelativeLuminance(ParseRgb(first));
        var secondLuminance = RelativeLuminance(ParseRgb(second));
        var lighter = Math.Max(firstLuminance, secondLuminance);
        var darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(IReadOnlyList<int> channels)
    {
        static double Linearize(int channel)
        {
            var value = channel / 255d;
            return value <= 0.04045
                ? value / 12.92
                : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Linearize(channels[0])
             + 0.7152 * Linearize(channels[1])
             + 0.0722 * Linearize(channels[2]);
    }

    private static string LoadEmbeddedText(string resourceName)
    {
        using var stream = OpenEmbeddedResource(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Stream OpenEmbeddedResource(string resourceName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
}
