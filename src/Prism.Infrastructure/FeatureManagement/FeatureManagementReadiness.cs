using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>Whether feature flags can be trusted right now. Part of what <c>/ready</c> reports, so a pod with no flags is not sent traffic.</summary>
public interface IFeatureManagementReadiness
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Ready when the App Config sidecar is switched off (flags then come from settings files), or when its first load finished, the agent still answers on its
/// port, and the flag profile is not empty (unless the options allow an empty one). Reasons are fixed words in the log, never data from the sidecar.
/// </summary>
public sealed partial class FeatureManagementReadiness(
    IOptionsMonitor<FeatureManagementOptions> options,
    FeatureManagementBootstrapState bootstrapState,
    ILogger<FeatureManagementReadiness> logger,
    IAppConfigSidecarConnectivityProbe? connectivityProbe = null,
    SidecarState? state = null) : IFeatureManagementReadiness
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        FeatureManagementOptions current = options.CurrentValue;
        if (!current.Enabled)
        {
            return true;
        }

        if (!bootstrapState.IsBootstrapComplete)
        {
            LogNotReady(logger, "bootstrap-incomplete");
            return false;
        }

        if (connectivityProbe is not null && !await IsReachableAsync(connectivityProbe, current, cancellationToken))
        {
            LogNotReady(logger, "sidecar-unreachable");
            return false;
        }

        if (current.RequireNonEmptyFeatureConfig && state is not null && state.GetSnapshot().Features.Count == 0)
        {
            LogNotReady(logger, "feature-profile-empty");
            return false;
        }

        return true;
    }

    private static async Task<bool> IsReachableAsync(IAppConfigSidecarConnectivityProbe probe, FeatureManagementOptions current, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(current.RequestTimeout);
        return await probe.IsReachableAsync(current, timeout.Token);
    }

    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning, Message = "Feature management is not ready ({Reason}).")]
    private static partial void LogNotReady(ILogger logger, string reason);
}
