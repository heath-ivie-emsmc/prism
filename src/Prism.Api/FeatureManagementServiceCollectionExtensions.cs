using Prism.Infrastructure.FeatureManagement;

namespace Prism.Api;

/// <summary>Composition root for feature flags. See <c>docs/feature-flags.md</c>.</summary>
public static class FeatureManagementServiceCollectionExtensions
{
    /// <summary>
    /// Feature flags come from AWS App Config through the agent sidecar when <c>FeatureManagement:Enabled</c> is true, and from <c>FeatureFlags:*</c> in
    /// settings otherwise (local development). Call <c>MergeSidecarAppSettingsIfEnabledAsync</c> on the configuration first, so the freeform profile is
    /// in place before options are validated.
    /// </summary>
    public static IServiceCollection AddPrismFeatureManagement(this IServiceCollection services, ConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        return services.AddAppConfigSidecar(configuration);
    }
}
