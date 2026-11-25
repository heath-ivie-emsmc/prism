using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Prism.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;

namespace Prism.Tests.Configuration;

public sealed class AwsSecretsReloadTests
{
    [Fact]
    public async Task Reload_ReplacesOneProvider_OnlyNotifiesOnChange_AndPreservesHigherOverrides()
    {
        using var configuration = new ConfigurationManager();
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, Raw("first", "Admin:Key"));
        configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Admin:Key"] = "break-glass" });
        int providers = ((IConfigurationRoot)configuration).Providers.Count();
        int notifications = 0;
        using IDisposable subscription = ChangeToken.OnChange(((IConfiguration)configuration).GetReloadToken, () => notifications++);

        Assert.False(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        reader.SetSecretString("first", "rotated");
        Assert.True(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.False(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));

        Assert.Equal("break-glass", configuration["Admin:Key"]);
        Assert.Equal(1, notifications);
        Assert.Equal(providers, ((IConfigurationRoot)configuration).Providers.Count());
    }

    [Fact]
    public async Task Reload_RefreshesEntriesSkippedAtStartup()
    {
        using var configuration = new ConfigurationManager();
        configuration["Admin:Key"] = "legacy-env";
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "new");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, Raw("first", "Admin:Key"));
        Assert.Empty(reader.Calls);
        await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.Equal("new", configuration["Admin:Key"]);
        Assert.Single(reader.Calls);
    }

    [Fact]
    public async Task Reload_LogsOnce_WhenStartupSkippedEntryIsSupersededByAws()
    {
        using var configuration = new ConfigurationManager();
        configuration["Admin:Key"] = "legacy-env";
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "legacy-env");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, Raw("first", "Admin:Key"));
        var logger = new RecordingLogger();

        // Same value as the kept one: the first refresh publishes the entry into the provider (its keys were
        // deliberately absent at startup), but nothing has been superseded, so no precedence-flip line.
        await state.ReloadAsync(reader, logger, TestContext.Current.CancellationToken);
        Assert.Equal("legacy-env", configuration["Admin:Key"]);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("skipped at startup", StringComparison.Ordinal));

        reader.SetSecretString("first", "rotated");
        Assert.True(await state.ReloadAsync(reader, logger, TestContext.Current.CancellationToken));
        Assert.Equal("rotated", configuration["Admin:Key"]);
        string line = Assert.Single(logger.Messages, m => m.Contains("skipped at startup", StringComparison.Ordinal));
        Assert.Contains("'first'", line, StringComparison.Ordinal);
        Assert.DoesNotContain("rotated", line, StringComparison.Ordinal);
        Assert.DoesNotContain("legacy-env", line, StringComparison.Ordinal);

        reader.SetSecretString("first", "rotated-again");
        Assert.True(await state.ReloadAsync(reader, logger, TestContext.Current.CancellationToken));
        Assert.Single(logger.Messages, m => m.Contains("skipped at startup", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reload_UsesActualPublishedSnapshot_WhenSkippedStartupEntryOverlapsFetchedEntry()
    {
        using var configuration = new ConfigurationManager();
        configuration["Admin:Key"] = "lower";
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "{\"a\":\"fetched\",\"b\":\"other\"}");
        reader.SetSecretString("last", "lower");
        var first = new AwsSecretsLoaderEntry
        {
            SecretId = "first",
            Keys = new() { ["Admin:Key"] = "a", ["Admin:Other"] = "b" },
        };
        AwsSecretsReloadState state = await StartAsync(configuration, reader, first, Raw("last", "Admin:Key"));
        Assert.Equal("fetched", configuration["Admin:Key"]);
        Assert.True(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.Equal("lower", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task Reload_AdvancesHiddenEntryState_WhenEffectiveValueIsUnchanged()
    {
        using var configuration = new ConfigurationManager();
        var reader = new FakeAwsSecretStringReader();
        var first = Raw("first", "Admin:Key");
        first.Optional = true;
        var last = new AwsSecretsLoaderEntry { SecretId = "last", AllowedKeys = ["Admin:Key"] };
        reader.SetSecretString("first", "old");
        reader.SetSecretString("last", "{\"Admin:Key\":\"winner\"}");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, first, last);

        reader.SetSecretString("first", "hidden-new");
        Assert.False(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        reader.SetException("first", new ResourceNotFoundException("sensitive exception text"));
        reader.SetSecretString("last", "{}");
        Assert.True(await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.Equal("hidden-new", configuration["Admin:Key"]);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"key\":null}")]
    [InlineData("{\"key\":\"\"}")]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Reload_InvalidRequiredEntry_RetainsEntireSet(string payload)
    {
        using var configuration = new ConfigurationManager();
        var reader = new FakeAwsSecretStringReader();
        var second = new AwsSecretsLoaderEntry { SecretId = "second", Keys = new() { ["Admin:Other"] = "key" } };
        reader.SetSecretString("first", "initial");
        reader.SetSecretString("second", "{\"key\":\"initial-other\"}");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, Raw("first", "Admin:Key"), second);
        reader.SetSecretString("first", "new");
        reader.SetSecretString("second", payload);

        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() =>
            state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken));
        Assert.Equal("initial", configuration["Admin:Key"]);
        Assert.Equal("initial-other", configuration["Admin:Other"]);
    }

    [Fact]
    public async Task Reload_RemovesAbsentAllowedProperty_AndExposesLowerLayer()
    {
        using var configuration = new ConfigurationManager();
        configuration["Admin:Key"] = "lower";
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "{\"Admin:Key\":\"loaded\"}");
        var entry = new AwsSecretsLoaderEntry { SecretId = "first", AllowedKeys = ["Admin:Key", "Admin:Other"] };
        AwsSecretsReloadState state = await StartAsync(configuration, reader, entry);
        reader.SetSecretString("first", "{}");
        await state.ReloadAsync(reader, NullLogger.Instance, TestContext.Current.CancellationToken);
        Assert.Equal("lower", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task Reload_CallbackFailure_IsPublished_NotReportedAsRollback_AndNotRepeated()
    {
        using var configuration = new ConfigurationManager();
        var reader = new FakeAwsSecretStringReader();
        reader.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, reader, Raw("first", "Admin:Key"));
        var logger = new RecordingLogger();
        using IDisposable subscription = ((IConfiguration)configuration).GetReloadToken().RegisterChangeCallback(
            _ => throw new InvalidOperationException("secret-callback-value"), null);
        reader.SetSecretString("first", "new");
        Assert.True(await state.ReloadAsync(reader, logger, TestContext.Current.CancellationToken));
        Assert.Equal("new", configuration["Admin:Key"]);
        Assert.False(await state.ReloadAsync(reader, logger, TestContext.Current.CancellationToken));
        Assert.Contains(logger.Messages, message => message.Contains("was not rolled back", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, message => message.Contains("secret-callback-value", StringComparison.Ordinal));
        Assert.All(logger.Exceptions, Assert.Null);
    }

    [Fact]
    public async Task Reload_CancelledAfterFetch_DoesNotPublish()
    {
        using var configuration = new ConfigurationManager();
        var initial = new FakeAwsSecretStringReader();
        initial.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, initial, Raw("first", "Admin:Key"));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var reader = new DelegateReader(_ => { cancellation.Cancel(); return Task.FromResult<string?>("new"); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => state.ReloadAsync(reader, NullLogger.Instance, cancellation.Token));
        Assert.Equal("initial", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task Reload_Timeout_DoesNotPublish()
    {
        using var configuration = new ConfigurationManager();
        var initial = new FakeAwsSecretStringReader();
        initial.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, initial, Raw("first", "Admin:Key"));
        var reader = new DelegateReader(async ct => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return "new"; });
        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() => state.ReloadAsync(
            reader, NullLogger.Instance, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(30)));
        Assert.Equal("initial", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task Reload_ReaderReturnsAfterBudgetWithoutObservingCancellation_DoesNotPublish()
    {
        using var configuration = new ConfigurationManager();
        var initial = new FakeAwsSecretStringReader();
        initial.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, initial, Raw("first", "Admin:Key"));
        // Returns only once the budget has lapsed, so the order does not depend on how promptly a loaded machine
        // fires the budget timer. The reader still ignores the cancellation: it hands back a value instead of throwing.
        var reader = new DelegateReader(async budgetToken =>
        {
            var budgetLapsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using (budgetToken.Register(() => budgetLapsed.TrySetResult()))
            {
                await budgetLapsed.Task.WaitAsync(TestContext.Current.CancellationToken);
            }

            return "late";
        });
        await Assert.ThrowsAsync<AwsSecretsLoaderException>(() => state.ReloadAsync(
            reader, NullLogger.Instance, TestContext.Current.CancellationToken, TimeSpan.FromMilliseconds(1)));
        Assert.Equal("initial", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task HostedReload_WaitsAnHour_DoesNotOverlap_AndCancelsOnShutdown()
    {
        using var configuration = new ConfigurationManager();
        var initial = new FakeAwsSecretStringReader();
        initial.SetSecretString("first", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, initial, Raw("first", "Admin:Key"));
        var clock = new FakeTimeProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var reader = new DelegateReader(async ct =>
        {
            Interlocked.Increment(ref calls);
            started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally { cancelled.TrySetResult(); }
            return null;
        });
        var timerReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new AwsSecretsReloadHostedService(state, new StartSignalLogger(timerReady), clock, reader);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await timerReady.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromMinutes(59));
        Assert.Equal(0, Volatile.Read(ref calls));
        clock.Advance(TimeSpan.FromMinutes(1));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(1, Volatile.Read(ref calls));
        await service.StopAsync(TestContext.Current.CancellationToken);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal("initial", configuration["Admin:Key"]);
    }

    [Fact]
    public async Task HostedReload_FetchFailure_LogsErrorNamingSecretAndErrorCode_AndKeepsLastKnownGood()
    {
        using var configuration = new ConfigurationManager();
        var initial = new FakeAwsSecretStringReader();
        initial.SetSecretString("prism/prod/admin-api", "initial");
        AwsSecretsReloadState state = await StartAsync(configuration, initial, Raw("prism/prod/admin-api", "Admin:Key"));
        var clock = new FakeTimeProvider();
        var failing = new FakeAwsSecretStringReader();
        failing.SetException("prism/prod/admin-api",
            new AmazonSecretsManagerException("kms-detail-must-not-leak") { ErrorCode = "DecryptionFailure" });
        var logger = new FailureSignalLogger();
        using var service = new AwsSecretsReloadHostedService(state, logger, clock, failing);
        await service.StartAsync(TestContext.Current.CancellationToken);
        await logger.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        clock.Advance(TimeSpan.FromHours(1));
        string failure = await logger.Failed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);

        Assert.Contains("AWS Secrets Manager loader: FAILED hourly refresh", failure, StringComparison.Ordinal);
        Assert.Contains("'prism/prod/admin-api'", failure, StringComparison.Ordinal);
        Assert.Contains("DecryptionFailure", failure, StringComparison.Ordinal);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("kms-detail-must-not-leak", StringComparison.Ordinal));
        Assert.Equal("initial", configuration["Admin:Key"]);
    }

    private static AwsSecretsLoaderEntry Raw(string id, string key) => new()
    {
        SecretId = id,
        Format = AwsSecretFormat.Raw,
        TargetKey = key,
    };

    private static async Task<AwsSecretsReloadState> StartAsync(
        ConfigurationManager configuration, IAwsSecretStringReader reader, params AwsSecretsLoaderEntry[] entries)
    {
        await AwsSecretsLoaderExtensions.AddAwsSecretsLoaderCoreAsync(configuration,
            new AwsSecretsLoaderOptions { Enabled = true, Secrets = [.. entries] }, reader,
            NullLogger.Instance, TestContext.Current.CancellationToken, environment: new TestEnvironment());
        var services = new ServiceCollection();
        services.AddAwsSecretsLoaderReloading(configuration);
        return (AwsSecretsReloadState)Assert.Single(services, s => s.ServiceType == typeof(AwsSecretsReloadState)).ImplementationInstance!;
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "ReloadTests";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class DelegateReader(Func<CancellationToken, Task<string?>> read) : IAwsSecretStringReader
    {
        public Task<string?> GetSecretStringAsync(string secretId, CancellationToken cancellationToken) => read(cancellationToken);
    }

    private sealed class StartSignalLogger(TaskCompletionSource ready) : ILogger<AwsSecretsReloadHostedService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter(state, exception).Contains("refresh is active", StringComparison.Ordinal))
                ready.TrySetResult();
        }
    }

    private sealed class FailureSignalLogger : ILogger<AwsSecretsReloadHostedService>
    {
        private readonly Lock _gate = new();
        private readonly List<string> _messages = [];
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<string> Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<string> Messages
        {
            get { lock (_gate) return [.. _messages]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            lock (_gate) _messages.Add(message);
            if (message.Contains("refresh is active", StringComparison.Ordinal))
                Ready.TrySetResult();
            if (logLevel == LogLevel.Error && message.Contains("FAILED hourly refresh", StringComparison.Ordinal))
                Failed.TrySetResult(message);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];
        public List<Exception?> Exceptions { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            Exceptions.Add(exception);
        }
    }
}
