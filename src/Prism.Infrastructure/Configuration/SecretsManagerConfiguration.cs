using System.Text.Json;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;

namespace Prism.Infrastructure.Configuration;

/// <summary>Reads the text of a secret by id. A seam so the loader can be tested without AWS.</summary>
public interface ISecretStore
{
    string GetSecretString(string secretId);
}

public sealed class AwsSecretStore(IAmazonSecretsManager client) : ISecretStore
{
    public string GetSecretString(string secretId)
    {
        GetSecretValueResponse response = client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId }).GetAwaiter().GetResult();
        return response.SecretString ?? throw new InvalidOperationException("The secret has no text value.");
    }
}

/// <summary>
/// Loads secrets from AWS Secrets Manager into configuration at startup. Each secret is a JSON object whose keys are configuration
/// keys (<c>Auth:SessionSigningKeyBase64</c>, or <c>Auth__SessionSigningKeyBase64</c>, or nested objects). Only keys under an allowed
/// prefix are accepted, so a secret cannot quietly change unrelated settings. Errors name the secret id and the problem and never
/// repeat any of the secret's content.
/// </summary>
public sealed class SecretsManagerConfigurationProvider(ISecretStore store, IReadOnlyList<string> secretIds) : ConfigurationProvider
{
    public const string SettingName = "Aws:SecretsManager:SecretIds";

    internal const int MaxSecrets = 10;

    internal const int MaxSecretLength = 64 * 1024;

    internal const int MaxKeys = 200;

    internal static readonly string[] AllowedPrefixes = ["Auth:", "ConnectionStrings:"];

    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (string secretId in secretIds.Take(MaxSecrets))
        {
            foreach ((string key, string value) in ReadSecret(secretId))
            {
                data[key] = value;
            }
        }

        Data = data;
    }

    private List<(string Key, string Value)> ReadSecret(string secretId)
    {
        string text;
        try
        {
            text = store.GetSecretString(secretId);
        }
        catch (Exception ex) when (ex is AmazonSecretsManagerException or AggregateException or InvalidOperationException or HttpRequestException or Amazon.Runtime.AmazonServiceException)
        {
            throw new InvalidOperationException($"Secret '{secretId}' could not be read ({ex.GetType().Name}).");
        }

        if (text.Length > MaxSecretLength)
        {
            throw new InvalidOperationException($"Secret '{secretId}' is larger than {MaxSecretLength} characters.");
        }

        var pairs = new List<(string Key, string Value)>();
        try
        {
            using JsonDocument json = JsonDocument.Parse(text);
            if (json.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException($"Secret '{secretId}' must be a JSON object of configuration keys.");
            }

            Flatten(json.RootElement, "", pairs);
        }
        catch (JsonException)
        {
            // The text is deliberately not echoed: it is the secret.
            throw new InvalidOperationException($"Secret '{secretId}' is not valid JSON.");
        }

        if (pairs.Count > MaxKeys)
        {
            throw new InvalidOperationException($"Secret '{secretId}' has more than {MaxKeys} keys.");
        }

        foreach ((string key, _) in pairs.Where(pair => !AllowedPrefixes.Any(prefix => pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException($"Secret '{secretId}' has a key outside the allowed sections ({string.Join(", ", AllowedPrefixes)}): '{Describe(key)}'.");
        }

        return pairs;
    }

    private static void Flatten(JsonElement element, string path, List<(string Key, string Value)> pairs)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string name = property.Name.Replace("__", ":", StringComparison.Ordinal);
                    Flatten(property.Value, path.Length == 0 ? name : $"{path}:{name}", pairs);
                }

                break;
            case JsonValueKind.String:
                pairs.Add((path, element.GetString() ?? ""));
                break;
            case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                pairs.Add((path, element.GetRawText()));
                break;
            default:
                break;
        }
    }

    // A key name is configuration, not secret, but it comes from outside: cap it before it goes into a message.
    private static string Describe(string key) => key.Length <= 60 ? key : key[..60] + "...";
}

public sealed class SecretsManagerConfigurationSource(ISecretStore store, IReadOnlyList<string> secretIds) : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => new SecretsManagerConfigurationProvider(store, secretIds);
}

public static class SecretsManagerConfigurationExtensions
{
    /// <summary>
    /// Adds the secrets named in <c>Aws:SecretsManager:SecretIds</c> (from the configuration built so far: settings files or environment).
    /// Nothing is added, and AWS is never contacted, when no ids are configured, as in local development.
    /// Added last so the secrets win over files.
    /// </summary>
    public static IConfigurationBuilder AddPrismSecretsManager(this IConfigurationBuilder builder, Func<ISecretStore>? storeFactory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        IConfigurationRoot current = builder.Build();
        string[] ids = current.GetSection(SecretsManagerConfigurationProvider.SettingName).Get<string[]>() ?? [];
        ids = [.. ids.Where(id => !string.IsNullOrWhiteSpace(id))];
        if (ids.Length == 0)
        {
            return builder;
        }

        ISecretStore store = storeFactory?.Invoke() ?? new AwsSecretStore(new AmazonSecretsManagerClient());
        return builder.Add(new SecretsManagerConfigurationSource(store, ids));
    }
}
