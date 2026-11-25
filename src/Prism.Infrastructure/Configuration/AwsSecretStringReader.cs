using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.Runtime.Credentials;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Hosting;

namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Real <see cref="IAwsSecretStringReader"/>: fetches via <see cref="AmazonSecretsManagerClient.GetSecretValueAsync"/>
/// under <see cref="AwsSecretsRetryPolicy"/> (4 attempts, throttling/5xx/timeout only).
/// </summary>
internal sealed class AwsSecretStringReader : IAwsSecretStringReader, IDisposable
{
    private static readonly HashSet<string> TransientErrorCodes =
    [
        "Throttling",
        "ThrottlingException",
        "TooManyRequestsException",
        "RequestLimitExceeded",
        "ServiceUnavailable",
        "ServiceUnavailableException",
        "InternalServiceError",
        "InternalServiceErrorException",
        "InternalFailure",
        "RequestTimeout",
        "RequestTimeoutException",
    ];

    private const string DefaultAuthenticationRegion = "us-east-1";

    private readonly AmazonSecretsManagerClient _client;
    private readonly TimeProvider _timeProvider;

    internal AwsSecretStringReader(AwsSecretsLoaderOptions options, IHostEnvironment environment, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        _timeProvider = timeProvider ?? TimeProvider.System;
        _client = CreateClient(options, environment);
    }

    public async Task<string?> GetSecretStringAsync(string secretId, CancellationToken cancellationToken)
    {
        GetSecretValueResponse response = await AwsSecretsRetryPolicy.ExecuteAsync(
                ct => _client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = secretId }, ct),
                IsTransient,
                _timeProvider,
                cancellationToken)
            .ConfigureAwait(false);

        return response.SecretString;
    }

    public void Dispose() => _client.Dispose();

    private static bool IsTransient(Exception ex) => ex is AmazonServiceException service
        && (TransientErrorCodes.Contains(service.ErrorCode) || IsTransientStatusCode(service.StatusCode));

    private static bool IsTransientStatusCode(HttpStatusCode status)
    {
        var code = (int)status;
        return code == 408 || code == 429 || code >= 500;
    }

    private static AmazonSecretsManagerClient CreateClient(AwsSecretsLoaderOptions options, IHostEnvironment environment)
    {
        var config = new AmazonSecretsManagerConfig();

        if (IsLocalServiceUrlHonoured(options, environment))
        {
            config.ServiceURL = options.ServiceUrl!.Trim();
            config.AuthenticationRegion = string.IsNullOrWhiteSpace(options.Region)
                ? DefaultAuthenticationRegion
                : options.Region.Trim();
            return new AmazonSecretsManagerClient(ResolveLocalCredentials(options), config);
        }

        if (string.IsNullOrWhiteSpace(options.Region))
        {
            // Ambient resolution (env var, shared profile, IMDS) would silently pick a region that is not the
            // one the secret ids belong to, and the resulting failure would look like a missing secret rather
            // than a misconfiguration. Every deployed environment file sets this.
            throw new AwsSecretsLoaderException(
                "Aws:SecretsLoader:Region is required when the loader is enabled (ambient AWS region resolution is not used).");
        }

        config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region.Trim());
        return new AmazonSecretsManagerClient(config);
    }

    /// <summary>
    /// Mirrors <c>AwsEmulatorClientConfiguration.ResolveCredentials</c>: take the emulator credentials from
    /// configuration, and fall back to the ambient resolver when they are not set. Deliberately not a literal
    /// pair — a hard-coded credential here is both a Sonar vulnerability and a bad habit to copy, even when
    /// the value only ever reaches an emulator.
    /// </summary>
    private static AWSCredentials ResolveLocalCredentials(AwsSecretsLoaderOptions options)
    {
        var accessKey = options.AccessKey?.Trim();
        var secretKey = options.SecretKey?.Trim();
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            return DefaultAWSCredentialsIdentityResolver.GetCredentials();

        return new BasicAWSCredentials(accessKey, secretKey);
    }

    /// <summary>Mirrors how <c>AwsEmulatorClientConfiguration</c> gates S3/SQS emulator endpoints: local-only, never in a deployed environment.</summary>
    private static bool IsLocalServiceUrlHonoured(AwsSecretsLoaderOptions options, IHostEnvironment environment) =>
        !string.IsNullOrWhiteSpace(options.ServiceUrl)
        && (environment.IsDevelopment()
            || string.Equals(environment.EnvironmentName, "Testing", StringComparison.OrdinalIgnoreCase));
}
