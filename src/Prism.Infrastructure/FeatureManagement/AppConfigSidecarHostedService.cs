using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;


using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Prism.Application.FeatureManagement;

namespace Prism.Infrastructure.FeatureManagement;

public sealed class AppConfigSidecarHostedService : BackgroundService
{
    private static readonly TimeSpan BootstrapRetryDelay = TimeSpan.FromSeconds(1);

    private readonly AppConfigSidecarClient _sidecarClient;
    private readonly SidecarState _state;
    private readonly SidecarConfigurationProvider _configurationProvider;
    private readonly IOptionsMonitor<FeatureManagementOptions> _options;
    private readonly ILogger<AppConfigSidecarHostedService> _logger;
    private readonly FeatureManagementBootstrapState _bootstrapState;
    private readonly IHostApplicationLifetime _lifetime;

    public AppConfigSidecarHostedService(
        AppConfigSidecarClient sidecarClient,
        SidecarState state,
        SidecarConfigurationProvider configurationProvider,
        IOptionsMonitor<FeatureManagementOptions> options,
        ILogger<AppConfigSidecarHostedService> logger,
        FeatureManagementBootstrapState bootstrapState,
        IHostApplicationLifetime lifetime)
    {
        _sidecarClient = sidecarClient;
        _state = state;
        _configurationProvider = configurationProvider;
        _options = options;
        _logger = logger;
        _bootstrapState = bootstrapState;
        _lifetime = lifetime;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.Enabled)
        {
            FeatureManagementLog.Disabled(_logger);
        }

        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        FeatureManagementOptions options = _options.CurrentValue;

        if (options.Enabled && !await BootstrapAndReportAsync(options, stoppingToken).ConfigureAwait(false))
        {
            return;
        }

        _bootstrapState.MarkBootstrapComplete();

