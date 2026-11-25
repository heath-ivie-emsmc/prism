using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Prism.Api.Health;
using Prism.Application.FeatureManagement;
using Prism.Infrastructure.FeatureManagement;
using Prism.Infrastructure.Persistence;
using Prism.Testing.Common;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Persistence;

/// <summary>Feature flags as part of the whole host: composition, settings-file flags, startup validation and what <c>/ready</c> reports.</summary>
public sealed class FeatureManagementWiringTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private sealed class AlwaysUp : IDatabaseProbe
    {
        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(true);
    }

    private sealed class FakeReadiness(bool ready) : IFeatureManagementReadiness
    {
        public int Calls { get; private set; }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ready);
        }
    }

    [Fact]
    public async Task Readiness_includes_feature_management_and_shares_one_check_between_callers()
    {
        var features = new FakeReadiness(ready: false);
        var check = new ReadinessCheck(new AlwaysUp(), new TestTimeProvider(Start), features);

        bool[] results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => check.IsReadyAsync()));

        Assert.All(results, Assert.False);
        Assert.Equal(1, features.Calls);
    }

    [Fact]
    public async Task Readiness_is_ready_when_the_database_and_feature_management_are()
    {
        var check = new ReadinessCheck(new AlwaysUp(), new TestTimeProvider(Start), new FakeReadiness(ready: true));

        Assert.True(await check.IsReadyAsync());
    }

    [Fact]
    public async Task The_host_reads_flags_from_settings_when_the_sidecar_is_off()
    {
        using var factory = new AuthTestFactory(settings: new Dictionary<string, string?> { ["FeatureFlags:Pilot"] = "true", ["FeatureFlags:Retired"] = "false" });
        factory.CreateClient().Dispose();
        IFeatureValueManager flags = factory.Services.GetRequiredService<IFeatureValueManager>();
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.IsType<ConfigurationFeatureValueManager>(flags);
        Assert.True(await flags.IsEnabledAsync("Pilot", ct));
        Assert.False(await flags.IsEnabledAsync("Retired", ct));
        Assert.False(await flags.IsEnabledAsync("NeverDefined", ct));
    }

    [Fact]
    public void An_enabled_sidecar_with_no_application_or_environment_stops_the_host()
    {
        using var factory = new AuthTestFactory(settings: new Dictionary<string, string?> { ["FeatureManagement:Enabled"] = "true" });

        Exception ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient().Dispose());

        Assert.Contains("FeatureManagement", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_sidecar_address_that_would_cross_a_network_in_plain_text_stops_the_host()
    {
        using var factory = new AuthTestFactory(settings: new Dictionary<string, string?>
        {
            ["FeatureManagement:Enabled"] = "true",
            ["FeatureManagement:Application"] = "prism-test",
            ["FeatureManagement:Environment"] = "test",
            ["FeatureManagement:EnableDynamicAppSettings"] = "false",
            ["FeatureManagement:BaseUrl"] = "http://appconfig.internal:2772",
        });

        Exception ex = Assert.ThrowsAny<Exception>(() => factory.CreateClient().Dispose());

        Assert.Contains("BaseUrl", ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_options_are_bound_so_a_deployed_file_can_name_the_application_and_environment()
    {
        using var factory = new AuthTestFactory(settings: new Dictionary<string, string?>
        {
            ["FeatureManagement:Application"] = "prism-test",
            ["FeatureManagement:Environment"] = "test",
        });
        factory.CreateClient().Dispose();

        FeatureManagementOptions options = factory.Services.GetRequiredService<IOptions<FeatureManagementOptions>>().Value;

        Assert.False(options.Enabled);
        Assert.Equal("prism-test", options.Application);
        Assert.Equal("test", options.Environment);
    }
}
