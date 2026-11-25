
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

using Polly;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public static class FeatureManagementSidecarRegistration
{
    public static void AddSidecarStack(IServiceCollection services, IConfigurationManager configuration)
    {
        var provider = new SidecarConfigurationProvider();
        configuration.Add(provider);

        services.AddSingleton(provider);
        services.AddSingleton<IAppConfigSidecarConnectivityProbe, TcpAppConfigSidecarConnectivityProbe>();

        services.AddHttpClient<AppConfigSidecarClient>()
            .AddResilienceHandler(
                "sidecar",
                static (builder, context) =>
                {
                    context.EnableReloads<FeatureManagementOptions>();

                    FeatureManagementOptions options = context.GetOptions<FeatureManagementOptions>();
                    builder.AddRetry(new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 3,
                        Delay = TimeSpan.FromMilliseconds(200),
                        BackoffType = DelayBackoffType.Exponential,
                        UseJitter = true
                    });
                    builder.AddTimeout(new HttpTimeoutStrategyOptions
                    {
                        Timeout = options.RequestTimeout
                    });
                });
        services.AddSingleton<SidecarState>();
        services.AddHostedService<AppConfigSidecarHostedService>();
        services.AddSingleton<IFeatureValueManager, FeatureValueManager>();
    }

    public static void AddConfigOnlyPath(IServiceCollection services)
    {
        services.AddSingleton<IFeatureValueManager, ConfigurationFeatureValueManager>();
        services.AddHostedService<FeatureManagementConfigOnlyBootstrapMarker>();
    }

    public static void AddOptionsBinding(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeatureManagementOptions>()
            .Bind(configuration.GetSection(FeatureManagementOptions.SectionName))
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.BaseUrl), "FeatureManagement:BaseUrl is required when the sidecar is enabled.")
            .Validate(o => !o.Enabled || IsLoopbackOrHttps(o.BaseUrl), "FeatureManagement:BaseUrl must be an https URL, or http only to this machine (the App Config agent sidecar listens on localhost).")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Application), "FeatureManagement:Application is required when the sidecar is enabled.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.Environment), "FeatureManagement:Environment is required when the sidecar is enabled.")
            .Validate(o => !o.Enabled || !string.IsNullOrWhiteSpace(o.FeatureConfiguration), "FeatureManagement:FeatureConfiguration is required when the sidecar is enabled.")
            .Validate(o => !o.Enabled || !o.EnableDynamicAppSettings || !string.IsNullOrWhiteSpace(o.AppSettingsConfiguration), "FeatureManagement:AppSettingsConfiguration is required when dynamic appsettings are enabled.")
            .Validate(o => o.PollInterval > TimeSpan.Zero, "FeatureManagement:PollInterval must be positive.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero, "FeatureManagement:RequestTimeout must be positive.")
            .Validate(o => o.StartupMaxWait > TimeSpan.Zero, "FeatureManagement:StartupMaxWait must be positive.")
            .Validate(o => Enum.IsDefined(o.StartupFailureMode), "FeatureManagement:StartupFailureMode is invalid.")
            .ValidateOnStart();
    }

    /// <summary>
    /// The App Config agent runs beside the app in the same pod and answers plain HTTP on localhost, which never leaves the machine. Any other
    /// http address would send flag and settings traffic across a network unencrypted, so it is refused (AGENTS.md section 7).
    /// </summary>
    internal static bool IsLoopbackOrHttps(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
