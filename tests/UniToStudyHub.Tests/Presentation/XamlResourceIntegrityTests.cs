using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UniToStudyHub.Tests.Presentation;

public sealed class XamlResourceIntegrityTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly Regex ResourceReference = new(
        @"\{(?:StaticResource|DynamicResource)\s+(?<key>[^}\s]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex EventHandlerReference = new(
        @"\b(?:Click|ValueChanged|RequestNavigate|MediaEnded|Loaded|SelectionChanged|Checked|Unchecked|TextChanged|KeyDown|MouseDoubleClick|Drop|DragOver|Closing|Closed|SizeChanged)\s*=\s*""(?<name>[A-Za-z_][A-Za-z0-9_]*)""",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    [Fact]
    public void ApplicationAndMainWindowXamlAreWellFormed()
    {
        var application = LoadXaml("UniToStudyHub.Tests.Presentation.App.xaml");
        var mainWindow = LoadXaml("UniToStudyHub.Tests.Presentation.MainWindow.xaml");

        Assert.Equal("UniToStudyHub.App", (string?)application.Root?.Attribute(XamlNamespace + "Class"));
        Assert.Equal("UniToStudyHub.MainWindow", (string?)mainWindow.Root?.Attribute(XamlNamespace + "Class"));
    }

    [Fact]
    public void MainWindowResourceReferencesResolveAgainstApplicationAndWindowResources()
    {
        var application = LoadXaml("UniToStudyHub.Tests.Presentation.App.xaml");
        var mainWindow = LoadXaml("UniToStudyHub.Tests.Presentation.MainWindow.xaml");
        var declaredKeys = application.Root!
            .DescendantsAndSelf()
            .Attributes(XamlNamespace + "Key")
            .Concat(mainWindow.Root!.DescendantsAndSelf().Attributes(XamlNamespace + "Key"))
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);

        var missingKeys = mainWindow.Root!
            .DescendantsAndSelf()
            .Attributes()
            .SelectMany(attribute => ResourceReference.Matches(attribute.Value)
                .Select(match => match.Groups["key"].Value))
            .Where(key => !key.StartsWith("{", StringComparison.Ordinal))
            .Where(key => !declaredKeys.Contains(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.True(missingKeys.Length == 0,
            "MainWindow.xaml references resource keys that are not declared in App.xaml or Window.Resources: " +
            string.Join(", ", missingKeys));
    }

    [Fact]
    public void MainWindowEventHandlersExistInCodeBehind()
    {
        var mainWindow = LoadXaml("UniToStudyHub.Tests.Presentation.MainWindow.xaml");
        var codeBehind = LoadEmbeddedText("UniToStudyHub.Tests.Presentation.MainWindow.xaml.cs");
        var handlers = mainWindow.Root!
            .DescendantsAndSelf()
            .Attributes()
            .SelectMany(attribute => EventHandlerReference.Matches(attribute.Value)
                .Select(match => match.Groups["name"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var missingHandlers = handlers
            .Where(handler => !Regex.IsMatch(codeBehind,
                @"\b" + Regex.Escape(handler) + @"\s*\(",
                RegexOptions.CultureInvariant))
            .OrderBy(handler => handler, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(handlers);
        Assert.True(missingHandlers.Length == 0,
            "MainWindow.xaml references event handlers not found in MainWindow.xaml.cs: " +
            string.Join(", ", missingHandlers));
    }

    private static XDocument LoadXaml(string resourceName)
    {
        using var stream = OpenEmbeddedResource(resourceName);
        return XDocument.Load(stream, LoadOptions.SetLineInfo);
    }

    private static string LoadEmbeddedText(string resourceName)
    {
        using var stream = OpenEmbeddedResource(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Stream OpenEmbeddedResource(string resourceName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded test resource '{resourceName}' was not found.");
}
