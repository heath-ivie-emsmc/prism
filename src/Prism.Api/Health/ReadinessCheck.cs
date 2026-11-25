using Prism.Infrastructure.FeatureManagement;
using Prism.Infrastructure.Persistence;

namespace Prism.Api.Health;

/// <summary>
/// Backs the anonymous <c>/ready</c> endpoint. Callers share one check (the database, and feature management when the App Config sidecar is on),
/// and its result is reused for a short time, so a flood of anonymous requests costs at most one attempt per window.
/// </summary>
public sealed class ReadinessCheck(IDatabaseProbe probe, TimeProvider timeProvider, IFeatureManagementReadiness? featureManagement = null)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);

    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    private readonly Lock gate = new();
    private Task<bool>? current;
    private DateTimeOffset startedAt;

    public Task<bool> IsReadyAsync()
    {
        lock (gate)
        {
            DateTimeOffset now = timeProvider.GetUtcNow();
            if (current is not null && (!current.IsCompleted || now - startedAt < CacheFor))
            {
                return current;
            }

            startedAt = now;
            current = RunAsync();
            return current;
        }
    }

    // Not tied to any caller's request: one caller disconnecting must not fail the check the others are waiting on.
    private async Task<bool> RunAsync()
    {
        using var timeout = new CancellationTokenSource(Timeout, timeProvider);
        try
        {
            return await probe.IsAvailableAsync(timeout.Token)
                && (featureManagement is null || await featureManagement.IsReadyAsync(timeout.Token));
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
