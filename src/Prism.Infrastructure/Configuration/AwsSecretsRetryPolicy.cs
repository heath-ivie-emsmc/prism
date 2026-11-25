namespace Prism.Infrastructure.Configuration;

/// <summary>
/// Generic (no AWS dependency) bounded-retry-with-full-jitter executor: 4 attempts, 500 ms base, factor 2,
/// 4 s cap. Retries only when the <c>isTransient</c> callback passed to <see cref="ExecuteAsync{T}"/> says so;
/// any other exception, or exhausting attempt 4, propagates immediately. Kept generic and separate from
/// <see cref="AwsSecretStringReader"/> so it is unit-testable without a real AWS client.
/// </summary>
internal static class AwsSecretsRetryPolicy
{
    internal const int MaxAttempts = 4;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(4);
    private const double BackoffFactor = 2;

    internal static Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(isTransient);
        ArgumentNullException.ThrowIfNull(timeProvider);

        return ExecuteValidatedAsync(operation, isTransient, timeProvider, cancellationToken);
    }

    private static async Task<T> ExecuteValidatedAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        Func<Exception, bool> isTransient,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < MaxAttempts && isTransient(ex))
            {
                var delay = ComputeDelayWithFullJitter(attempt);
                await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }

        // Unreachable: attempt == MaxAttempts always either returns or lets the exception propagate
        // (the `when` guard above only retries while attempt < MaxAttempts).
        throw new InvalidOperationException("Retry policy exhausted without a captured exception.");
    }

    private static TimeSpan ComputeDelayWithFullJitter(int attempt)
    {
        var exponential = BaseDelay * Math.Pow(BackoffFactor, attempt - 1);
        var capped = exponential > MaxDelay ? MaxDelay : exponential;
        var jitterMilliseconds = Random.Shared.NextDouble() * capped.TotalMilliseconds;
        return TimeSpan.FromMilliseconds(jitterMilliseconds);
    }
}
