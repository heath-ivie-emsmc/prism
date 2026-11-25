using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Tests.Configuration;

/// <summary>
/// Covers the two behaviours that only show up under load or failure: what a budget expiry reports, and that
/// the fan-out is actually bounded. Both matter most during an AWS incident, which is exactly when nobody is
/// in a position to work out what the loader was doing.
/// </summary>
public sealed class AwsSecretsLoaderBudgetAndConcurrencyTests
{
    [Fact]
    public async Task BudgetExpiry_ThrowsNamingTheSecretsThatHadNotLoaded()
    {
        AwsSecretsLoaderEntry fast = new()
        {
            SecretId = "nemsis/stage/fast",
            Format = AwsSecretFormat.Raw,
            TargetKey = "Demo:Fast",
        };
        AwsSecretsLoaderEntry stuck = new()
        {
            SecretId = "nemsis/stage/stuck",
            Format = AwsSecretFormat.Raw,
            TargetKey = "Demo:Stuck",
        };

        var reader = new BlockingSecretStringReader();
        reader.SetSecretString(fast.SecretId, "fast-value");
        reader.BlockUntilCancelled(stuck.SecretId);

        var options = new AwsSecretsLoaderOptions { Enabled = true, Secrets = [fast, stuck] };
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderException ex = await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
                configuration,
                options,
                reader,
                NullLogger.Instance,
                TestContext.Current.CancellationToken,
                TimeSpan.FromMilliseconds(250)));

        // The whole point: the message has to name the secret that hung, not just say "canceled".
        Assert.Contains(stuck.SecretId, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(fast.SecretId, ex.Message, StringComparison.Ordinal);

        // And nothing is merged: a host must not start half-configured.
        Assert.Null(configuration["Demo:Fast"]);
    }

    [Fact]
    public async Task FanOut_NeverExceedsFourConcurrentFetches()
    {
        var reader = new BlockingSecretStringReader();
        var entries = new List<AwsSecretsLoaderEntry>();
        for (var i = 0; i < 12; i++)
        {
            AwsSecretsLoaderEntry entry = new()
            {
                SecretId = $"nemsis/stage/secret-{i}",
                Format = AwsSecretFormat.Raw,
                TargetKey = $"Demo:Secret{i}",
            };
            entries.Add(entry);
            reader.SetSecretString(entry.SecretId, $"value-{i}", delay: TimeSpan.FromMilliseconds(25));
        }

        var options = new AwsSecretsLoaderOptions { Enabled = true, Secrets = entries };
        using var configuration = new ConfigurationManager();

        AwsSecretsLoaderResult result = await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.Equal(entries.Count, result.LoadedSecretIds.Count);
        Assert.True(
            reader.MaxObservedConcurrency <= 4,
            $"expected at most 4 concurrent fetches, observed {reader.MaxObservedConcurrency}");
        Assert.True(
            reader.MaxObservedConcurrency > 1,
            "expected the fetches to actually overlap, otherwise this test proves nothing");
    }

    [Fact]
    public void RealReader_WithoutRegion_ThrowsRatherThanResolvingAmbiently()
    {
        var options = new AwsSecretsLoaderOptions { Enabled = true, Region = null };
        var environment = new StubHostEnvironment("Production");

        AwsSecretsLoaderException ex = Assert.Throws<AwsSecretsLoaderException>(() =>
            new AwsSecretStringReader(options, environment));

        Assert.Contains("Region", ex.Message, StringComparison.Ordinal);
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Prism.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class BlockingSecretStringReader : IAwsSecretStringReader
    {
        private readonly Dictionary<string, (string? Value, TimeSpan Delay, bool Block)> _responses = [];

        private int _current;

        public int MaxObservedConcurrency { get; private set; }

        public void SetSecretString(string secretId, string? value, TimeSpan? delay = null) =>
            _responses[secretId] = (value, delay ?? TimeSpan.Zero, false);

        public void BlockUntilCancelled(string secretId) =>
            _responses[secretId] = (null, Timeout.InfiniteTimeSpan, true);

        public async Task<string?> GetSecretStringAsync(string secretId, CancellationToken cancellationToken)
        {
            var observed = Interlocked.Increment(ref _current);
            lock (_responses)
            {
                if (observed > MaxObservedConcurrency)
                    MaxObservedConcurrency = observed;
            }

            try
            {
                if (!_responses.TryGetValue(secretId, out (string? Value, TimeSpan Delay, bool Block) response))
                    throw new InvalidOperationException($"No response configured for '{secretId}'.");

                if (response.Block)
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                    return null;
                }

                if (response.Delay > TimeSpan.Zero)
                    await Task.Delay(response.Delay, cancellationToken).ConfigureAwait(false);

                return response.Value;
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }
    }
}
