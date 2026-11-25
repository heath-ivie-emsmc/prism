using System.Collections.Immutable;
using System.Text.Json;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class SidecarJsonParserTests
{
    [Fact]
    public void ParseFeatures_IgnoresFlatBooleanFeatureValues()
    {
        using var doc = JsonDocument.Parse("""{"SubmitDataEnabled":true,"Other":{"enabled":true}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.False(map.ContainsKey("SubmitDataEnabled"));
        Assert.True(map.ContainsKey("Other"));
        Assert.True(map["Other"].Enabled);
    }

    [Fact]
    public void ParseFeatures_ReadsValuesNestedObject()
    {
        using var doc = JsonDocument.Parse(
            """{"values":{"WorkerProcessingEnabled":{"enabled":false},"SubmitDataEnabled":{"enabled":true}}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.False(map["WorkerProcessingEnabled"].Enabled);
        Assert.True(map["SubmitDataEnabled"].Enabled);
    }

    [Fact]
    public void ParseFeatures_NonObject_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("42");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.Empty(map);
    }

    [Fact]
    public void ParseFeatures_EmptyObject_ReturnsEmpty()
    {
        using var doc = JsonDocument.Parse("{}");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.Empty(map);
    }

    [Fact]
    public void ParseFeatures_ReadsVariant()
    {
        using var doc = JsonDocument.Parse(
            """{"Feature1":{"enabled":true,"_variant":"premium"}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.Equal("premium", map["Feature1"].Variant);
    }

    [Fact]
    public void ParseFeatures_ReadsVariantWithoutUnderscore()
    {
        using var doc = JsonDocument.Parse(
            """{"Feature1":{"enabled":true,"variant":"basic"}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.Equal("basic", map["Feature1"].Variant);
    }

    [Fact]
    public void ParseFeatures_VariantNonString_IgnoresVariant()
    {
        using var doc = JsonDocument.Parse(
            """{"Feature1":{"enabled":true,"_variant":42}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.Null(map["Feature1"].Variant);
    }

    [Fact]
    public void ParseFeatures_EnabledNotBoolean_DefaultsFalse()
    {
        using var doc = JsonDocument.Parse(
            """{"Feature1":{"enabled":"yes"}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.False(map["Feature1"].Enabled);
    }

    [Fact]
    public void ParseFeatures_ExtraSettings_CapturedInSettings()
    {
        using var doc = JsonDocument.Parse(
            """{"Feature1":{"enabled":true,"provider_allowlist":[1,2,3]}}""");
        IImmutableDictionary<string, FeatureValue> map = SidecarJsonParser.ParseFeatures(doc.RootElement);
        Assert.True(map["Feature1"].Settings.ContainsKey("provider_allowlist"));
    }

    [Fact]
    public void ParseAppSettings_FlatObject_ReturnsKeyValues()
    {
        using var doc = JsonDocument.Parse("""{"Key1":"Value1","Key2":"Value2"}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("Value1", result["Key1"]);
        Assert.Equal("Value2", result["Key2"]);
    }

    [Fact]
    public void ParseAppSettings_NestedObject_FlattensToPaths()
    {
        using var doc = JsonDocument.Parse("""{"Parent":{"Child":"Value"}}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("Value", result["Parent:Child"]);
    }

    [Fact]
    public void ParseAppSettings_NonObject_Throws()
    {
        using var doc = JsonDocument.Parse("42");
        Assert.Throws<JsonException>(() => SidecarJsonParser.ParseAppSettings(doc.RootElement));
    }

    [Fact]
    public void ParseAppSettings_ArrayValues_FlattensWithIndex()
    {
        using var doc = JsonDocument.Parse("""{"Items":["a","b","c"]}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("a", result["Items:0"]);
        Assert.Equal("b", result["Items:1"]);
        Assert.Equal("c", result["Items:2"]);
    }

    [Fact]
    public void ParseAppSettings_NullValue_SetsNull()
    {
        using var doc = JsonDocument.Parse("""{"Key":null}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.True(result.ContainsKey("Key"));
        Assert.Null(result["Key"]);
    }

    [Fact]
    public void ParseAppSettings_BooleanValue_StoredAsString()
    {
        using var doc = JsonDocument.Parse("""{"Flag":true}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("True", result["Flag"]);
    }

    [Fact]
    public void ParseAppSettings_NumberValue_StoredAsRawText()
    {
        using var doc = JsonDocument.Parse("""{"Count":42}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("42", result["Count"]);
    }

    [Fact]
    public void ParseAppSettings_KeyContainsColon_Throws()
    {
        using var doc = JsonDocument.Parse("""{"Bad:Key":"value"}""");
        Assert.Throws<FormatException>(() => SidecarJsonParser.ParseAppSettings(doc.RootElement));
    }

    [Fact]
    public void ParseAppSettings_EmptyObject_SetsNullForPath()
    {
        using var doc = JsonDocument.Parse("""{"Parent":{}}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.True(result.ContainsKey("Parent"));
        Assert.Null(result["Parent"]);
    }

    [Fact]
    public void ParseAppSettings_EmptyArray_SetsNullForPath()
    {
        using var doc = JsonDocument.Parse("""{"Items":[]}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.True(result.ContainsKey("Items"));
        Assert.Null(result["Items"]);
    }

    [Fact]
    public void ParseAppSettings_NestedArray_FlattensCorrectly()
    {
        using var doc = JsonDocument.Parse("""{"Outer":{"Inner":[10,20]}}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("10", result["Outer:Inner:0"]);
        Assert.Equal("20", result["Outer:Inner:1"]);
    }

    [Fact]
    public void ParseAppSettings_ArrayOfObjects_FlattensCorrectly()
    {
        using var doc = JsonDocument.Parse("""{"Items":[{"Name":"A"},{"Name":"B"}]}""");
        IReadOnlyDictionary<string, string?> result = SidecarJsonParser.ParseAppSettings(doc.RootElement);
        Assert.Equal("A", result["Items:0:Name"]);
        Assert.Equal("B", result["Items:1:Name"]);
    }
}
