using Microsoft.Extensions.Configuration;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class ConfigurationFeatureValueManagerTests
{
    private static ConfigurationFeatureValueManager CreateManager(Dictionary<string, string?> data)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();
        return new ConfigurationFeatureValueManager(configuration, TimeProvider.System);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsAllFeatureFlags()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:WorkerProcessingEnabled"] = "true",
            ["FeatureFlags:SubmitDataEnabled"] = "false",
        });

        FeatureSnapshot snapshot = await manager.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Features.Count);
        Assert.True(snapshot.Features["WorkerProcessingEnabled"].Enabled);
        Assert.False(snapshot.Features["SubmitDataEnabled"].Enabled);
    }

    [Fact]
    public async Task GetSnapshotAsync_EmptyConfiguration_ReturnsEmptyFeatures()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        FeatureSnapshot snapshot = await manager.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Empty(snapshot.Features);
    }

    [Fact]
    public async Task GetSnapshotAsync_CancellationRequested_Throws()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.GetSnapshotAsync(cts.Token));
    }

    [Fact]
    public async Task GetFeatureValueAsync_ExistingFeature_ReturnsValue()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:MyFeature"] = "true",
        });

        FeatureValue? result = await manager.GetFeatureValueAsync("MyFeature", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Enabled);
        Assert.Equal("MyFeature", result.FeatureName);
    }

    [Fact]
    public async Task GetFeatureValueAsync_MissingFeature_ReturnsNull()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        FeatureValue? result = await manager.GetFeatureValueAsync("NonExistent", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFeatureValueAsync_WithContext_ReturnsValue()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:MyFeature"] = "true",
        });

        FeatureValue? result = await manager.GetFeatureValueAsync(
            "MyFeature", new FeatureContext { ProviderId = 1 }, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Enabled);
    }

    [Fact]
    public async Task GetFeatureValueAsync_NullFeatureName_Throws()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => manager.GetFeatureValueAsync(null!, FeatureContext.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetFeatureValueAsync_WhitespaceFeatureName_Throws()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        await Assert.ThrowsAsync<ArgumentException>(
            () => manager.GetFeatureValueAsync("   ", FeatureContext.Empty, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetFeatureValueAsync_NullContext_Throws()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:MyFeature"] = "true",
        });

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => manager.GetFeatureValueAsync("MyFeature", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetFeatureValueAsync_CancellationRequested_Throws()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.GetFeatureValueAsync("Anything", FeatureContext.Empty, cts.Token));
    }

    [Fact]
    public async Task IsEnabledAsync_EnabledFeature_ReturnsTrue()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:TestFlag"] = "true",
        });

        Assert.True(await manager.IsEnabledAsync("TestFlag", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_DisabledFeature_ReturnsFalse()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:TestFlag"] = "false",
        });

        Assert.False(await manager.IsEnabledAsync("TestFlag", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_MissingFeature_ReturnsFalse()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        Assert.False(await manager.IsEnabledAsync("Missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_ReturnsExpected()
    {
        ConfigurationFeatureValueManager manager = CreateManager(new Dictionary<string, string?>
        {
            ["FeatureFlags:TestFlag"] = "true",
        });

        Assert.True(await manager.IsEnabledAsync(
            "TestFlag", new FeatureContext { ProviderId = 1 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_MissingFeature_ReturnsFalse()
    {
        ConfigurationFeatureValueManager manager = CreateManager([]);

        Assert.False(await manager.IsEnabledAsync(
            "Missing", new FeatureContext { ProviderId = 1 }, TestContext.Current.CancellationToken));
    }
}
