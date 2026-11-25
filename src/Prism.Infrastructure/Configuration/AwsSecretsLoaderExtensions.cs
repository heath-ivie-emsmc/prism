using Amazon.Runtime;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Fetches this host's secrets from AWS Secrets Manager at startup and merges them into <see cref="IConfiguration"/>.
/// See <c>docs/secrets.md</c> for the design; this is the single public entry point.
/// </summary>
public static class AwsSecretsLoaderExtensions
{
    public const string SectionName = AwsSecretsLoaderOptions.SectionName;

    internal static readonly TimeSpan DefaultOverallBudget = TimeSpan.FromSeconds(20);
    private const int MaxConcurrency = 4;
    internal const int MaxSecrets = 20;
    private const string BreakGlassEnvVarPrefix = "PRISM_SECRET_OVERRIDE__";
    private const string BreakGlassConfigurationPrefix = "PRISM_SECRET_OVERRIDE:";

    /// <summary>
    /// Used when a caller passes no logger. The loader runs before the host is built, so there is no DI logger
    /// yet — the same bootstrap-console approach as <c>FeatureManagementConfigurationBootstrap</c>. Defaulting
    /// here rather than at each call site matters: with a null default, forgetting the argument silently
    /// deletes the loader's entire diagnostic surface, including the startup line the rollout depends on.
    /// </summary>
    private static readonly ILogger BootstrapLogger =
        LoggerFactory.Create(static builder => builder.AddSimpleConsole())
            .CreateLogger(typeof(AwsSecretsLoaderExtensions));

    public static Task<AwsSecretsLoaderResult> AddAwsSecretsLoaderAsync(
        this ConfigurationManager configuration,
        IHostEnvironment environment,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        logger ??= BootstrapLogger;

        AwsSecretsLoaderOptions options = BindOptions(configuration);

        if (!options.Enabled)
            return Task.FromResult(AwsSecretsLoaderResult.Empty);

        // Before constructing a client: with no entries there is nothing to fetch, and an AmazonSecretsManagerClient
        // built with no resolvable region would throw for no reason.
        if (options.Secrets.Count == 0)
            return Task.FromResult(NoEntriesResult(logger));

        return AddAwsSecretsLoaderWithReaderAsync(configuration, options, environment, logger, cancellationToken);
    }

    internal static AwsSecretsLoaderOptions BindOptions(IConfiguration configuration)
    {
        IConfigurationSection loaderSection = configuration.GetSection(AwsSecretsLoaderOptions.SectionName);
        var options = loaderSection.Get<AwsSecretsLoaderOptions>() ?? new AwsSecretsLoaderOptions();
        List<IConfigurationSection> configuredSecrets = loaderSection
            .GetSection(nameof(AwsSecretsLoaderOptions.Secrets))
            .GetChildren()
            .ToList();

        for (var index = 0; index < Math.Min(options.Secrets.Count, configuredSecrets.Count); index++)
        {
            IConfigurationSection keysSection = configuredSecrets[index].GetSection(nameof(AwsSecretsLoaderEntry.Keys));
            if (!keysSection.Exists())
                continue;

            options.Secrets[index].Keys = BindKeyMappings(keysSection);
        }

        return options;
    }

    private static Dictionary<string, string> BindKeyMappings(IConfigurationSection keysSection)
    {
        var mappings = new Dictionary<string, string>(StringComparer.Ordinal);
        AddKeyMappings(keysSection, null, mappings);
        return mappings;
    }

    private static void AddKeyMappings(
        IConfigurationSection section,
        string? keyPrefix,
        Dictionary<string, string> mappings)
    {
        foreach (IConfigurationSection child in section.GetChildren())
        {
            string key = keyPrefix is null ? child.Key : $"{keyPrefix}:{child.Key}";
            if (child.Value is not null)
            {
                mappings[key] = child.Value;
                continue;
            }

            AddKeyMappings(child, key, mappings);
        }
    }

