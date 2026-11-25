using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Key safety for the AWS Secrets Manager loader: the global denylist, per-entry <c>AllowedKeys</c> filtering,
/// and entry-definition validation. Never logs a value.
/// </summary>
internal static class AwsSecretKeyPolicy
{
    /// <summary>
    /// A produced key equal to, or prefixed by (case-insensitive), any of these is refused. See the plan for why
    /// each one is here — in short, none of them may be set from a secret without changing the app's security or
    /// operational posture in a way that should be reviewed as code, not injected at runtime.
    /// </summary>
    internal static readonly IReadOnlyList<string> DeniedKeyPrefixes =
    [
        "environment",
        "ASPNETCORE_ENVIRONMENT",
        "DOTNET_ENVIRONMENT",
        "Aws:",
        "FeatureManagement:",
        "FeatureFlags:",
        "Serilog:",
        "ForwardedHeaders:",
        "Logging:",
        "AllowedHosts",
        "Internal:",
        "Warehouse:SkipLoginCheck",
    ];

    /// <summary>Static, fetch-independent validation of one entry's definition. Throws on a configuration mistake.</summary>
    internal static void ValidateEntryDefinition(AwsSecretsLoaderEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(entry.SecretId))
            throw new AwsSecretsLoaderException("Aws:SecretsLoader:Secrets contains an entry with no SecretId.");

        if (entry.Format == AwsSecretFormat.Raw && string.IsNullOrWhiteSpace(entry.TargetKey))
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' uses Format=Raw and requires TargetKey.");
        }

        var hasKeys = entry.Keys.Count > 0;
        var hasTargetKey = !string.IsNullOrWhiteSpace(AwsSecretPayloadFlattener.ResolveEffectiveTargetKey(entry));
        if (!hasKeys && !hasTargetKey && entry.AllowedKeys.Count == 0)
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{entry.SecretId}' has neither Keys nor TargetKey and must set AllowedKeys " +
                "(a bare KeyPrefix does not satisfy this requirement).");
        }
    }

    /// <summary>
    /// Applies <c>AllowedKeys</c> filtering (dropping and logging keys not listed, by name only, never by value)
    /// then the global denylist/<c>__</c> check (which throws) to the flattener's output, returning the final
    /// key/value pairs this entry is allowed to write.
    /// </summary>
    internal static IReadOnlyDictionary<string, string?> ApplyAndFilter(
        AwsSecretsLoaderEntry entry,
        IReadOnlyList<AwsSecretFlattenedProperty> properties,
        ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(properties);

        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (AwsSecretFlattenedProperty property in properties)
        {
            if (!IsAllowed(entry, property.ProducedKey))
            {
                SecretsLog.PropertyDropped(logger ?? NullLogger.Instance, property.SourceProperty, entry.SecretId, property.ProducedKey);
                continue;
            }

            ValidateProducedKey(entry.SecretId, property.ProducedKey);
            result[property.ProducedKey] = property.Value;
        }

        return result;
    }

    private static bool IsAllowed(AwsSecretsLoaderEntry entry, string producedKey) =>
        entry.AllowedKeys.Count == 0
        || entry.AllowedKeys.Any(allowed => string.Equals(allowed, producedKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>The denylisted prefix a configuration key falls under, or null. Used for every runtime source that may not set these keys.</summary>
    internal static string? DeniedPrefixFor(string key) =>
        DeniedKeyPrefixes.FirstOrDefault(candidate => key.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));

    /// <summary>Denylist check for a key that did not come from a secret payload (currently the break-glass channel).</summary>
    internal static void ThrowIfDeniedKey(string source, string producedKey) => ValidateProducedKey(source, producedKey);

    private static void ValidateProducedKey(string secretId, string producedKey)
    {
        if (producedKey.Contains("__", StringComparison.Ordinal))
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{secretId}' would produce configuration key '{producedKey}', which contains " +
                "'__'; use ':' separated segments instead.");
        }

        var denied = DeniedKeyPrefixes.FirstOrDefault(
            candidate => producedKey.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (denied is not null)
        {
            throw new AwsSecretsLoaderException(
                $"Secrets Manager entry '{secretId}' would produce configuration key '{producedKey}', which is not " +
                $"allowed to be set from a secret (denylisted: '{denied}').");
        }
    }
}
