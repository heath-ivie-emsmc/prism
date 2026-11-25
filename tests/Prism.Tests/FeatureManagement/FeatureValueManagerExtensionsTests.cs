using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureValueManagerExtensionsTests
{
    private sealed class RecordingManager(bool answer) : IFeatureValueManager
    {
        public List<(string Feature, FeatureContext Context)> Asked { get; } = [];

        public Task<FeatureSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureValue?> GetFeatureValueAsync(string featureName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<FeatureValue?> GetFeatureValueAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsEnabledAsync(string featureName, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> IsEnabledAsync(string featureName, FeatureContext context, CancellationToken cancellationToken = default)
        {
            Asked.Add((featureName, context));
            return Task.FromResult(answer);
        }
    }

    [Fact]
    public async Task IsEnabledForProviderAsync_AsksWithTheProviderOnly()
    {
        var manager = new RecordingManager(true);

        bool result = await manager.IsEnabledForProviderAsync("MyFeature", 42, TestContext.Current.CancellationToken);

        Assert.True(result);
        (string feature, FeatureContext context) = Assert.Single(manager.Asked);
        Assert.Equal("MyFeature", feature);
        Assert.Equal(42, context.ProviderId);
        Assert.Null(context.UserId);
    }

    [Fact]
    public async Task IsEnabledForUserAsync_AsksWithTheUserOnly()
    {
        var manager = new RecordingManager(true);

        bool result = await manager.IsEnabledForUserAsync("UserFeature", 99, TestContext.Current.CancellationToken);

        Assert.True(result);
        FeatureContext context = Assert.Single(manager.Asked).Context;
        Assert.Equal(99, context.UserId);
        Assert.Null(context.ProviderId);
    }

    [Fact]
    public async Task IsEnabledAsync_WithProviderAndUser_AsksWithBoth()
    {
        var manager = new RecordingManager(false);

        bool result = await manager.IsEnabledAsync("CombinedFeature", 10, 20, TestContext.Current.CancellationToken);

        Assert.False(result);
        FeatureContext context = Assert.Single(manager.Asked).Context;
        Assert.Equal(10, context.ProviderId);
        Assert.Equal(20, context.UserId);
    }

    [Fact]
    public async Task Every_extension_rejects_a_null_manager()
    {
        IFeatureValueManager manager = null!;
        CancellationToken ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.IsEnabledForProviderAsync("Feature", 1, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.IsEnabledForUserAsync("Feature", 1, ct));
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.IsEnabledAsync("Feature", 1, 2, ct));
    }
}
