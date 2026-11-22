using Microsoft.Extensions.Options;
using Prism.Application.SavedViews;
using Prism.Infrastructure.SavedViews;

namespace Prism.Api.SavedViews;

/// <summary>Composition and routes for Saved Views.</summary>
public static class SavedViewsServiceCollectionExtensions
{
    public static IServiceCollection AddPrismSavedViews(this IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<SavedViewInvitationOptions>, SavedViewInvitationOptionsValidator>();
        services.AddOptions<SavedViewInvitationOptions>()
            .BindConfiguration(SavedViewInvitationOptions.ConfigurationSection)
            .ValidateOnStart();

        services.AddScoped<ISavedViewStore, SavedViewStore>();
        services.AddScoped<ISavedViewInvitationStore, SavedViewInvitationStore>();
        services.AddSingleton<IInvitationEmailSender, SmtpInvitationEmailSender>();
        services.AddScoped<InvitationService>();
        return services;
    }

    public static void MapSavedViewEndpoints(this RouteGroupBuilder v1) => SavedViewEndpoints.Map(v1);
}

/// <summary>Refuses to start an enabled invitation setup that is missing what it needs.</summary>
internal sealed class SavedViewInvitationOptionsValidator(IHostEnvironment environment) : IValidateOptions<SavedViewInvitationOptions>
{
    public ValidateOptionsResult Validate(string? name, SavedViewInvitationOptions options)
    {
        IReadOnlyList<string> problems = SavedViewInvitationOptionsRules.Problems(options, environment.IsDeployedEnvironment());
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
