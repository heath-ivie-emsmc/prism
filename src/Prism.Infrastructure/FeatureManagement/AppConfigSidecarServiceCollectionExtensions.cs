
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public static class AppConfigSidecarServiceCollectionExtensions
{
    /// <summary>Registers the App Config sidecar client, dynamic appsettings provider, and the readiness check that <c>/ready</c> includes.</summary>
    public static IServiceCollection AddAppConfigSidecar(
        this IServiceCollection services,
        IConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<FeatureManagementBootstrapState>();

        FeatureManagementOptions initialOptions =
            configuration.GetSection(FeatureManagementOptions.SectionName).Get<FeatureManagementOptions>()
            ?? new FeatureManagementOptions();

        // Register the sidecar hosted service before options ValidateOnStart so freeform App Config
        // is merged before StartupValidator runs (hosted services start in registration order).
        if (!initialOptions.Enabled)
        {
            FeatureManagementSidecarRegistration.AddConfigOnlyPath(services);
        }
        else
        {
            FeatureManagementSidecarRegistration.AddSidecarStack(services, configuration);
        }

        FeatureManagementSidecarRegistration.AddOptionsBinding(services, configuration);

        services.AddSingleton<IFeatureManagementReadiness, FeatureManagementReadiness>();

        return services;
    }
}
