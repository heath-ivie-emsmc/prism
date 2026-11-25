using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Prism.Infrastructure.Configuration;

namespace Prism.Tests.Configuration;

/// <summary>AGENTS.md rule 4 for the secrets loader: what one secret, and the list of secrets, may carry.</summary>
public sealed class AwsSecretsLoaderBoundsTests
{
    private static AwsSecretsLoaderEntry Bundle(string id) => new() { SecretId = id, AllowedKeys = ["Auth:ApplicationId"] };

    private static async Task<AwsSecretsLoaderException> LoadExpectingFailureAsync(AwsSecretsLoaderOptions options, FakeAwsSecretStringReader reader)
    {
        using var configuration = new ConfigurationManager();
        return await Assert.ThrowsAsync<AwsSecretsLoaderException>(() => AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_secret_larger_than_the_cap_is_refused_without_repeating_its_content()
    {
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("big", "{\"Auth:ApplicationId\":\"SECRET-VALUE-123" + new string('x', AwsSecretPayloadFlattener.MaxSecretLength) + "\"}");

        AwsSecretsLoaderException ex = await LoadExpectingFailureAsync(new AwsSecretsLoaderOptions { Enabled = true, Secrets = [Bundle("big")] }, reader);

        Assert.Contains("'big'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SECRET-VALUE-123", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secret_with_too_many_keys_is_refused()
    {
        var reader = new FakeAwsSecretStringReader();
        string keys = string.Join(",", Enumerable.Range(0, AwsSecretPayloadFlattener.MaxKeysPerSecret + 1).Select(i => $"\"Auth:K{i}\":\"v\""));
        reader.SetSecretString("many", "{" + keys + "}");
        var entry = new AwsSecretsLoaderEntry { SecretId = "many", AllowedKeys = [.. Enumerable.Range(0, AwsSecretPayloadFlattener.MaxKeysPerSecret + 1).Select(i => $"Auth:K{i}")] };

        AwsSecretsLoaderException ex = await LoadExpectingFailureAsync(new AwsSecretsLoaderOptions { Enabled = true, Secrets = [entry] }, reader);

        Assert.Contains("more than", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task More_secrets_than_the_cap_fail_before_anything_is_fetched()
    {
        var reader = new FakeAwsSecretStringReader();
        List<AwsSecretsLoaderEntry> entries = [.. Enumerable.Range(0, AwsSecretsLoaderExtensions.MaxSecrets + 1).Select(i => Bundle($"s{i}"))];

        AwsSecretsLoaderException ex = await LoadExpectingFailureAsync(new AwsSecretsLoaderOptions { Enabled = true, Secrets = entries }, reader);

        Assert.Contains("more than", ex.Message, StringComparison.Ordinal);
        Assert.Empty(reader.Calls);
    }
}
