using System.ComponentModel;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UniToStudyHub.Presentation.Navigation;
using Xunit;

namespace UniToStudyHub.Tests.Presentation;

public sealed class SidebarNavigationShellTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly XNamespace AutomationNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void NavigationCatalogKeepsAllPageRoutesStableAndUnique()
    {
        var entries = SidebarNavigationCatalog.Entries;

        Assert.Equal(8, entries.Count);
        Assert.Equal(Enumerable.Range(0, 8), entries.Select(entry => entry.PageIndex));
        Assert.Equal(
            new[] { "Oggi", "Materie", "Calendario", "Studio Lab", "Accademia", "Progressi", "Profilo", "Impostazioni" },
            entries.Select(entry => entry.Title));
        Assert.All(entries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.Glyph));
        });

        var sectionStart = Assert.Single(entries.Where(entry => entry.IsSectionStart));
        Assert.Equal(6, sectionStart.PageIndex);
    }

    [Fact]
    public void NavigationSelectionRaisesPropertyChangedOnlyWhenTheStateChanges()
    {
        var items = SidebarNavigationCatalog.CreateItems();
        var first = items[0];
        var second = items[1];
        var changes = new List<string>();

        Assert.IsAssignableFrom<INotifyPropertyChanged>(first);
        first.PropertyChanged += (_, args) => changes.Add($"first:{args.PropertyName}");
        second.PropertyChanged += (_, args) => changes.Add($"second:{args.PropertyName}");

        first.SetSelected(true);
        first.SetSelected(true);
        second.SetSelected(true);
        first.SetSelected(false);

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Equal(
            new[] { "first:IsSelected", "second:IsSelected", "first:IsSelected" },
            changes);
    }

    [Fact]
    public void MainWindowUsesTheSidebarAndContentControlInsteadOfVisibleTabChrome()
    {
        var window = LoadXaml("UniToStudyHub.Tests.Presentation.MainWindow.xaml");
        var root = window.Root!;
        var presentation = root.GetDefaultNamespace();
        var sidebar = Assert.Single(root.Descendants()
            .Where(element => element.Name.LocalName == "StudyHubSidebar"));
        var contentHost = Assert.Single(root.Descendants(presentation + "ContentControl")
            .Where(element => (string?)element.Attribute(XamlNamespace + "Name") == "PageContentHost"));
        var pageRegistry = Assert.Single(root.Descendants(presentation + "TabControl")
            .Where(element => (string?)element.Attribute(XamlNamespace + "Name") == "MainTabs"));

        Assert.Equal("{Binding SelectedIndex, ElementName=MainTabs, Mode=TwoWay}",
            (string?)sidebar.Attribute("SelectedIndex"));
        Assert.Equal("{Binding SelectedContent, ElementName=MainTabs}",
            (string?)contentHost.Attribute("Content"));
        Assert.Equal("Collapsed", (string?)pageRegistry.Attribute("Visibility"));
        Assert.Equal("False", (string?)pageRegistry.Attribute("IsHitTestVisible"));
        Assert.Equal("980", (string?)root.Attribute("MinWidth"));
        Assert.Equal("680", (string?)root.Attribute("MinHeight"));
        Assert.Empty(root.Descendants(presentation + "TabPanel"));
        Assert.Empty(root.Descendants().Where(element =>
            element.Name.LocalName is "BackEase" or "Storyboard" or "DoubleAnimation"));
    }

    [Fact]
    public void SidebarCollapsesToIconWidthAndKeepsNavigationButtonsAccessible()
    {
        var sidebar = LoadXaml("UniToStudyHub.Tests.Presentation.StudyHubSidebar.xaml");
        var root = sidebar.Root!;
        var presentation = root.GetDefaultNamespace();

        Assert.Equal("80", (string?)root.Attribute("MinWidth"));
        Assert.Null(root.Attribute("Width"));

        var widthStyle = root.Element(presentation + "UserControl.Style")?.Element(presentation + "Style");
        Assert.NotNull(widthStyle);
        Assert.Contains(widthStyle!.Descendants(presentation + "Setter"),
            setter => (string?)setter.Attribute("Property") == "Width" &&
                      (string?)setter.Attribute("Value") == "236");
        Assert.Contains(widthStyle.Descendants(presentation + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsCollapsed" &&
                       (string?)trigger.Attribute("Value") == "True" &&
                       trigger.Elements(presentation + "Setter").Any(setter =>
                           (string?)setter.Attribute("Property") == "Width" &&
                           (string?)setter.Attribute("Value") == "80"));

        var toggle = Assert.Single(root.Descendants(presentation + "ToggleButton"));
        Assert.Equal(
            "{Binding IsCollapsed, RelativeSource={RelativeSource AncestorType={x:Type local:StudyHubSidebar}}, Mode=TwoWay}",
            (string?)toggle.Attribute("IsChecked"));
        Assert.False(string.IsNullOrWhiteSpace((string?)toggle.Attribute(AutomationNamespace + "Name")));

        var navigationButton = Assert.Single(root.Descendants(presentation + "DataTemplate")
            .SelectMany(template => template.Descendants(presentation + "Button")));
        Assert.Equal("{Binding Title}", (string?)navigationButton.Attribute("ToolTip"));
        Assert.Equal("{Binding Title}", (string?)navigationButton.Attribute(AutomationNamespace + "Name"));
        Assert.Equal("{Binding Description}", (string?)navigationButton.Attribute(AutomationNamespace + "HelpText"));

        Assert.Contains(root.Descendants(presentation + "Border"),
            border => (string?)border.Attribute("Width") == "3" &&
                      (string?)border.Attribute("Background") == "{DynamicResource AccentBrush}");
        Assert.Empty(root.Descendants().Where(element =>
            element.Name.LocalName is "BackEase" or "Storyboard" or "DoubleAnimation" or "DropShadowEffect"));
    }

    [Fact]
    public void SettingsShortcutTargetsTheSettingsPageAndSidebarClickUsesCatalogPageIndex()
    {
        var mainWindowCode = ReadResource("UniToStudyHub.Tests.Presentation.MainWindow.xaml.cs");
        var sidebarCode = ReadResource("UniToStudyHub.Tests.Presentation.StudyHubSidebar.xaml.cs");

        Assert.Matches(
            @"private void NavigateToSettings_Click\(object sender, RoutedEventArgs e\)\s*\{\s*MainTabs\.SelectedIndex\s*=\s*7;\s*\}",
            mainWindowCode);
        Assert.Matches(
            @"private void NavigationItem_Click\(object sender, RoutedEventArgs e\)\s*\{\s*if \(sender is Button \{ Tag: SidebarNavigationItem item \}\)\s*SelectedIndex = item\.PageIndex;\s*\}",
            sidebarCode);
    }

    private static XDocument LoadXaml(string resourceName)
    {
        using var stream = OpenEmbeddedResource(resourceName);
        return XDocument.Load(stream, LoadOptions.SetLineInfo);
    }

    private static string ReadResource(string resourceName)
    {
        using var stream = OpenEmbeddedResource(resourceName);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static Stream OpenEmbeddedResource(string resourceName) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
        ?? throw new InvalidOperationException($"Embedded test resource '{resourceName}' was not found.");
}
