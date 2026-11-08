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
        services.AddSingleton<ShellViewModel>();
        return services;
    }

    // Retained for existing test/bootstrap callers until the composition
    // migration is complete.
    public static IServiceCollection AddUniToStudyHubViewModels(this IServiceCollection services)
    {
        services.AddSingleton<StudyWorkspaceViewModel>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
