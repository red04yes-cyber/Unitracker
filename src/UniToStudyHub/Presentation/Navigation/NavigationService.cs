namespace UniToStudyHub.Presentation.Navigation;

public enum StudyRoute
{
    Today = 0,
    Courses = 1,
    Calendar = 2,
    StudyLab = 3,
    Academy = 4,
    Progress = 5,
    Settings = 6
}

public sealed class NavigationChangedEventArgs(
    StudyRoute previousRoute,
    StudyRoute currentRoute) : EventArgs
{
    public StudyRoute PreviousRoute { get; } = previousRoute;
    public StudyRoute CurrentRoute { get; } = currentRoute;
}

public interface INavigationService
{
    StudyRoute CurrentRoute { get; }

    event EventHandler<NavigationChangedEventArgs>? Navigated;

    bool NavigateTo(StudyRoute route);
}

/// <summary>
/// Stores the active application route without depending on WPF windows,
/// controls, or view types.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private StudyRoute _currentRoute = StudyRoute.Today;

    public StudyRoute CurrentRoute => _currentRoute;

    public event EventHandler<NavigationChangedEventArgs>? Navigated;

    public bool NavigateTo(StudyRoute route)
    {
        if (!Enum.IsDefined(typeof(StudyRoute), route))
            return false;

        if (_currentRoute == route)
            return true;

        var previousRoute = _currentRoute;
        _currentRoute = route;
        Navigated?.Invoke(this, new NavigationChangedEventArgs(previousRoute, route));
        return true;
    }
}
