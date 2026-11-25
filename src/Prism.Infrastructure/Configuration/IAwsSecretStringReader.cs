namespace Prism.Infrastructure.Configuration;

/// <summary>Seam over <c>IAmazonSecretsManager.GetSecretValueAsync</c> so the loader is unit-testable without AWS calls.</summary>
internal interface IAwsSecretStringReader
{
    /// <summary>
    /// Returns the secret's <c>SecretString</c> (which may be null/empty if the current version has none), after
    /// the retry policy has been exhausted. Throws the underlying AWS exception on a non-transient or
    /// retry-exhausted failure; the caller classifies optional-skippable failures.
    /// </summary>
    Task<string?> GetSecretStringAsync(string secretId, CancellationToken cancellationToken);
}
