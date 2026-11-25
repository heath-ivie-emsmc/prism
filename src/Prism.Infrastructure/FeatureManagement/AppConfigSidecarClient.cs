using System.Text.Json;


using Microsoft.Extensions.Options;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class AppConfigSidecarClient(HttpClient httpClient, IOptionsMonitor<FeatureManagementOptions> options, TimeProvider timeProvider)
{

    public async Task<SidecarState.Snapshot> FetchFeatureSnapshotAsync(CancellationToken cancellationToken)
    {
        FeatureManagementOptions current = options.CurrentValue;
        using JsonDocument document = await FetchJsonAsync(current.FeatureConfiguration, current, cancellationToken).ConfigureAwait(false);

        return new SidecarState.Snapshot(
            timeProvider.GetUtcNow(),
            SidecarJsonParser.ParseFeatures(document.RootElement));
    }

    public async Task<IReadOnlyDictionary<string, string?>> FetchAppSettingsAsync(CancellationToken cancellationToken)
    {
        FeatureManagementOptions current = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(current.AppSettingsConfiguration))
        {
            throw new InvalidOperationException("FeatureManagement:AppSettingsConfiguration is required when dynamic appsettings are enabled.");
        }

        using JsonDocument document = await FetchJsonAsync(current.AppSettingsConfiguration, current, cancellationToken).ConfigureAwait(false);
        return SidecarJsonParser.ParseAppSettings(document.RootElement);
    }

    private async Task<JsonDocument> FetchJsonAsync(
        string configurationName,
        FeatureManagementOptions current,
        CancellationToken cancellationToken)
    {
        Uri endpoint = BuildEndpoint(current.BaseUrl, current.Application, current.Environment, configurationName);

        using HttpResponseMessage response = await httpClient.GetAsync(endpoint, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await BoundedSidecarJson.ParseAsync(responseStream, cancellationToken).ConfigureAwait(false);
    }

    private static Uri BuildEndpoint(
        string baseUrl,
        string application,
        string environment,
        string configuration)
    {
        string normalizedBaseUrl = baseUrl.TrimEnd('/');
        string escapedApplication = Uri.EscapeDataString(application);
        string escapedEnvironment = Uri.EscapeDataString(environment);
        string escapedConfiguration = Uri.EscapeDataString(configuration);

        return new Uri(
            $"{normalizedBaseUrl}/applications/{escapedApplication}/environments/{escapedEnvironment}/configurations/{escapedConfiguration}",
            UriKind.Absolute);
    }
}
