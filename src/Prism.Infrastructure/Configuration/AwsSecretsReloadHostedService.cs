using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prism.Infrastructure.Configuration;

internal sealed class AwsSecretsReloadHostedService(
    AwsSecretsReloadState state,
    ILogger<AwsSecretsReloadHostedService> logger,
    TimeProvider? timeProvider = null,
    IAwsSecretStringReader? testReader = null) : BackgroundService
{
    // The container does not dispose a singleton registered as an instance, so the one service that uses it does.
    public override void Dispose()
    {
        state.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(state.Options.ReloadIntervalSeconds);
        using var timer = new PeriodicTimer(interval, timeProvider ?? TimeProvider.System);
        using var ownedReader = testReader is null ? new AwsSecretStringReader(state.Options, state.Environment) : null;
        IAwsSecretStringReader reader = testReader ?? ownedReader!;

        SecretsLog.RefreshActive(logger, state.Options.ReloadIntervalSeconds);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                await ReloadOnceAsync(reader, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal host shutdown.
        }
    }

    private async Task ReloadOnceAsync(IAwsSecretStringReader reader, CancellationToken cancellationToken)
    {
        try
        {
            bool changed = await state.ReloadAsync(reader, logger, cancellationToken).ConfigureAwait(false);
            if (!changed)
                SecretsLog.RefreshUnchanged(logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (AwsSecretsLoaderException ex)
        {
            // The loader's own messages carry secret ids, key names and AWS error codes only, so naming the failing
            // secret is safe and is what makes a refresh failure actionable.
            // Never attach the exception: SDK details can contain credential-bearing endpoint text.
            SecretsLog.RefreshFailed(logger, ex.Message);
        }
        catch (Exception ex)
        {
            SecretsLog.RefreshFailedUnexpectedly(logger, ex.GetType().Name);
        }
    }
}
