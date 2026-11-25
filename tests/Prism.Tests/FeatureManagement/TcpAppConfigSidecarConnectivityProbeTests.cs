using Prism.Infrastructure.FeatureManagement;
using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class TcpAppConfigSidecarConnectivityProbeTests
{
    [Fact]
    public async Task IsReachableAsync_InvalidBaseUrl_ReturnsFalse()
    {
        var probe = new TcpAppConfigSidecarConnectivityProbe();
        var options = new FeatureManagementOptions { BaseUrl = "not-a-uri" };

        bool reachable = await probe.IsReachableAsync(options, TestContext.Current.CancellationToken);

        Assert.False(reachable);
    }

    [Fact]
    public async Task IsReachableAsync_ClosedPort_ReturnsFalse()
    {
        var probe = new TcpAppConfigSidecarConnectivityProbe();
        var options = new FeatureManagementOptions { BaseUrl = "http://127.0.0.1:1" };

        bool reachable = await probe.IsReachableAsync(options, TestContext.Current.CancellationToken);

        Assert.False(reachable);
    }
}
