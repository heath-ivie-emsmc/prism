using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;

namespace Prism.Tests.FeatureManagement;

public sealed class FeatureManagementReadinessTests
{
    private sealed class FakeProbe(bool reachable) : IAppConfigSidecarConnectivityProbe
    {
        public Task<bool> IsReachableAsync(FeatureManagementOptions options, CancellationToken cancellationToken) => Task.FromResult(reachable);
    }

    private static SidecarState StateWith(int features)
    {
        var state = new SidecarState();
        ImmutableDictionary<string, FeatureValue>.Builder values = ImmutableDictionary.CreateBuilder<string, FeatureValue>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < features; i++)
        {
            values[$"f{i}"] = new FeatureValue { FeatureName = $"f{i}", Enabled = true, Settings = new Dictionary<string, JsonElement>() };
        }

        state.Replace(new SidecarState.Snapshot(DateTimeOffset.UnixEpoch, values.ToImmutable()));
        return state;
    }

    private static FeatureManagementReadiness Readiness(
        FeatureManagementOptions options,
        FeatureManagementBootstrapState bootstrap,
        bool reachable = true,
        SidecarState? state = null,
        ILogger<FeatureManagementReadiness>? logger = null) =>
        new(new TestOptionsMonitor<FeatureManagementOptions>(options), bootstrap, logger ?? NullLogger<FeatureManagementReadiness>.Instance, new FakeProbe(reachable), state);

    private static FeatureManagementBootstrapState Completed()
    {
        var bootstrap = new FeatureManagementBootstrapState();
        bootstrap.MarkBootstrapComplete();
        return bootstrap;
    }

    [Fact]
    public async Task A_disabled_sidecar_is_always_ready_because_flags_come_from_settings()
    {
        FeatureManagementReadiness readiness = Readiness(new FeatureManagementOptions { Enabled = false }, new FeatureManagementBootstrapState(), reachable: false);

        Assert.True(await readiness.IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_enabled_sidecar_is_not_ready_until_its_first_load_finishes()
    {
        FeatureManagementReadiness readiness = Readiness(new FeatureManagementOptions { Enabled = true }, new FeatureManagementBootstrapState(), state: StateWith(1));

        Assert.False(await readiness.IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_enabled_sidecar_that_stops_answering_is_not_ready()
    {
        FeatureManagementReadiness readiness = Readiness(new FeatureManagementOptions { Enabled = true }, Completed(), reachable: false, state: StateWith(1));

        Assert.False(await readiness.IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_empty_flag_profile_is_not_ready_unless_the_options_allow_it()
    {
        var strict = new FeatureManagementOptions { Enabled = true, RequireNonEmptyFeatureConfig = true };
        var lenient = new FeatureManagementOptions { Enabled = true, RequireNonEmptyFeatureConfig = false };

        Assert.False(await Readiness(strict, Completed(), state: StateWith(0)).IsReadyAsync(TestContext.Current.CancellationToken));
        Assert.True(await Readiness(lenient, Completed(), state: StateWith(0)).IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_loaded_reachable_sidecar_with_flags_is_ready()
    {
        FeatureManagementReadiness readiness = Readiness(new FeatureManagementOptions { Enabled = true }, Completed(), state: StateWith(3));

        Assert.True(await readiness.IsReadyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_not_ready_answer_is_logged_as_a_fixed_reason_only()
    {
        var logger = new ListLogger();
        FeatureManagementReadiness readiness = Readiness(new FeatureManagementOptions { Enabled = true, BaseUrl = "http://localhost:2772" }, Completed(), reachable: false, state: StateWith(1), logger: logger);

        Assert.False(await readiness.IsReadyAsync(TestContext.Current.CancellationToken));

        string line = Assert.Single(logger.Lines);
        Assert.Contains("sidecar-unreachable", line, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", line, StringComparison.Ordinal);
    }

    private sealed class ListLogger : ILogger<FeatureManagementReadiness>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception));
    }
}
