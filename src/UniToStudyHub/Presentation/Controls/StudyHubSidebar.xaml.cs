using System.Windows;
using System.Windows.Controls;
using UniToStudyHub.Presentation.Navigation;

namespace UniToStudyHub.Presentation.Controls;

public partial class StudyHubSidebar : UserControl
{
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(StudyHubSidebar),
            new FrameworkPropertyMetadata(
                0,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnSelectedIndexChanged));

    public static readonly DependencyProperty IsCollapsedProperty =
        DependencyProperty.Register(
            nameof(IsCollapsed),
            typeof(bool),
            typeof(StudyHubSidebar),
            new FrameworkPropertyMetadata(false));

    public IReadOnlyList<SidebarNavigationItem> NavigationItems { get; } =
        SidebarNavigationCatalog.CreateItems();

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public bool IsCollapsed
    {
        get => (bool)GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    public StudyHubSidebar()
    {
        InitializeComponent();
        UpdateSelectedItem();
    }

    private static void OnSelectedIndexChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is StudyHubSidebar sidebar)
            sidebar.UpdateSelectedItem();
    }

    private void UpdateSelectedItem()
    {
        foreach (var item in NavigationItems)
            item.SetSelected(item.PageIndex == SelectedIndex);
    }

    private void NavigationItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: SidebarNavigationItem item })
            SelectedIndex = item.PageIndex;
    }
}
