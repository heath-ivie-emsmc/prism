using System.Globalization;
using System.Text.Json;

namespace Prism.Infrastructure.Configuration;

/// <summary>One candidate configuration key produced from a secret payload, before <see cref="AwsSecretKeyPolicy"/> filtering.</summary>
/// <param name="SourceProperty">The JSON property (or <c>"SecretString"</c> for <see cref="AwsSecretFormat.Raw"/>/<see cref="AwsSecretFormat.RdsPostgres"/>) this came from. Never a value; used only for diagnostics.</param>
/// <param name="ProducedKey">The configuration key this would be written to.</param>
/// <param name="Value">The configuration value. Never logged directly.</param>
internal readonly record struct AwsSecretFlattenedProperty(string SourceProperty, string ProducedKey, string? Value);

/// <summary>
/// Pure mapping from a secret's <c>SecretString</c> + its <see cref="AwsSecretsLoaderEntry"/> to candidate
/// configuration keys. No AWS calls, no logging, no key-safety policy — see <see cref="AwsSecretKeyPolicy"/>
/// for the denylist/AllowedKeys filtering applied to the result.
/// </summary>
internal static class AwsSecretPayloadFlattener
{
    private const string RawSourcePropertyName = "SecretString";

    /// <summary>Bounds on what one secret may carry (AGENTS.md rule 4): the text is capped before it is parsed, and the keys it produces are counted.</summary>
    internal const int MaxSecretLength = 64 * 1024;

    internal const int MaxKeysPerSecret = 200;

    internal static IReadOnlyList<AwsSecretFlattenedProperty> Flatten(AwsSecretsLoaderEntry entry, string secretString)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrEmpty(secretString);

