using System.Runtime.CompilerServices;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Prism.Infrastructure.Configuration;

public static class AwsSecretsReloadServiceCollectionExtensions
{
    private static readonly ConditionalWeakTable<ConfigurationManager, AwsSecretsReloadState> ReloadStates = [];

    /// <summary>
    /// Registers hourly refresh for a loader invocation on this configuration manager. No service is registered
    /// when the loader is disabled or has no entries.
    /// </summary>
    public static IServiceCollection AddAwsSecretsLoaderReloading(
        this IServiceCollection services,
        ConfigurationManager configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!ReloadStates.TryGetValue(configuration, out AwsSecretsReloadState? state))
            return services;

        services.AddSingleton(state);
        services.AddHostedService<AwsSecretsReloadHostedService>();
        return services;
    }

    internal static void SetReloadState(ConfigurationManager configuration, AwsSecretsReloadState state)
    {
        ReloadStates.Remove(configuration);
        ReloadStates.Add(configuration, state);
    }
}