    private static async Task<AwsSecretsLoaderResult> AddAwsSecretsLoaderWithReaderAsync(
        ConfigurationManager configuration,
        AwsSecretsLoaderOptions options,
        IHostEnvironment environment,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        using AwsSecretStringReader reader = CreateReader(options, environment, logger);
        return await AddAwsSecretsLoaderCoreAsync(
            configuration, options, reader, logger, cancellationToken, environment: environment)
            .ConfigureAwait(false);
    }

    /// <summary>Constructing the client can fail on configuration (a missing region); report that like any other startup failure.</summary>
    private static AwsSecretStringReader CreateReader(AwsSecretsLoaderOptions options, IHostEnvironment environment, ILogger logger)
    {
        try
        {
            return new AwsSecretStringReader(options, environment);
        }
        catch (AwsSecretsLoaderException ex)
        {
            LogStartupFailure(logger, ex);
            throw;
        }
    }

    /// <summary>
    /// Reader-injectable core, shared with the public entry point above. Internal so unit tests can drive the
    /// whole entry-validation / mode-filter / skip-if-set / fetch / flatten / policy / merge / break-glass
    /// pipeline with a fake <see cref="IAwsSecretStringReader"/> and no AWS calls.
    /// </summary>
    internal static Task<AwsSecretsLoaderResult> AddAwsSecretsLoaderCoreAsync(
        ConfigurationManager configuration,
        AwsSecretsLoaderOptions options,
        IAwsSecretStringReader reader,
        ILogger? logger,
        CancellationToken cancellationToken,
        TimeSpan? overallBudget = null,
        IHostEnvironment? environment = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(reader);

        if (!options.Enabled)
            return Task.FromResult(AwsSecretsLoaderResult.Empty);

        if (options.Secrets.Count == 0)
            return Task.FromResult(NoEntriesResult(logger));

        ValidateOptions(options, logger);

        return LogStartupFailuresAsync(
            AddValidatedAwsSecretsLoaderCoreAsync(
                configuration, options, reader, logger, overallBudget, environment, cancellationToken),
            logger);
    }

    /// <summary>
    /// Every entry is validated before any fetch starts, so a configuration mistake fails the host without having
    /// reached out to AWS first.
    /// </summary>
    private static void ValidateOptions(AwsSecretsLoaderOptions options, ILogger? logger)
    {
        try
        {
            if (options.ReloadIntervalSeconds <= 0)
            {
                throw new AwsSecretsLoaderException(
                    "Aws:SecretsLoader:ReloadIntervalSeconds must be greater than zero when the loader is enabled.");
            }

            if (options.Secrets.Count > MaxSecrets)
                throw new AwsSecretsLoaderException($"Aws:SecretsLoader:Secrets has more than {MaxSecrets} entries.");

            foreach (AwsSecretsLoaderEntry entry in options.Secrets)
                AwsSecretKeyPolicy.ValidateEntryDefinition(entry);
        }
        catch (AwsSecretsLoaderException ex)
        {
            LogStartupFailure(logger, ex);
            throw;
        }
    }

    private static async Task<AwsSecretsLoaderResult> LogStartupFailuresAsync(
        Task<AwsSecretsLoaderResult> load,
        ILogger? logger)
    {
        try
        {
            return await load.ConfigureAwait(false);
        }
        catch (AwsSecretsLoaderException ex)
        {
            LogStartupFailure(logger, ex);
            throw;
        }
    }

    /// <summary>
    /// One Critical line per startup failure, with a fixed "FAILED" marker so a single log query finds every loader
    /// failure. The host's startup guard still prints the full redacted exception to stderr; this line is the
    /// searchable summary. <see cref="AwsSecretsLoaderException"/> messages carry secret ids, key names and AWS error
    /// codes only, never values, which is what makes logging the message safe here.
    /// </summary>
    private static void LogStartupFailure(ILogger? logger, AwsSecretsLoaderException exception)
    {
        // Message-only by design: the exception object would bypass redaction. The loader's exception message is value-free.
        SecretsLog.StartupFailed(logger ?? NullLogger.Instance, exception.Message);
    }

