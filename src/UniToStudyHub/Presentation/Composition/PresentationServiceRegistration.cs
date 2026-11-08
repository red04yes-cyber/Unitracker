using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Presentation.Navigation;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub.Presentation.Composition;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddUniToStudyHubPresentation(this IServiceCollection services)
    {
        services.AddUniToStudyHubViewModels();
        services.AddSingleton<INavigationService, NavigationService>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<CoursesViewModel>();
        services.AddSingleton<CalendarViewModel>();
        services.AddSingleton<StudyLabViewModel>();
        services.AddSingleton<AcademyViewModel>();
        services.AddSingleton<ProgressViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<ShellViewModel>();
        return services;
    }

    // Compatibility composition remains until the page operations move behind
    // their page-specific application APIs.
    public static IServiceCollection AddUniToStudyHubViewModels(this IServiceCollection services)
    {
        services.AddSingleton<StudyWorkspaceViewModel>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
