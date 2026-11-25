using Prism.Application.Personas;
using Prism.Infrastructure.Personas;

namespace Prism.Api.Personas;

/// <summary>Registers Personas, the questionnaire and the home page.</summary>
public static class PersonaServiceCollectionExtensions
{
    public static IServiceCollection AddPrismPersonas(this IServiceCollection services)
    {
        services.AddScoped<IPersonaStore, PersonaStore>();
        services.AddScoped<IOnboardingQuestionStore, OnboardingQuestionStore>();
        services.AddScoped<IOnboardingService, OnboardingService>();
        services.AddScoped<IHomePage, HomePageService>();
        return services;
    }

    public static void MapPersonaEndpoints(this RouteGroupBuilder v1)
    {
        PersonaAdminEndpoints.Map(v1);
        OnboardingEndpoints.Map(v1);
        HomePageEndpoints.Map(v1);
    }
}
