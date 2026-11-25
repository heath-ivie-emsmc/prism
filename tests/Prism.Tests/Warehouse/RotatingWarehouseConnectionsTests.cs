using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;

namespace Prism.Tests.Warehouse;

/// <summary>
/// The rotation rules, with the read-only check replaced by a fake. Nothing here connects: the connection strings point at a name that cannot resolve
/// and no data source is ever opened. The real check against real roles is in the integration project.
/// </summary>
public sealed class RotatingWarehouseConnectionsTests
{
    private const string Start = "Host=dw.invalid;Database=wh;Username=ro;Password=startup-secret;SSL Mode=VerifyFull";
    private const string Rotated = "Host=dw.invalid;Database=wh;Username=ro2;Password=rotated-secret;SSL Mode=VerifyFull";

    private sealed class Harness : IDisposable
    {
        public string Wanted { get; set; } = Start;

        public List<string> Verified { get; } = [];

        public Func<Exception?> Outcome { get; set; } = () => null;

        public FakeTimeProvider Clock { get; } = new();

        public RotatingWarehouseConnections Connections { get; }

        public NpgsqlDataSource StartupSource { get; }

        public Harness(bool deployed = true, ILogger? logger = null)
        {
            var options = new PrismWarehouseOptions { ConnectionString = Start };
            StartupSource = WarehouseDataSourceFactory.Create(options);
            Connections = new RotatingWarehouseConnections(
                () => Wanted,
                options,
                StartupSource,
                deployed,
                (_, candidate, _) =>
                {
                    Verified.Add(candidate.ConnectionString);
                    Exception? failure = Outcome();
                    return failure is null ? Task.CompletedTask : Task.FromException(failure);
                },
                Clock,
                logger ?? NullLogger.Instance);
        }

        public void Dispose() => Connections.Dispose();
    }

    [Fact]
    public async Task An_unchanged_connection_string_is_not_checked_again()
    {
        using var harness = new Harness();

        using WarehouseDataSourceLease lease = await harness.Connections.AcquireAsync(TestContext.Current.CancellationToken);

        Assert.Same(harness.StartupSource, lease.DataSource);
        Assert.Empty(harness.Verified);
    }

    [Fact]
    public async Task A_changed_connection_string_is_checked_once_and_then_used()
    {
        using var harness = new Harness { Wanted = Rotated };

        using (WarehouseDataSourceLease first = await harness.Connections.AcquireAsync(TestContext.Current.CancellationToken))
        {
            Assert.NotSame(harness.StartupSource, first.DataSource);
        }

        using WarehouseDataSourceLease second = await harness.Connections.AcquireAsync(TestContext.Current.CancellationToken);

        Assert.Equal([Rotated], harness.Verified);
        Assert.Equal("ro2", new NpgsqlConnectionStringBuilder(second.DataSource.ConnectionString).Username);
    }

    [Fact]
    public async Task A_changed_connection_string_that_fails_the_check_is_refused_and_remembered_for_a_while()
    {
        using var harness = new Harness { Wanted = Rotated, Outcome = () => new WarehouseWriteProtectionException("can write") };
        CancellationToken ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => harness.Connections.AcquireAsync(ct));
        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => harness.Connections.AcquireAsync(ct));
        Assert.Single(harness.Verified);

        // After the memory lapses it is checked again, and a fixed login is accepted.
        harness.Clock.Advance(RotatingWarehouseConnections.RefusalMemory + TimeSpan.FromSeconds(1));
        harness.Outcome = () => null;
        using WarehouseDataSourceLease lease = await harness.Connections.AcquireAsync(ct);

        Assert.Equal(2, harness.Verified.Count);
        Assert.NotSame(harness.StartupSource, lease.DataSource);
    }

    [Fact]
    public async Task A_refused_login_never_falls_back_to_the_old_pool()
    {
        using var harness = new Harness { Wanted = Rotated, Outcome = () => new InvalidOperationException("cannot check") };

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => harness.Connections.AcquireAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Host=dw.invalid;Database=wh;Username=ro;Password=p;SSL Mode=Disable")]
    public async Task A_changed_connection_string_that_breaks_the_rules_is_refused_without_a_check(string wanted)
    {
        using var harness = new Harness(deployed: true) { Wanted = wanted };

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => harness.Connections.AcquireAsync(TestContext.Current.CancellationToken));

        Assert.Empty(harness.Verified);
    }

    [Fact]
    public async Task A_refusal_is_logged_without_the_connection_string_or_the_failure_message()
    {
        var logger = new CapturingLogger();
        using var harness = new Harness(logger: logger)
        {
            Wanted = Rotated,
            Outcome = () => new InvalidOperationException("password=rotated-secret leaked"),
        };

        await Assert.ThrowsAsync<WarehouseUnavailableException>(() => harness.Connections.AcquireAsync(TestContext.Current.CancellationToken));

        Assert.NotEmpty(logger.Messages);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("rotated-secret", StringComparison.Ordinal) || message.Contains("startup-secret", StringComparison.Ordinal));
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
            if (exception is not null)
            {
                Messages.Add(exception.ToString());
            }
        }
    }
}
