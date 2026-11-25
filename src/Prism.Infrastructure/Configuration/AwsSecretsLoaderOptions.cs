namespace Prism.Infrastructure.Configuration;

/// <summary>Binds the <c>Aws:SecretsLoader</c> configuration section.</summary>
internal sealed class AwsSecretsLoaderOptions
{
    /// <summary>The configuration section the loader reads. Control keys are read before any secret is fetched.</summary>
    public const string SectionName = "Aws:SecretsLoader";

    public bool Enabled { get; set; }

    public string? Region { get; set; }

    /// <summary>Honoured only in Development/Testing; see <see cref="AwsSecretStringReader"/>.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Emulator access key, used only when <see cref="ServiceUrl"/> is honoured. Unset falls back to ambient AWS credentials.</summary>
    public string? AccessKey { get; set; }

    /// <summary>Emulator secret key, used only when <see cref="ServiceUrl"/> is honoured. Unset falls back to ambient AWS credentials.</summary>
    public string? SecretKey { get; set; }

    /// <summary>Refresh cadence after a successful startup load. Defaults to one hour.</summary>
    public int ReloadIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// When true, startup fetches every entry even if its keys are already configured
    /// (typically by a Helm-injected environment variable), so the AWS value wins from startup instead of from
    /// the first hourly refresh. Keys an entry should supply that still resolve from another source are logged
    /// as a warning. Read before the App Config sidecar merge: set it in appsettings or an environment variable,
    /// not in App Config freeform config.
    /// </summary>
    public bool OnlyUseAwsSecrets { get; set; }

    public List<AwsSecretsLoaderEntry> Secrets { get; set; } = [];
}
