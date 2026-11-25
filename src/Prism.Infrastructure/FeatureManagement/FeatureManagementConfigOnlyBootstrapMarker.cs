using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>Marks bootstrap complete when the App Config sidecar integration is disabled (no sidecar hosted service runs).</summary>
public sealed class FeatureManagementConfigOnlyBootstrapMarker : IHostedService
{
    private readonly FeatureManagementBootstrapState _bootstrapState;
    private readonly IOptionsMonitor<FeatureManagementOptions> _options;

    public FeatureManagementConfigOnlyBootstrapMarker(
        FeatureManagementBootstrapState bootstrapState,
        IOptionsMonitor<FeatureManagementOptions> options)
    {
        _bootstrapState = bootstrapState;
        _options = options;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            _bootstrapState.MarkBootstrapComplete();
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
