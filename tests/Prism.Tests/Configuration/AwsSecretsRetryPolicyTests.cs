using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace Prism.Tests.Configuration;

public sealed class AwsSecretsRetryPolicyTests
{
    [Fact]
    public void ExecuteAsync_NullOperation_ThrowsSynchronously()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _ = AwsSecretsRetryPolicy.ExecuteAsync<string>(
                null!, _ => true, TimeProvider.System, TestContext.Current.CancellationToken);
        });
    }

    [Fact]
    public async Task ExecuteAsync_SucceedsFirstTry_NeverDelays()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;

        var result = await AwsSecretsRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult("ok");
            },
            _ => true,
            timeProvider,
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientException_DoesNotRetry()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;

        var task = AwsSecretsRetryPolicy.ExecuteAsync<string>(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException("permanent");
            },
            _ => false,
            timeProvider,
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailures_RetriesThenSucceeds()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;

        Task<string> Run() => AwsSecretsRetryPolicy.ExecuteAsync(
            _ =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("transient");
                return Task.FromResult("ok");
            },
            _ => true,
            timeProvider,
            TestContext.Current.CancellationToken);

        Task<string> resultTask = Run();
        await AdvanceUntilCompleteAsync(timeProvider, resultTask);

        Assert.Equal("ok", await resultTask);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureEveryAttempt_ExhaustsRetriesThenThrows()
    {
        var timeProvider = new FakeTimeProvider();
        var attempts = 0;

        Task<string> resultTask = AwsSecretsRetryPolicy.ExecuteAsync<string>(
            _ =>
            {
                attempts++;
                throw new InvalidOperationException($"attempt-{attempts}");
            },
            _ => true,
            timeProvider,
            TestContext.Current.CancellationToken);

        await AdvanceUntilCompleteAsync(timeProvider, resultTask);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resultTask);
        Assert.Equal(AwsSecretsRetryPolicy.MaxAttempts, attempts);
        Assert.Equal($"attempt-{AwsSecretsRetryPolicy.MaxAttempts}", ex.Message);
    }

    /// <summary>
    /// Drives a <see cref="FakeTimeProvider"/> forward in small steps until the operation under test completes,
    /// so retry-with-delay tests run instantly instead of waiting on real 500 ms-4 s backoff delays.
    /// </summary>
    private static async Task AdvanceUntilCompleteAsync(FakeTimeProvider timeProvider, Task task)
    {
        for (var i = 0; i < 100 && !task.IsCompleted; i++)
        {
            timeProvider.Advance(TimeSpan.FromSeconds(5));
            await Task.WhenAny(task, Task.Delay(10, TestContext.Current.CancellationToken));
        }
    }
}
