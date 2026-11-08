using UniToStudyHub.Presentation.Navigation;

namespace UniToStudyHub.Presentation.ViewModels;

/// <summary>
/// Transitional page-state adapter. Each page has a distinct ViewModel and
/// binding surface; the shared MainViewModel remains the compatibility owner
/// until its commands and data are moved into page-specific application APIs.
/// </summary>
public abstract class PageViewModelBase(
    MainViewModel state,
    INavigationService navigation)
{
    public MainViewModel State { get; } = state ?? throw new ArgumentNullException(nameof(state));

    protected INavigationService Navigation { get; } =
        navigation ?? throw new ArgumentNullException(nameof(navigation));

    public bool NavigateTo(StudyRoute route) => Navigation.NavigateTo(route);
}

public sealed class DashboardViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);

public sealed class CoursesViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);

public sealed class CalendarViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);

public sealed class StudyLabViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation)
{
    public void RefreshFocusSnapshot() => State.Workspace.RefreshFocusSnapshot();
}

public sealed class AcademyViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);

public sealed class ProgressViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);

public sealed class SettingsViewModel(MainViewModel state, INavigationService navigation)
    : PageViewModelBase(state, navigation);
