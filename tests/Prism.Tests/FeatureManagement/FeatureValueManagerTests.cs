using System.Collections.Immutable;
using System.Text.Json;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureValueManagerTests
{
    private static (FeatureValueManager Manager, SidecarState State) CreateManager()
    {
        var state = new SidecarState();
        var manager = new FeatureValueManager(state);
        return (manager, state);
    }

    private static FeatureValue MakeFeature(string name, bool enabled) => new()
    {
        FeatureName = name,
        Enabled = enabled,
        Settings = new Dictionary<string, JsonElement>(),
    };

    private static void LoadFeatures(SidecarState state, params (string Name, bool Enabled)[] features)
    {
        ImmutableDictionary<string, FeatureValue>.Builder builder = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        foreach ((string? name, bool enabled) in features)
            builder[name] = MakeFeature(name, enabled);
        state.Replace(new SidecarState.Snapshot(DateTimeOffset.UtcNow, builder.ToImmutable()));
    }

    [Fact]
    public async Task GetSnapshotAsync_ReturnsCurrentSnapshot()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("F1", true), ("F2", false));

        FeatureSnapshot snapshot = await manager.GetSnapshotAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, snapshot.Features.Count);
        Assert.True(snapshot.Features["F1"].Enabled);
        Assert.False(snapshot.Features["F2"].Enabled);
    }

    [Fact]
    public async Task GetSnapshotAsync_CancellationRequested_Throws()
    {
        (FeatureValueManager? manager, SidecarState _) = CreateManager();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.GetSnapshotAsync(cts.Token));
    }

    [Fact]
    public async Task GetFeatureValueAsync_ExistingFeature_ReturnsValue()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("TestFeature", true));

        FeatureValue? result = await manager.GetFeatureValueAsync("TestFeature", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.Enabled);
    }

    [Fact]
    public async Task GetFeatureValueAsync_MissingFeature_ReturnsNull()
    {
        (FeatureValueManager? manager, SidecarState _) = CreateManager();

        FeatureValue? result = await manager.GetFeatureValueAsync("NonExistent", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetFeatureValueAsync_WithContext_EvaluatesTargeting()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();

        using var arrayDoc = JsonDocument.Parse("[100]");
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = arrayDoc.RootElement.Clone(),
        };

        ImmutableDictionary<string, FeatureValue>.Builder builder = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        builder["Targeted"] = new FeatureValue
        {
            FeatureName = "Targeted",
            Enabled = true,
            Settings = settings,
        };
        state.Replace(new SidecarState.Snapshot(DateTimeOffset.UtcNow, builder.ToImmutable()));

        FeatureValue? matched = await manager.GetFeatureValueAsync(
            "Targeted", new FeatureContext { ProviderId = 100 }, TestContext.Current.CancellationToken);
        Assert.NotNull(matched);
        Assert.True(matched.Enabled);

        FeatureValue? notMatched = await manager.GetFeatureValueAsync(
            "Targeted", new FeatureContext { ProviderId = 999 }, TestContext.Current.CancellationToken);
        Assert.NotNull(notMatched);
        Assert.False(notMatched.Enabled);
    }

    [Fact]
    public async Task GetFeatureValueAsync_NullContext_Throws()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("F", true));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => manager.GetFeatureValueAsync("F", null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetFeatureValueAsync_CancellationRequested_Throws()
    {
        (FeatureValueManager? manager, SidecarState _) = CreateManager();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => manager.GetFeatureValueAsync("Any", FeatureContext.Empty, cts.Token));
    }

    [Fact]
    public async Task IsEnabledAsync_EnabledFeature_ReturnsTrue()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("Active", true));

        Assert.True(await manager.IsEnabledAsync("Active", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_DisabledFeature_ReturnsFalse()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("Inactive", false));

        Assert.False(await manager.IsEnabledAsync("Inactive", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_MissingFeature_ReturnsFalse()
    {
        (FeatureValueManager? manager, SidecarState _) = CreateManager();

        Assert.False(await manager.IsEnabledAsync("Missing", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_EvaluatesTargeting()
    {
        (FeatureValueManager? manager, SidecarState? state) = CreateManager();
        LoadFeatures(state, ("Flag", true));

        Assert.True(await manager.IsEnabledAsync(
            "Flag", new FeatureContext { ProviderId = 1 }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IsEnabledAsync_WithContext_MissingFeature_ReturnsFalse()
    {
        (FeatureValueManager? manager, SidecarState _) = CreateManager();

        Assert.False(await manager.IsEnabledAsync(
            "Missing", new FeatureContext { ProviderId = 1 }, TestContext.Current.CancellationToken));
    }
}
