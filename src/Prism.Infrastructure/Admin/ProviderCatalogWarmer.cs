using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Prism.Application.Admin;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Admin;

/// <summary>
/// Reads the Provider list in the background when the host starts and again before it expires, so a staff search never waits for it.
/// A failure is logged and tried again at the next turn; searches keep the old list meanwhile.
/// </summary>
public sealed partial class ProviderCatalogWarmer(IProviderListSource source, ProviderCatalogCache cache, TimeProvider timeProvider, ILogger<ProviderCatalogWarmer> logger) : BackgroundService
{
    /// <summary>Shorter than the cache's life, so the list is replaced before it runs out.</summary>
    internal static readonly TimeSpan Every = TimeSpan.FromMinutes(45);

    internal static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait = await RefreshOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(wait, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    internal async Task<TimeSpan> RefreshOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await cache.RefreshAsync(source.ListAsync, cancellationToken);
            return Every;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Anything at all: the warehouse not reachable or not configured (a test host). Never stops the host.
            LogRefreshFailed(logger, ex.GetType().Name);
            return RetryAfterFailure;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Provider list could not be read for the Provider search ({ExceptionType}). It will be tried again.")]
    private static partial void LogRefreshFailed(ILogger logger, string exceptionType);
}
