using System.ComponentModel;
using System.Runtime.CompilerServices;
using UniToStudyHub.Presentation.Navigation;

namespace UniToStudyHub.Presentation.ViewModels;

/// <summary>
/// Application shell state and page routing. The page view models are
/// long-lived singletons so their presentation state survives navigation.
/// </summary>
public sealed class ShellViewModel : INotifyPropertyChanged
{
    private readonly INavigationService _navigation;

    public ShellViewModel(
        DashboardViewModel dashboard,
        CoursesViewModel courses,
        CalendarViewModel calendar,
        StudyLabViewModel studyLab,
        AcademyViewModel academy,
        ProgressViewModel progress,
        SettingsViewModel settings,
        INavigationService navigation)
    {
        Dashboard = dashboard ?? throw new ArgumentNullException(nameof(dashboard));
        Courses = courses ?? throw new ArgumentNullException(nameof(courses));
        Calendar = calendar ?? throw new ArgumentNullException(nameof(calendar));
        StudyLab = studyLab ?? throw new ArgumentNullException(nameof(studyLab));
        Academy = academy ?? throw new ArgumentNullException(nameof(academy));
        Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _navigation.Navigated += OnNavigated;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public DashboardViewModel Dashboard { get; }
    public CoursesViewModel Courses { get; }
    public CalendarViewModel Calendar { get; }
    public StudyLabViewModel StudyLab { get; }
    public AcademyViewModel Academy { get; }
    public ProgressViewModel Progress { get; }
    public SettingsViewModel Settings { get; }

    /// <summary>
    /// Compatibility access for startup initialization. Remove after page
    /// commands and data move into page-specific application APIs.
    /// </summary>
    public MainViewModel LegacyContent => Dashboard.State;

    public StudyRoute CurrentRoute => _navigation.CurrentRoute;

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

    private void OnNavigated(object? sender, NavigationChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CurrentRoute));
        OnPropertyChanged(nameof(SelectedPageIndex));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
