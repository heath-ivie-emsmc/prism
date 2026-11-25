using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prism.Infrastructure.Configuration;

internal sealed class AwsSecretsReloadState : IDisposable
{
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly AwsSecretsConfigurationProvider _provider;
    private IReadOnlyDictionary<string, string?>[] _lastKnownEntryValues;

    // True while an entry still carries the pre-existing configuration value it kept at startup (skip-if-set).
    // Reload deliberately fetches those entries and publishes AWS above the old environment layer; the first
    // refresh that changes such a value is logged once so the precedence flip is visible, not silent.
    private readonly bool[] _keepsStartupValue;

    internal AwsSecretsReloadState(
        AwsSecretsLoaderOptions options,
        IHostEnvironment environment,
        IReadOnlyList<AwsSecretsLoaderEntry> entries,
        AwsSecretsConfigurationProvider provider,
        IReadOnlyDictionary<string, string?>[] initialEntryValues,
        bool[]? keepsStartupValue = null)
    {
        Options = options;
        Environment = environment;
        Entries = entries;
        _provider = provider;
        _lastKnownEntryValues = initialEntryValues;
        _keepsStartupValue = keepsStartupValue ?? new bool[entries.Count];
    }

    internal AwsSecretsLoaderOptions Options { get; }

    internal IHostEnvironment Environment { get; }

    internal IReadOnlyList<AwsSecretsLoaderEntry> Entries { get; }

    internal async Task<bool> ReloadAsync(
        IAwsSecretStringReader reader,
        ILogger logger,
        CancellationToken cancellationToken,
        TimeSpan? overallBudget = null)
    {
        await _reloadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AwsSecretsLoaderExtensions.EntryOutcome[] outcomes = await AwsSecretsLoaderExtensions.FetchAllAsync(
                [.. Entries], reader, logger, overallBudget ?? AwsSecretsLoaderExtensions.DefaultOverallBudget, cancellationToken)
                .ConfigureAwait(false);

            var nextEntryValues = new IReadOnlyDictionary<string, string?>[outcomes.Length];
            var supersededStartupValues = new List<int>();
            for (var index = 0; index < outcomes.Length; index++)
            {
                nextEntryValues[index] = outcomes[index].Skipped
                    ? _lastKnownEntryValues[index]
                    : outcomes[index].Values;
                if (_keepsStartupValue[index]
                    && !outcomes[index].Skipped
                    && !ConfigurationValuesEqual(_lastKnownEntryValues[index], outcomes[index].Values))
                {
                    supersededStartupValues.Add(index);
                }
            }

            IReadOnlyDictionary<string, string?> merged = MergeInDeclarationOrder(nextEntryValues);
            IReadOnlyDictionary<string, string?> current = _provider.Values;
            cancellationToken.ThrowIfCancellationRequested();
            _lastKnownEntryValues = nextEntryValues;
            foreach (var index in supersededStartupValues)
            {
                _keepsStartupValue[index] = false;
                SecretsLog.RefreshSupersededStartupValue(logger, Entries[index].SecretId);
            }

            if (ConfigurationValuesEqual(current, merged))
                return false;

            _provider.Replace(merged, signalReload: false);
            try
            {
                _provider.NotifyReload();
            }
            catch (Exception ex)
            {
                // Callback exceptions may contain secrets; never attach the exception or its message.
                SecretsLog.ReloadSubscriberFailed(logger, ex.GetType().Name);
            }

            SecretsLog.RefreshApplied(logger, Entries.Count, merged.Count);
            return true;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    public void Dispose() => _reloadGate.Dispose();

    internal static IReadOnlyDictionary<string, string?> MergeInDeclarationOrder(
        IReadOnlyList<IReadOnlyDictionary<string, string?>> entryValues)
    {
        var merged = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (IReadOnlyDictionary<string, string?> values in entryValues)
        {
            foreach ((var key, var value) in values)
                merged[key] = value;
        }

        return merged;
    }

    private static bool ConfigurationValuesEqual(
        IReadOnlyDictionary<string, string?> left,
        IReadOnlyDictionary<string, string?> right)
    {
        if (left.Count != right.Count)
            return false;

        foreach ((var key, var value) in left)
        {
            if (!right.TryGetValue(key, out var candidate)
                || !string.Equals(value, candidate, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