    private static async Task<AwsSecretsLoaderResult> AddValidatedAwsSecretsLoaderCoreAsync(
        ConfigurationManager configuration,
        AwsSecretsLoaderOptions options,
        IAwsSecretStringReader reader,
        ILogger? logger,
        TimeSpan? overallBudget,
        IHostEnvironment? environment,
        CancellationToken cancellationToken)
    {
        (List<AwsSecretsLoaderEntry> planned,
            List<AwsSecretsLoaderEntry> reloadEntries,
            Dictionary<AwsSecretsLoaderEntry, IReadOnlyDictionary<string, string?>> skippedValues,
            List<string> skippedSecretIds) = PlanEntries(configuration, options, logger);

        EntryOutcome[] outcomes = await FetchAllAsync(
            planned, reader, logger, overallBudget ?? DefaultOverallBudget, cancellationToken)
            .ConfigureAwait(false);

        var loadedSecretIds = new List<string>(outcomes.Length);
        var initialEntryValues = new IReadOnlyDictionary<string, string?>[outcomes.Length];
        for (var index = 0; index < outcomes.Length; index++)
        {
            EntryOutcome outcome = outcomes[index];
            if (outcome.Skipped)
                skippedSecretIds.Add(outcome.SecretId);
            else
                loadedSecretIds.Add(outcome.SecretId);

            initialEntryValues[index] = outcome.Values;
        }

        IReadOnlyDictionary<string, string?> merged =
            AwsSecretsReloadState.MergeInDeclarationOrder(initialEntryValues);
        cancellationToken.ThrowIfCancellationRequested();

        // Before the provider is added, so configuration still shows what was set before the loader ran.
        if (options.OnlyUseAwsSecrets)
            LogOnlyUseAwsSecretsFindings(configuration, reloadEntries, outcomes, merged, logger);

        if (reloadEntries.Count > 0)
        {
            var provider = new AwsSecretsConfigurationProvider();
            provider.Replace(merged, signalReload: false);
            ((IConfigurationBuilder)configuration).Add(new AwsSecretsConfigurationSource(provider));
            if (environment is not null)
            {
                AwsSecretsReloadServiceCollectionExtensions.SetReloadState(
                    configuration,
                    new AwsSecretsReloadState(options, environment, reloadEntries, provider,
                        reloadEntries.Select(entry => skippedValues.TryGetValue(entry, out var values)
                            ? values
                            : initialEntryValues[planned.IndexOf(entry)]).ToArray(),
                        reloadEntries.Select(skippedValues.ContainsKey).ToArray()));
            }
        }

        var loadedKeys = new HashSet<string>(merged.Keys, StringComparer.OrdinalIgnoreCase);

        LogOutcome(logger, loadedSecretIds, skippedSecretIds, loadedKeys);

        return new AwsSecretsLoaderResult(loadedKeys, loadedSecretIds, skippedSecretIds);
    }

    /// <summary>
    /// Splits the configured entries into those to fetch now, those kept for the hourly refresh, and those skipped
    /// at startup — either outside this host's mode or already fully configured.
    /// </summary>
    private static (
        List<AwsSecretsLoaderEntry> Planned,
        List<AwsSecretsLoaderEntry> ReloadEntries,
        Dictionary<AwsSecretsLoaderEntry, IReadOnlyDictionary<string, string?>> SkippedValues,
        List<string> SkippedSecretIds) PlanEntries(
        ConfigurationManager configuration,
        AwsSecretsLoaderOptions options,
        ILogger? logger)
    {
        var skippedSecretIds = new List<string>();
        var planned = new List<AwsSecretsLoaderEntry>(options.Secrets.Count);
        var reloadEntries = new List<AwsSecretsLoaderEntry>(options.Secrets.Count);
        var skippedValues = new Dictionary<AwsSecretsLoaderEntry, IReadOnlyDictionary<string, string?>>();

        foreach (AwsSecretsLoaderEntry entry in options.Secrets)
        {
            reloadEntries.Add(entry);
            if (!options.OnlyUseAwsSecrets && AllCandidateKeysAlreadySet(configuration, entry))
            {
                skippedValues[entry] = AwsSecretPayloadFlattener.ResolveCandidateKeys(entry)
                    .ToDictionary(key => key, key => configuration[key], StringComparer.OrdinalIgnoreCase);
                SecretsLog.SkippedAlreadyConfigured(logger ?? NullLogger.Instance, entry.SecretId);

                skippedSecretIds.Add(entry.SecretId);
                continue;
            }

            planned.Add(entry);
        }

        return (planned, reloadEntries, skippedValues, skippedSecretIds);
    }

