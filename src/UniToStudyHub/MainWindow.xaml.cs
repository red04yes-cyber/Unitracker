using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shellViewModel;
    private readonly DispatcherTimer _focusRefreshTimer;

    public MainWindow(ShellViewModel shellViewModel)
    {
        _shellViewModel = shellViewModel ?? throw new ArgumentNullException(nameof(shellViewModel));

        InitializeComponent();
        DataContext = _shellViewModel;
        MainTabs.SetBinding(Selector.SelectedIndexProperty, new Binding(nameof(ShellViewModel.SelectedPageIndex))
        {
            Source = _shellViewModel,
            Mode = BindingMode.TwoWay
        });

        MainTabs.SelectionChanged += MainTabs_SelectionChanged;
        UpdateThemeToggleButtonContent();
        UpdatePageHeader();

        _focusRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _focusRefreshTimer.Tick += (_, _) => _shellViewModel.StudyLab.RefreshFocusSnapshot();
        _focusRefreshTimer.Start();
        Closed += (_, _) => _focusRefreshTimer.Stop();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.ToggleTheme();
        UpdateThemeToggleButtonContent();
    }

    private void UpdateThemeToggleButtonContent()
    {
        MainTabs.ApplyTemplate();
        if (MainTabs.Template.FindName("ThemeToggleButton", MainTabs) is Button themeButton)
        {
            themeButton.Content = ThemeManager.IsDarkMode
                ? "☀ Modalità chiara"
                : "☾ Modalità scura";
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs))
            return;

        UpdatePageHeader();
    }

    private void UpdatePageHeader()
    {
        MainTabs.ApplyTemplate();
        if (MainTabs.SelectedItem is not TabItem selected ||
            selected.Header is not StackPanel header)
            return;

        var labelsContainer = header.Children.OfType<StackPanel>().FirstOrDefault();
        var labels = labelsContainer?.Children.OfType<TextBlock>().ToArray();
        if (labels is null || labels.Length < 2)
            return;

        if (MainTabs.Template.FindName("PageHeading", MainTabs) is TextBlock heading)
            heading.Text = labels[0].Text;
        if (MainTabs.Template.FindName("PageSubheading", MainTabs) is TextBlock subheading)
            subheading.Text = labels[1].Text;
    }

    private void NavigateToSettings_Click(object sender, RoutedEventArgs e) =>
        _shellViewModel.SelectedPageIndex = (int)Presentation.Navigation.StudyRoute.Settings;
}
