using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;


using Microsoft.Extensions.Configuration;

using Prism.Application.FeatureManagement;
using Prism.Infrastructure.Configuration;

namespace Prism.Infrastructure.FeatureManagement;

public static class SidecarJsonParser
{
    /// <summary>Most flags and most freeform settings one profile may carry (AGENTS.md rule 4). More fails the load and the last good state stays.</summary>
    internal const int MaxEntries = 500;

    private const string EnabledPropertyName = "enabled";
    private const string VariantPropertyName = "variant";
    private const string UnderscoredVariantPropertyName = "_variant";

    public static IImmutableDictionary<string, FeatureValue> ParseFeatures(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ImmutableDictionary.Create<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        }

        if (root.TryGetProperty("values", out JsonElement valuesElement) && valuesElement.ValueKind == JsonValueKind.Object)
        {
            return ParseFeatureMap(valuesElement);
        }

        return ParseFeatureMap(root);
    }

    public static IReadOnlyDictionary<string, string?> ParseAppSettings(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Appsettings sidecar payload must be a JSON object.");
        }

        var results = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        VisitObject(results, root, parentPath: null);
        if (results.Count > MaxEntries)
        {
            throw new JsonException($"The appsettings profile has more than {MaxEntries} entries.");
        }

        // Same keys a secret may not set (environment name, loader and flag settings, logging, proxy trust, internal access, the warehouse override).
        // A runtime profile is not a place to change them: they are reviewed as code. The whole payload is refused, never partly applied.
        foreach (string key in results.Keys)
        {
            if (AwsSecretKeyPolicy.DeniedPrefixFor(key) is { } denied)
            {
                throw new SidecarSettingRefusedException(key, denied);
            }
        }

        return results;
    }

    private static ImmutableDictionary<string, FeatureValue> ParseFeatureMap(JsonElement featureMap)
    {
        ImmutableDictionary<string, FeatureValue>.Builder results = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty featureProperty in featureMap.EnumerateObject())
        {
            if (results.Count >= MaxEntries)
            {
                throw new JsonException($"The feature profile has more than {MaxEntries} entries.");
            }

            if (featureProperty.Value.ValueKind != JsonValueKind.Object)
                continue;

            results[featureProperty.Name] = ParseFeatureEntry(featureProperty.Name, featureProperty.Value);
        }

        return results.ToImmutable();
    }

    private static FeatureValue ParseFeatureEntry(string featureName, JsonElement featureObject)
    {
        string? variant = null;
        bool enabled = false;
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (JsonProperty property in featureObject.EnumerateObject())
        {
            if (TryApplyEnabledProperty(property, ref enabled)
                || TryApplyVariantProperty(property, ref variant))
                continue;

            settings[property.Name] = property.Value.Clone();
        }

        return new FeatureValue
        {
            FeatureName = featureName,
            Enabled = enabled,
            Variant = variant,
            Settings = settings
        };
    }

    private static bool TryApplyEnabledProperty(JsonProperty property, ref bool enabled)
    {
        if (!string.Equals(property.Name, EnabledPropertyName, StringComparison.OrdinalIgnoreCase))
            return false;

        enabled = (property.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            && property.Value.GetBoolean();
        return true;
    }

    private static bool TryApplyVariantProperty(JsonProperty property, ref string? variant)
    {
        if (!string.Equals(property.Name, UnderscoredVariantPropertyName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(property.Name, VariantPropertyName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (property.Value.ValueKind == JsonValueKind.String)
            variant = property.Value.GetString();

        return true;
    }

    private static void VisitObject(IDictionary<string, string?> data, JsonElement element, string? parentPath)
    {
        bool hasChildren = false;
        foreach (JsonProperty property in element.EnumerateObject())
        {
            hasChildren = true;

            if (property.Name.Contains(ConfigurationPath.KeyDelimiter, StringComparison.Ordinal))
            {
                throw new FormatException($"Invalid configuration key '{property.Name}'. Keys cannot contain '{ConfigurationPath.KeyDelimiter}'.");
            }

            VisitElement(data, property.Value, CombinePath(parentPath, property.Name));
        }

        if (!hasChildren && !string.IsNullOrEmpty(parentPath))
        {
            data[parentPath] = null;
        }
    }

    private static void VisitArray(IDictionary<string, string?> data, JsonElement element, string path)
    {
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            VisitElement(data, item, CombinePath(path, index.ToString(CultureInfo.InvariantCulture)));
            index++;
        }

        if (index == 0)
        {
            data[path] = null;
        }
    }

    private static void VisitElement(IDictionary<string, string?> data, JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                VisitObject(data, element, path);
                break;
            case JsonValueKind.Array:
                VisitArray(data, element, path);
                break;
            case JsonValueKind.String:
                data[path] = element.GetString();
                break;
            case JsonValueKind.Number:
                data[path] = element.GetRawText();
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                data[path] = element.GetBoolean().ToString();
                break;
            case JsonValueKind.Null:
                data[path] = null;
                break;
            default:
                throw new JsonException($"Unsupported JSON token kind '{element.ValueKind}' for key '{path}'.");
        }
    }

    private static string CombinePath(string? parentPath, string pathSegment) =>
        string.IsNullOrEmpty(parentPath)
            ? pathSegment
            : ConfigurationPath.Combine(parentPath, pathSegment);
}
