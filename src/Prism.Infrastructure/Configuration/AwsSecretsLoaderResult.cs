namespace Prism.Infrastructure.Configuration;

/// <summary>Outcome of <see cref="AwsSecretsLoaderExtensions.AddAwsSecretsLoaderAsync"/>. Pinned public shape — see the plan.</summary>
public sealed record AwsSecretsLoaderResult(
    IReadOnlySet<string> LoadedKeys,
    IReadOnlyList<string> LoadedSecretIds,
    IReadOnlyList<string> SkippedSecretIds)
{
    public static AwsSecretsLoaderResult Empty { get; } = new(
        new HashSet<string>(StringComparer.OrdinalIgnoreCase), [], []);
}
