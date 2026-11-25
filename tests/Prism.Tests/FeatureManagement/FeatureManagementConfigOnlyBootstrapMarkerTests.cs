using Microsoft.Extensions.Options;
using Prism.Infrastructure.FeatureManagement;
using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureManagementConfigOnlyBootstrapMarkerTests
{
    [Fact]
    public async Task StartAsync_WhenDisabled_MarksBootstrapComplete()
    {
        var bootstrapState = new FeatureManagementBootstrapState();
        var mockOptions = new TestOptionsMonitor<FeatureManagementOptions>(new FeatureManagementOptions { Enabled = false });

        var marker = new FeatureManagementConfigOnlyBootstrapMarker(bootstrapState, mockOptions);
        await marker.StartAsync(TestContext.Current.CancellationToken);

        Assert.True(bootstrapState.IsBootstrapComplete);
    }

    [Fact]
    public async Task StartAsync_WhenEnabled_DoesNotMarkBootstrapComplete()
    {
        var bootstrapState = new FeatureManagementBootstrapState();
        var mockOptions = new TestOptionsMonitor<FeatureManagementOptions>(new FeatureManagementOptions { Enabled = true });

        var marker = new FeatureManagementConfigOnlyBootstrapMarker(bootstrapState, mockOptions);
        await marker.StartAsync(TestContext.Current.CancellationToken);

        Assert.False(bootstrapState.IsBootstrapComplete);
    }

    [Fact]
    public async Task StopAsync_CompletesSuccessfully()
    {
        var bootstrapState = new FeatureManagementBootstrapState();
        var mockOptions = new TestOptionsMonitor<FeatureManagementOptions>(new FeatureManagementOptions());

        var marker = new FeatureManagementConfigOnlyBootstrapMarker(bootstrapState, mockOptions);
        Exception? ex = await Record.ExceptionAsync(async () => await marker.StopAsync(TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }
}
