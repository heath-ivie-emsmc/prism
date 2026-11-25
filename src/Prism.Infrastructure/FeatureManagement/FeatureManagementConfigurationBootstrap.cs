using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

/// <summary>
/// Loads App Config freeform settings into <see cref="IConfiguration"/> before host build so
/// <c>ValidateOnStart</c> sees sidecar values (hosted services run after startup validation).
/// </summary>
public static class FeatureManagementConfigurationBootstrap
{
    public const string HttpClientName = "FeatureManagementSidecarBootstrap";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly IReadOnlyDictionary<string, string?> EmptyAppSettings =
        new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    private static readonly ILogger Logger = CreateBootstrapLogger();

    private static ILogger CreateBootstrapLogger() =>
        LoggerFactory.Create(static builder => builder.AddSimpleConsole())
            .CreateLogger(typeof(FeatureManagementConfigurationBootstrap));

    public static Task MergeSidecarAppSettingsIfEnabledAsync(
        this ConfigurationManager configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return MergeSidecarAppSettingsIfEnabledCoreAsync(configuration, cancellationToken);
    }

    private static async Task MergeSidecarAppSettingsIfEnabledCoreAsync(
        ConfigurationManager configuration,
        CancellationToken cancellationToken)
    {
        FeatureManagementOptions options = configuration.GetSection(FeatureManagementOptions.SectionName).Get<FeatureManagementOptions>()
            ?? new FeatureManagementOptions();

        if (!options.Enabled || !options.EnableDynamicAppSettings)
            return;

        if (string.IsNullOrWhiteSpace(options.AppSettingsConfiguration))
        {
            throw new InvalidOperationException(
                "FeatureManagement:AppSettingsConfiguration is required when dynamic appsettings are enabled.");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl)
            || string.IsNullOrWhiteSpace(options.Application)
            || string.IsNullOrWhiteSpace(options.Environment))
        {
            throw new InvalidOperationException(
                "FeatureManagement:BaseUrl, Application, and Environment are required when the sidecar is enabled.");
        }

        IReadOnlyDictionary<string, string?> appSettings;
        try
        {
            appSettings = await FetchAppSettingsWithRetryAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (options.StartupFailureMode == StartupFailureMode.FailOpen)
        {
            FeatureManagementLog.BootstrapMergeFailedOpen(Logger, exception.GetType().Name);
            return;
        }

        if (appSettings.Count > 0)
            configuration.AddInMemoryCollection(appSettings);
    }

    private static async Task<IReadOnlyDictionary<string, string?>> FetchAppSettingsWithRetryAsync(
        FeatureManagementOptions options,
        CancellationToken cancellationToken)
    {
        var services = new ServiceCollection();
        services.AddHttpClient(HttpClientName, client =>
        {
            var baseUri = new Uri(options.BaseUrl, UriKind.Absolute);
            client.BaseAddress = new Uri(baseUri, new Uri("./", UriKind.Relative));
            client.Timeout = options.RequestTimeout;
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        HttpClient httpClient = provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

        var stopwatch = Stopwatch.StartNew();
        await SidecarPortWait.WaitUntilListeningAsync(options, stopwatch, cancellationToken).ConfigureAwait(false);
        var attempt = 0;

        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using JsonDocument? document = await FetchJsonDocumentAsync(
                        httpClient,
                        options,
                        options.AppSettingsConfiguration,
                        cancellationToken)
                    .ConfigureAwait(false);

                // 404 means the profile does not exist for this application/environment, which is
                // "no overrides configured" — not a failure. Since every environment's identity now
                // ships in appsettings.<Environment>.json, an empty or absent freeform profile is the
                // expected steady state, and a brand-new environment has one before anybody creates
                // it. Retrying a 404 for StartupMaxWait and then killing the host made standing up an
                // environment depend on creating a profile that has nothing left to say.
                //
                // Deliberately narrow: this is the appsettings/freeform profile only. A missing
                // feature-flag profile stays fatal (see AppConfigSidecarHostedService), because the
                // FeatureFlags:* kill switches have to be reachable. Every other status code, and any
                // transport failure, keeps the existing retry-then-fail behaviour.
                if (document is null)
                {
                    FeatureManagementLog.NoAppSettingsProfile(Logger, options.AppSettingsConfiguration, options.Application, options.Environment);
                    return EmptyAppSettings;
                }

                return SidecarJsonParser.ParseAppSettings(document.RootElement);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not SidecarSettingRefusedException && stopwatch.Elapsed < options.StartupMaxWait)
            {
                TimeSpan remaining = options.StartupMaxWait - stopwatch.Elapsed;
                TimeSpan delay = remaining < RetryDelay ? remaining : RetryDelay;
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Null when the sidecar reports the configuration profile does not exist (HTTP 404).</summary>
    private static async Task<JsonDocument?> FetchJsonDocumentAsync(
        HttpClient httpClient,
        FeatureManagementOptions options,
        string configurationName,
        CancellationToken cancellationToken)
    {
        Uri endpoint = BuildConfigurationUri(options.BaseUrl, options.Application, options.Environment, configurationName);
        using HttpResponseMessage response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await BoundedSidecarJson.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);
    }

    public static Uri BuildConfigurationUri(string baseUrl, string application, string environment, string configuration)
    {
        var normalizedBaseUrl = baseUrl.TrimEnd('/');
        return new Uri(
            $"{normalizedBaseUrl}/applications/{Uri.EscapeDataString(application)}/environments/{Uri.EscapeDataString(environment)}/configurations/{Uri.EscapeDataString(configuration)}",
            UriKind.Absolute);
    }
}
