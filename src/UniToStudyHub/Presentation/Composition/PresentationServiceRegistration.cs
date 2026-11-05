using Microsoft.Extensions.DependencyInjection;
using UniToStudyHub.Presentation.ViewModels;

namespace UniToStudyHub.Presentation.Composition;

public static class PresentationServiceRegistration
{
    public static IServiceCollection AddUniToStudyHubViewModels(this IServiceCollection services)
    {
        services.AddSingleton<StudyWorkspaceViewModel>();
        services.AddSingleton<MainViewModel>();
        return services;
    }
}
