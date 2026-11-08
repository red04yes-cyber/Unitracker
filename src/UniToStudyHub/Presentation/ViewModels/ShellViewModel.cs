using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniToStudyHub.Presentation.Navigation;

namespace UniToStudyHub.Presentation.ViewModels;

/// <summary>
/// Application shell state. LegacyContent is a temporary compatibility bridge
/// while the existing tab content is extracted into page-specific views.
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly INavigationService _navigation;

    public ShellViewModel(
        MainViewModel legacyContent,
        INavigationService navigation)
    {
        LegacyContent = legacyContent ?? throw new ArgumentNullException(nameof(legacyContent));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _navigation.Navigated += OnNavigated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Temporary host for existing page bindings. Remove when all routes use
    /// their own view models and UserControls.
    /// </summary>
    public MainViewModel LegacyContent { get; }

    public StudyRoute CurrentRoute => _navigation.CurrentRoute;

    /// <summary>
    /// Mirrors the stable sidebar order to keep the current TabControl working
    /// during the first navigation migration step.
    /// </summary>
    public int SelectedPageIndex
    {
        get => (int)_navigation.CurrentRoute;
        set
        {
            if (!Enum.IsDefined(typeof(StudyRoute), value))
                return;

            _navigation.NavigateTo((StudyRoute)value);
        }
    }

    public bool NavigateTo(StudyRoute route) => _navigation.NavigateTo(route);

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentRoute));
        OnPropertyChanged(nameof(SelectedPageIndex));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
