using System.Collections.Immutable;
using System.Text.Json;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class SidecarStateTests
{
    private static FeatureValue MakeFeature(string name, bool enabled) => new()
    {
        FeatureName = name,
        Enabled = enabled,
        Settings = new Dictionary<string, JsonElement>(),
    };

    [Fact]
    public void GetSnapshot_ReturnsEmptyByDefault()
    {
        var state = new SidecarState();
        SidecarState.Snapshot snapshot = state.GetSnapshot();

        Assert.Equal(DateTimeOffset.MinValue, snapshot.RetrievedAt);
        Assert.Empty(snapshot.Features);
    }

    [Fact]
    public void Replace_UpdatesSnapshot()
    {
        var state = new SidecarState();
        ImmutableDictionary<string, FeatureValue>.Builder features = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        features["Feature1"] = MakeFeature("Feature1", true);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        state.Replace(new SidecarState.Snapshot(now, features.ToImmutable()));

        SidecarState.Snapshot result = state.GetSnapshot();
        Assert.Equal(now, result.RetrievedAt);
        Assert.Single(result.Features);
    }

    [Fact]
    public void Replace_NullSnapshot_Throws()
    {
        var state = new SidecarState();
        Assert.Throws<ArgumentNullException>(() => state.Replace(null!));
    }

    [Fact]
    public void TryGetFeature_ExistingFeature_ReturnsTrue()
    {
        var state = new SidecarState();
        ImmutableDictionary<string, FeatureValue>.Builder features = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        features["TestFeature"] = MakeFeature("TestFeature", true);
        state.Replace(new SidecarState.Snapshot(DateTimeOffset.UtcNow, features.ToImmutable()));

        Assert.True(state.TryGetFeature("TestFeature", out FeatureValue? value));
        Assert.NotNull(value);
        Assert.True(value.Enabled);
    }

    [Fact]
    public void TryGetFeature_MissingFeature_ReturnsFalse()
    {
        var state = new SidecarState();

        Assert.False(state.TryGetFeature("Missing", out FeatureValue? value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetFeature_NullFeatureName_ReturnsFalse()
    {
        var state = new SidecarState();

        Assert.False(state.TryGetFeature(null!, out FeatureValue? value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGetFeature_EmptyFeatureName_ReturnsFalse()
    {
        var state = new SidecarState();

        Assert.False(state.TryGetFeature("", out _));
    }

    [Fact]
    public void TryGetFeature_WhitespaceFeatureName_ReturnsFalse()
    {
        var state = new SidecarState();

        Assert.False(state.TryGetFeature("   ", out _));
    }

    [Fact]
    public void TryGetFeature_CaseInsensitive()
    {
        var state = new SidecarState();
        ImmutableDictionary<string, FeatureValue>.Builder features = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        features["myFeature"] = MakeFeature("myFeature", true);
        state.Replace(new SidecarState.Snapshot(DateTimeOffset.UtcNow, features.ToImmutable()));

        Assert.True(state.TryGetFeature("MYFEATURE", out _));
    }

    [Fact]
    public void Snapshot_Empty_HasExpectedDefaults()
    {
        SidecarState.Snapshot empty = SidecarState.Snapshot.Empty;
        Assert.Equal(DateTimeOffset.MinValue, empty.RetrievedAt);
        Assert.Empty(empty.Features);
    }
}