    /// <summary>Enabled with nothing to fetch.</summary>
    private static AwsSecretsLoaderResult NoEntriesResult(ILogger? logger)
    {
        SecretsLog.NoEntries(logger ?? NullLogger.Instance);

        return AwsSecretsLoaderResult.Empty;
    }

    internal static async Task<EntryOutcome[]> FetchAllAsync(
        List<AwsSecretsLoaderEntry> planned,
        IAwsSecretStringReader reader,
        ILogger? logger,
        TimeSpan overallBudget,
        CancellationToken cancellationToken)
    {
        if (planned.Count == 0)
            return [];

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(overallBudget);
        using var throttle = new SemaphoreSlim(MaxConcurrency, MaxConcurrency);

        Task<EntryOutcome>[] fetches = [.. planned.Select(entry =>
            ProcessEntryAsync(entry, reader, throttle, logger, budget.Token))];

        try
        {
            EntryOutcome[] outcomes = await Task.WhenAll(fetches).ConfigureAwait(false);
            budget.Token.ThrowIfCancellationRequested();
            return outcomes;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Task.WhenAll only completes once every task has completed, so the states below are final.
            // Without this, a budget expiry surfaces as a bare TaskCanceledException naming nothing — the one
            // failure path with no secret id in it, and the one most likely to fire during a real AWS incident
            // rather than a config typo.
            var unfinished = planned
                .Where((_, index) => !fetches[index].IsCompletedSuccessfully)
                .Select(entry => entry.SecretId)
                .ToList();

            throw new AwsSecretsLoaderException(
                $"AWS Secrets Manager loader exceeded its {overallBudget.TotalSeconds:0}-second overall budget. " +
                $"These secrets had not loaded: {string.Join(", ", unfinished)}. " +
                "Secrets that did load are discarded: the host must not start half-configured.");
        }
    }

