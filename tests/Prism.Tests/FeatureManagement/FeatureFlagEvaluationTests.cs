using System.Text.Json;
using Prism.Application.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureFlagEvaluationTests
{
    private static FeatureValue MakeFeature(
        bool enabled,
        Dictionary<string, JsonElement>? settings = null) =>
        new()
        {
            FeatureName = "TestFeature",
            Enabled = enabled,
            Settings = settings ?? (IReadOnlyDictionary<string, JsonElement>)System.Collections.Immutable.ImmutableDictionary<string, JsonElement>.Empty,
        };

    private static JsonElement JsonArray(params long[] values)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return doc.RootElement.Clone();
    }

    private static JsonElement JsonString(string value)
    {
        using var doc = JsonDocument.Parse($"\"{value}\"");
        return doc.RootElement.Clone();
    }

    private static JsonElement JsonNumber(int value)
    {
        using var doc = JsonDocument.Parse($"{value}");
        return doc.RootElement.Clone();
    }

    [Fact]
    public void DisabledFeature_ReturnsFalse()
    {
        FeatureValue feature = MakeFeature(false);
        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }

    [Fact]
    public void EnabledFeature_NoSettings_ReturnsTrue()
    {
        FeatureValue feature = MakeFeature(true);
        Assert.True(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }

    [Fact]
    public void EnabledFeature_ProviderAllowlist_MatchingId_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = JsonArray(100, 200, 300),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 200 };

        Assert.True(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void EnabledFeature_ProviderAllowlist_NonMatchingId_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = JsonArray(100, 200),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 999 };

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void EnabledFeature_ProviderAllowlist_NoContextId_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = JsonArray(100),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }

    [Fact]
    public void EnabledFeature_UserAllowlist_MatchingId_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_allowlist"] = JsonArray(10, 20),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { UserId = 20 };

        Assert.True(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void EnabledFeature_UserAllowlist_NonMatchingId_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_allowlist"] = JsonArray(10, 20),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { UserId = 999 };

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void InvalidAllowlist_NotArray_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = JsonString("not-an-array"),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, new FeatureContext { ProviderId = 1 }));
    }

    [Fact]
    public void InvalidAllowlistItem_NotNumber_ReturnsFalse()
    {
        using var doc = JsonDocument.Parse("""["not-a-number"]""");
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["provider_allowlist"] = doc.RootElement.Clone(),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, new FeatureContext { ProviderId = 1 }));
    }

    [Fact]
    public void AndStrategy_BothAllowlistsMatch_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonString("and"),
            ["provider_allowlist"] = JsonArray(100),
            ["user_allowlist"] = JsonArray(50),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 100, UserId = 50 };

        Assert.True(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void AndStrategy_OnlyProviderMatches_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonString("and"),
            ["provider_allowlist"] = JsonArray(100),
            ["user_allowlist"] = JsonArray(50),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 100, UserId = 999 };

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void OrStrategy_OnlyUserMatches_ReturnsTrue()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonString("or"),
            ["provider_allowlist"] = JsonArray(100),
            ["user_allowlist"] = JsonArray(50),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 999, UserId = 50 };

        Assert.True(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void OrStrategy_NeitherMatches_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonString("or"),
            ["provider_allowlist"] = JsonArray(100),
            ["user_allowlist"] = JsonArray(50),
        };
        FeatureValue feature = MakeFeature(true, settings);
        var context = new FeatureContext { ProviderId = 1, UserId = 1 };

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, context));
    }

    [Fact]
    public void InvalidTargetingStrategy_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonString("invalid"),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }

    [Fact]
    public void TargetingStrategy_NotString_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["targeting_strategy"] = JsonNumber(42),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }

    [Fact]
    public void UserAllowlist_InvalidNotArray_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_allowlist"] = JsonNumber(42),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, new FeatureContext { UserId = 42 }));
    }

    [Fact]
    public void UserAllowlist_NoContextId_ReturnsFalse()
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["user_allowlist"] = JsonArray(10),
        };
        FeatureValue feature = MakeFeature(true, settings);

        Assert.False(FeatureFlagEvaluation.EvaluateEnabled(feature, FeatureContext.Empty));
    }
}