        await RunPollLoopAsync(stoppingToken).ConfigureAwait(false);
    }

    /// <returns><see langword="false"/> when the host should stop running.</returns>
    private async Task<bool> BootstrapAndReportAsync(FeatureManagementOptions options, CancellationToken stoppingToken)
    {
        try
        {
            BootstrapResult result = await BootstrapAsync(options, stoppingToken).ConfigureAwait(false);

            FeatureManagementLog.BootstrapSucceeded(
                _logger,
                result.FeatureCount,
                result.AppSettingsCount,
                options.BaseUrl,
                options.Application,
                options.Environment,
                options.FeatureConfiguration);

            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception) when (options.StartupFailureMode == StartupFailureMode.FailOpen)
        {
            FeatureManagementLog.BootstrapFailedOpen(_logger, exception.GetType().Name);
            return true;
        }
        catch (Exception exception)
        {
            _bootstrapState.MarkBootstrapFailed(exception);
            FeatureManagementLog.BootstrapFailedClosed(_logger, exception.GetType().Name);
            _lifetime.StopApplication();
            return false;
        }
    }

    private async Task RunPollLoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await WaitForNextPollAsync(stoppingToken).ConfigureAwait(false))
            {
                break;
            }

            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await RefreshFeaturesAsync(stoppingToken).ConfigureAwait(false);
                await RefreshAppSettingsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task<BootstrapResult> BootstrapAsync(
        FeatureManagementOptions options,
        CancellationToken cancellationToken)
    {
        int attempt = 0;
        var stopwatch = Stopwatch.StartNew();

        using var startupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startupCts.CancelAfter(options.StartupMaxWait);

        CancellationToken startupToken = startupCts.Token;

        await SidecarPortWait.WaitUntilListeningAsync(options, stopwatch, startupToken).ConfigureAwait(false);

        var attemptCancellation = new BootstrapAttemptCancellation(startupToken, cancellationToken);

        while (!startupToken.IsCancellationRequested)
        {
            attempt++;
            BootstrapResult? result = await TryBootstrapAttemptAsync(options, stopwatch, attempt, attemptCancellation)
                .ConfigureAwait(false);
            if (result is not null)
                return result;
        }

        throw new TimeoutException(
            $"Failed to bootstrap AppConfig sidecar within {options.StartupMaxWait}. Attempts={attempt}.");
    }

    private async Task<BootstrapResult?> TryBootstrapAttemptAsync(
        FeatureManagementOptions options,
        Stopwatch stopwatch,
        int attempt,
        BootstrapAttemptCancellation cancellation)
    {
        try
        {
            return await RefreshRequiredDataAsync(options, cancellation.StartupToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.OuterToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellation.StartupToken.IsCancellationRequested)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return await HandleBootstrapAttemptFailureAsync(
                exception,
                options,
                stopwatch,
                attempt,
                cancellation).ConfigureAwait(false);
        }
    }

    private async Task<BootstrapResult?> HandleBootstrapAttemptFailureAsync(
        Exception exception,
        FeatureManagementOptions options,
        Stopwatch stopwatch,
        int attempt,
        BootstrapAttemptCancellation cancellation)
    {
        TimeSpan remaining = options.StartupMaxWait - stopwatch.Elapsed;
        if (remaining <= TimeSpan.Zero)
            return null;

        TimeSpan delay = remaining < BootstrapRetryDelay ? remaining : BootstrapRetryDelay;
        LogBootstrapAttemptFailure(exception, attempt, delay);

        try
        {
            await Task.Delay(delay, cancellation.StartupToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellation.OuterToken.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    private readonly record struct BootstrapAttemptCancellation(CancellationToken StartupToken, CancellationToken OuterToken);

    private void LogBootstrapAttemptFailure(Exception exception, int attempt, TimeSpan delay)
    {
        if (IsSidecarNotListeningException(exception))
        {
            FeatureManagementLog.BootstrapWaitingForAgent(_logger, attempt, (long)delay.TotalMilliseconds);
            return;
        }

        FeatureManagementLog.BootstrapAttemptFailed(_logger, attempt, exception.GetType().Name, (long)delay.TotalMilliseconds);
    }

    private static bool IsSidecarNotListeningException(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
            {
                return true;
            }

            if (current is HttpRequestException httpRequestException
                && httpRequestException.HttpRequestError == HttpRequestError.ConnectionError)
            {
                return true;
            }
        }

        return false;
    }

    private async Task<BootstrapResult> RefreshRequiredDataAsync(
        FeatureManagementOptions options,
        CancellationToken cancellationToken)
    {
        SidecarState.Snapshot snapshot = await _sidecarClient
            .FetchFeatureSnapshotAsync(cancellationToken)
            .ConfigureAwait(false);

        if (options.RequireNonEmptyFeatureConfig && snapshot.Features.Count == 0)
        {
            throw new InvalidOperationException(
                "The feature configuration payload was empty during startup.");
        }

        _state.Replace(snapshot);

        int appSettingsCount = 0;

        if (options.EnableDynamicAppSettings)
        {
            IReadOnlyDictionary<string, string?> appSettings = await _sidecarClient
                .FetchAppSettingsAsync(cancellationToken)
                .ConfigureAwait(false);

            _configurationProvider.Replace(appSettings);

            appSettingsCount = appSettings.Count;
        }

        return new BootstrapResult(snapshot.Features.Count, appSettingsCount);
    }

    private async Task<bool> WaitForNextPollAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(_options.CurrentValue.PollInterval, stoppingToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private async Task RefreshFeaturesAsync(CancellationToken cancellationToken)
    {
        try
        {
            SidecarState.Snapshot snapshot = await _sidecarClient
                .FetchFeatureSnapshotAsync(cancellationToken)
                .ConfigureAwait(false);
            _state.Replace(snapshot);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            FeatureManagementLog.FeatureRefreshFailed(_logger, exception.GetType().Name);
        }
    }

    private async Task RefreshAppSettingsAsync(CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.EnableDynamicAppSettings)
        {
            return;
        }

        try
        {
            IReadOnlyDictionary<string, string?> appSettings = await _sidecarClient
                .FetchAppSettingsAsync(cancellationToken)
                .ConfigureAwait(false);
            _configurationProvider.Replace(appSettings);
            FeatureManagementLog.AppSettingsRefreshed(_logger, appSettings.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            FeatureManagementLog.AppSettingsRefreshFailed(_logger, exception.GetType().Name);
        }
    }

    private sealed record BootstrapResult(int FeatureCount, int AppSettingsCount);
}
