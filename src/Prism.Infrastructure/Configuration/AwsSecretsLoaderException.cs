namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Thrown by the AWS Secrets Manager loader for configuration mistakes and unrecoverable fetch/parse failures.
/// The message names the secret id and/or configuration key involved; it never contains a secret value.
/// </summary>
public sealed class AwsSecretsLoaderException : Exception
{
    public AwsSecretsLoaderException(string message)
        : base(message)
    {
    }

    public AwsSecretsLoaderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