    private static async Task<EntryOutcome> ProcessEntryAsync(
        AwsSecretsLoaderEntry entry,
        IAwsSecretStringReader reader,
        SemaphoreSlim throttle,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        await throttle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            string? secretString;
            try
            {
                secretString = await reader.GetSecretStringAsync(entry.SecretId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (entry.Optional && IsSkippableFetchFailure(ex))
            {
                // Message-only by design: attaching the exception would bypass redaction. An optional secret being absent is expected, not a fault.
                SecretsLog.OptionalSkipped(logger ?? NullLogger.Instance, entry.SecretId, DescribeFetchFailure(ex));
                return EntryOutcome.AsSkipped(entry.SecretId);
            }
            catch (Exception ex) when (ex is not AwsSecretsLoaderException
                && !(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // A raw SDK exception names neither the secret nor, for KMS denials, the resource at all. Wrap it with
                // the secret id and the AWS error code (safe to log, unlike the SDK message) so startup and refresh
                // failures say exactly which secret failed and why. Caller or budget cancellation passes through
                // unchanged for FetchAllAsync to report.
                throw new AwsSecretsLoaderException(
                    $"Secrets Manager entry '{entry.SecretId}' could not be fetched ({DescribeFailureCode(ex)}).",
                    ex);
            }

            if (string.IsNullOrWhiteSpace(secretString))
            {
                // Not an exception from AWS: a rotation that leaves no AWSCURRENT stage returns a null
                // SecretString, so this has to be caught explicitly or the host boots with a null secret.
                throw new AwsSecretsLoaderException(
                    $"Secrets Manager entry '{entry.SecretId}' returned no AWSCURRENT SecretString " +
                    "(binary secrets and rotations with no current stage are not supported).");
            }

            IReadOnlyList<AwsSecretFlattenedProperty> properties = AwsSecretPayloadFlattener.Flatten(entry, secretString);
            IReadOnlyDictionary<string, string?> values = AwsSecretKeyPolicy.ApplyAndFilter(entry, properties, logger);
            return EntryOutcome.Loaded(entry.SecretId, values);
        }
        finally
        {
            throttle.Release();
        }
    }

    /// <summary>
    /// Classifies on exception type and <c>ErrorCode</c> only. Never on HTTP status: Secrets Manager answers 400
    /// for throttling, denial and not-found alike, so a status-based rule would silently swallow throttling.
    /// </summary>
    private static bool IsSkippableFetchFailure(Exception ex) => ex switch
    {
        ResourceNotFoundException => true,
        AmazonSecretsManagerException amazon =>
            string.Equals(amazon.ErrorCode, "AccessDeniedException", StringComparison.Ordinal),
        _ => false,
    };

    private static string DescribeFetchFailure(Exception ex) => ex switch
    {
        ResourceNotFoundException => "not found in this environment",
        AmazonSecretsManagerException amazon => $"access denied ({amazon.ErrorCode})",
        _ => ex.GetType().Name,
    };

    /// <summary>
    /// The AWS error code (e.g. <c>AccessDeniedException</c>, <c>DecryptionFailure</c>, <c>ThrottlingException</c>)
    /// when the SDK supplies one, else the exception type. Never the exception message, which can carry endpoint or
    /// credential text.
    /// </summary>
    internal static string DescribeFailureCode(Exception ex) => ex switch
    {
        AmazonServiceException amazon when !string.IsNullOrWhiteSpace(amazon.ErrorCode) => amazon.ErrorCode,
        _ => ex.GetType().Name,
    };

    /// <summary>
    /// An already-configured value (typically
    /// still env-injected during the cutover) wins, and the fetch is not even attempted.
    /// </summary>
    private static bool AllCandidateKeysAlreadySet(ConfigurationManager configuration, AwsSecretsLoaderEntry entry)
    {
        IReadOnlyList<string> candidates = AwsSecretPayloadFlattener.ResolveCandidateKeys(entry);
        if (candidates.Count == 0)
            return false;

        return candidates.All(key => !string.IsNullOrWhiteSpace(configuration[key]));
    }

    /// <summary>
    /// With <see cref="AwsSecretsLoaderOptions.OnlyUseAwsSecrets"/> on, reports which pre-existing values AWS replaced
    /// with a different value, and which keys an entry should supply that still come from another source.
    /// Ids and key names only. A value never reaches a log sink from here.
    /// </summary>
    private static void LogOnlyUseAwsSecretsFindings(
        ConfigurationManager configuration,
        List<AwsSecretsLoaderEntry> reloadEntries,
        EntryOutcome[] outcomes,
        IReadOnlyDictionary<string, string?> merged,
        ILogger? logger)
    {
        if (logger is null)
            return;

        foreach (EntryOutcome outcome in outcomes.Where(outcome => !outcome.Skipped))
        {
            List<string> replaced = [.. outcome.Values
                .Where(pair => !string.IsNullOrWhiteSpace(configuration[pair.Key])
                    && !string.Equals(configuration[pair.Key], pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .Order(StringComparer.Ordinal)];
            if (replaced.Count > 0)
                SecretsLog.OnlyAwsReplaced(logger, outcome.SecretId, string.Join(", ", replaced));
        }

        foreach (AwsSecretsLoaderEntry entry in reloadEntries)
        {
            List<string> notFromAws = [.. AwsSecretPayloadFlattener.ResolveCandidateKeys(entry)
                .Where(key => !merged.ContainsKey(key) && !string.IsNullOrWhiteSpace(configuration[key]))
                .Order(StringComparer.Ordinal)];
            if (notFromAws.Count > 0)
                SecretsLog.OnlyAwsNotFromAws(logger, string.Join(", ", notFromAws), entry.SecretId);
        }
    }

    /// <summary>Ids and key names only. A value never reaches a log sink from here.</summary>
    private static void LogOutcome(
        ILogger? logger,
        List<string> loadedSecretIds,
        List<string> skippedSecretIds,
        HashSet<string> loadedKeys)
    {
        if (logger is null)
            return;

        SecretsLog.Loaded(
            logger,
            loadedSecretIds.Count,
            string.Join(", ", loadedSecretIds),
            skippedSecretIds.Count,
            string.Join(", ", skippedSecretIds),
            loadedKeys.Count,
            string.Join(", ", loadedKeys.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Applies every <c>PRISM_SECRET_OVERRIDE__&lt;key&gt;</c> environment variable as the final configuration
    /// source, and folds both each destination key and its normalized <c>PRISM_SECRET_OVERRIDE:&lt;key&gt;</c> alias
    /// into <paramref name="loaded"/> so their values are redacted from the startup configuration dump.
    /// </summary>
    /// <remarks>
    /// Hosts must call this <b>after</b> every other merge, including the App Config sidecar. It used to run
    /// inside the loader, which put it before the sidecar merge and meant an overlapping App Config value beat
    /// it — the opposite of the documented guarantee, and worst precisely during the incident the channel
    /// exists for: an operator sets the override, restarts, and still gets the bad App Config value.
    /// </remarks>
    public static AwsSecretsLoaderResult ApplySecretOverridesFromEnvironment(
        this ConfigurationManager configuration,
        AwsSecretsLoaderResult? loaded = null,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        AwsSecretsLoaderResult result = loaded ?? AwsSecretsLoaderResult.Empty;
        HashSet<string> overridden = ApplyBreakGlassOverrides(configuration);
        if (overridden.Count == 0)
            return result;

        SecretsLog.BreakGlassApplied(
            logger ?? BootstrapLogger,
            overridden.Count,
            BreakGlassEnvVarPrefix,
            string.Join(", ", overridden.Order(StringComparer.Ordinal)));

        var keys = new HashSet<string>(result.LoadedKeys, StringComparer.OrdinalIgnoreCase);
        keys.UnionWith(overridden);
        keys.UnionWith(overridden.Select(key => BreakGlassConfigurationPrefix + key));
        return result with { LoadedKeys = keys };
    }

    private static HashSet<string> ApplyBreakGlassOverrides(ConfigurationManager configuration)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is not string name || !name.StartsWith(BreakGlassEnvVarPrefix, StringComparison.Ordinal))
                continue;

            var configKey = name[BreakGlassEnvVarPrefix.Length..].Replace("__", ":", StringComparison.Ordinal);
            if (configKey.Length == 0)
                continue;

            // Same denylist as a loaded secret. An operator with kubectl could set these another way, but an
            // override that lands in LoadedKeys would also be redacted from the startup dump — a protected key
            // changed invisibly is worse than one that is simply refused.
            AwsSecretKeyPolicy.ThrowIfDeniedKey(BreakGlassEnvVarPrefix + name[BreakGlassEnvVarPrefix.Length..], configKey);

            overrides[configKey] = entry.Value as string;
        }

        if (overrides.Count > 0)
            configuration.AddInMemoryCollection(overrides);

        return new HashSet<string>(overrides.Keys, StringComparer.OrdinalIgnoreCase);
    }

    internal readonly record struct EntryOutcome(string SecretId, bool Skipped, IReadOnlyDictionary<string, string?> Values)
    {
        internal static EntryOutcome Loaded(string secretId, IReadOnlyDictionary<string, string?> values) => new(secretId, false, values);

        internal static EntryOutcome AsSkipped(string secretId) => new(secretId, true, new Dictionary<string, string?>());
    }
}
