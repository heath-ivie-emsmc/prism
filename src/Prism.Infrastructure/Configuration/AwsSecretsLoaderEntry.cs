namespace Prism.Infrastructure.Configuration;

/// <summary>How a fetched secret's <c>SecretString</c> maps into configuration keys.</summary>
internal enum AwsSecretFormat
{
    /// <summary>Full recursive flattening into <c>A:B:C</c> / <c>A:0:B</c>, semantically identical to <c>AddJsonStream</c>.</summary>
    Json = 0,

    /// <summary>The whole <c>SecretString</c> becomes the value at <see cref="AwsSecretsLoaderEntry.TargetKey"/> (required).</summary>
    Raw,

    /// <summary><see cref="PostgreSqlRdsSecretJson"/> builds an Npgsql connection string at <see cref="AwsSecretsLoaderEntry.TargetKey"/> (default <c>ConnectionStrings:Writer</c>).</summary>
    RdsPostgres,
}

/// <summary>One <c>Aws:SecretsLoader:Secrets[]</c> entry.</summary>
internal sealed class AwsSecretsLoaderEntry
{
    public string SecretId { get; set; } = string.Empty;

    /// <summary>When true, <see cref="Amazon.SecretsManager.Model.ResourceNotFoundException"/> and access-denied fetch failures are skipped with a warning instead of failing startup.</summary>
    public bool Optional { get; set; }

    public AwsSecretFormat Format { get; set; } = AwsSecretFormat.Json;

    /// <summary>Prepended (as <c>"{KeyPrefix}:"</c>) to every flattened key when <see cref="Format"/> is <see cref="AwsSecretFormat.Json"/> and <see cref="Keys"/> is empty.</summary>
    public string? KeyPrefix { get; set; }

    /// <summary>Required for <see cref="AwsSecretFormat.Raw"/>; defaults to <c>ConnectionStrings:Writer</c> for <see cref="AwsSecretFormat.RdsPostgres"/>.</summary>
    public string? TargetKey { get; set; }

    /// <summary>
    /// Configuration key -&gt; JSON property name. Reversed from what you might expect: one property can fan
    /// out to several configuration keys (for example one signing key landing at two).
    /// When non-empty this wins over <see cref="KeyPrefix"/> / whole-JSON flattening.
    /// </summary>
    public Dictionary<string, string> Keys { get; set; } = [];

    /// <summary>
    /// Produced configuration keys this entry may set, evaluated after <see cref="Keys"/>/<see cref="KeyPrefix"/>
    /// mapping. Required when the entry sets neither <see cref="Keys"/> nor <see cref="TargetKey"/> (a bare
    /// <see cref="KeyPrefix"/> does not satisfy this). A produced key not in this list is dropped with a warning.
    /// </summary>
    public List<string> AllowedKeys { get; set; } = [];
}