        if (secretString.Length > MaxSecretLength)
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' is larger than {MaxSecretLength} characters.");
        }

        IReadOnlyList<AwsSecretFlattenedProperty> properties = entry.Format switch
        {
            AwsSecretFormat.Raw => FlattenRaw(entry, secretString),
            AwsSecretFormat.RdsPostgres => FlattenRdsPostgres(entry, secretString),
            _ => FlattenJson(entry, secretString),
        };

        if (properties.Count > MaxKeysPerSecret)
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' has more than {MaxKeysPerSecret} keys.");
        }

        return properties;
    }

    private static IReadOnlyList<AwsSecretFlattenedProperty> FlattenRaw(AwsSecretsLoaderEntry entry, string secretString)
    {
        var targetKey = ResolveEffectiveTargetKey(entry);
        if (string.IsNullOrWhiteSpace(targetKey))
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' uses Format=Raw and requires TargetKey.");
        }

        return [new AwsSecretFlattenedProperty(RawSourcePropertyName, targetKey, secretString)];
    }

    private static IReadOnlyList<AwsSecretFlattenedProperty> FlattenRdsPostgres(AwsSecretsLoaderEntry entry, string secretString)
    {
        var targetKey = ResolveEffectiveTargetKey(entry)!;
        if (!PostgreSqlRdsSecretJson.TryParse(secretString, out var connectionString))
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' is not valid PostgreSQL RDS JSON (expected hostname, database, username, password).");
        }

        return [new AwsSecretFlattenedProperty(RawSourcePropertyName, targetKey, connectionString)];
    }

    /// <summary>The configuration key this entry's single-value formats (<c>Raw</c>/<c>RdsPostgres</c>) write to, including the <c>RdsPostgres</c> default.</summary>
    internal static string? ResolveEffectiveTargetKey(AwsSecretsLoaderEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.TargetKey))
            return entry.TargetKey.Trim();

        return entry.Format == AwsSecretFormat.RdsPostgres ? "ConnectionStrings:Writer" : null;
    }

    /// <summary>
    /// The configuration keys this entry is statically known to produce, without fetching the secret — used
    /// to decide whether a fetch can be skipped entirely because every candidate key is already set.
    /// </summary>
    internal static IReadOnlyList<string> ResolveCandidateKeys(AwsSecretsLoaderEntry entry)
    {
        if (entry.Keys.Count > 0)
            return [.. entry.Keys.Keys];

        var targetKey = ResolveEffectiveTargetKey(entry);
        if (!string.IsNullOrWhiteSpace(targetKey))
            return [targetKey];

        return entry.AllowedKeys;
    }

    private static List<AwsSecretFlattenedProperty> FlattenJson(AwsSecretsLoaderEntry entry, string secretString)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(secretString);
        }
        catch (JsonException ex)
        {
            throw new AwsSecretsLoaderException($"Secrets Manager entry '{entry.SecretId}' is not valid JSON.", ex);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new AwsSecretsLoaderException($"Secrets Manager entry '{entry.SecretId}' must contain a JSON object.");

            return entry.Keys.Count > 0
                ? FlattenWithKeysMapping(entry, document.RootElement)
                : FlattenWholeJson(entry, document.RootElement);
        }
    }

    private static List<AwsSecretFlattenedProperty> FlattenWithKeysMapping(AwsSecretsLoaderEntry entry, JsonElement root)
    {
        var results = new List<AwsSecretFlattenedProperty>(entry.Keys.Count);
        foreach ((var configKey, var propertyName) in entry.Keys)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement propertyElement))
            {
                throw new AwsSecretsLoaderException(
                    $"Secrets Manager entry '{entry.SecretId}' is missing expected property '{propertyName}' for configuration key '{configKey}'.");
            }

            var value = ExtractScalarValue(entry.SecretId, propertyName, propertyElement);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new AwsSecretsLoaderException(
                    $"Secrets Manager entry '{entry.SecretId}' has an empty required property '{propertyName}' for configuration key '{configKey}'.");
            }

            results.Add(new AwsSecretFlattenedProperty(propertyName, configKey, value));
        }

        return results;
    }

    private static List<AwsSecretFlattenedProperty> FlattenWholeJson(AwsSecretsLoaderEntry entry, JsonElement root)
    {
        var raw = new List<AwsSecretFlattenedProperty>();
        FlattenRecursive(entry.SecretId, root, path: null, raw);

        var prefix = string.IsNullOrWhiteSpace(entry.KeyPrefix) ? null : entry.KeyPrefix.Trim();
        if (prefix is null)
            return raw;

        return [.. raw.Select(p => p with { ProducedKey = $"{prefix}:{p.ProducedKey}" })];
    }

    private static void FlattenRecursive(string secretId, JsonElement element, string? path, List<AwsSecretFlattenedProperty> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    var childPath = path is null ? property.Name : $"{path}:{property.Name}";
                    FlattenRecursive(secretId, property.Value, childPath, results);
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    var childPath = path is null ? index.ToString(CultureInfo.InvariantCulture) : $"{path}:{index}";
                    FlattenRecursive(secretId, item, childPath, results);
                    index++;
                }

                break;

            default:
                if (path is not null)
                {
                    var value = ExtractScalarValue(secretId, path, element);
                    results.Add(new AwsSecretFlattenedProperty(path, path, value));
                }

                break;
        }
    }

    /// <summary>Mirrors <c>JsonConfigurationFileParser</c>'s leaf-value conversion so <c>Json</c> format matches <c>AddJsonStream</c> exactly.</summary>
    private static string? ExtractScalarValue(string secretId, string path, JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.GetRawText(),
        // "True"/"False", not "true"/"false": JsonConfigurationFileParser stores JsonElement.ToString()
        // for these kinds, and Json format is specified to match AddJsonStream exactly.
        JsonValueKind.True => bool.TrueString,
        JsonValueKind.False => bool.FalseString,
        JsonValueKind.Null => null,
        _ => throw new AwsSecretsLoaderException(
            $"Secrets Manager entry '{secretId}' has unsupported JSON value kind '{element.ValueKind}' at '{path}'."),
    };
}
