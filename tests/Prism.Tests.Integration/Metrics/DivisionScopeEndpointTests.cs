using System.Data.Common;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Metrics;

/// <summary>
/// Tenant scope for every route that honors the <c>division</c> filter, driven by the Metric Catalog so a new division-aware Metric cannot be added without a route here:
/// a user of Provider A asking for Provider B, and a Facility-restricted user asking for a sibling division, are refused with 404 before the division is
/// looked up and before the warehouse is touched.
/// </summary>
public sealed class DivisionScopeEndpointTests : IDisposable
{
    private const string Root = "/api/v1/providers";
    private const string Tail = "metrics/net-collections";

    // The route of each Metric that lists the division dimension, by catalog key. The divisions route has no catalog entry and is listed apart.
    private static readonly Dictionary<string, string> RouteByKey = new(StringComparer.Ordinal)
    {
        ["net_cash_breakdown"] = "breakdown",
        ["net_cash_by_group"] = "by-group",
        ["cash_per_transport_by_payer"] = "cash-per-transport",
        ["payment_age"] = "payment-age",
        ["payer_mix"] = "payer-mix",
        ["payer_mix_detail"] = "payer-mix/commercial",
        ["transport_volume"] = "transports",
    };

    private readonly CountingDirectory directory = new();
    private readonly CountingWarehouse warehouse = new();
    private readonly AuthTestFactory factory;

    public DivisionScopeEndpointTests()
    {
        factory = new AuthTestFactory(configureServices: services =>
        {
            services.RemoveAll<IProviderDivisionDirectory>();
            services.AddSingleton<IProviderDivisionDirectory>(directory);
            services.RemoveAll<IWarehouse>();
            services.AddSingleton<IWarehouse>(warehouse);
        });
        // Provider 100 in full, 200 limited to Facility 9 (a sibling of 10), 400 limited to one Facility. 101 and 999 are not assigned to the user.
        factory.Directory.Providers.Clear();
        factory.Directory.Providers.AddRange([new ProviderScope("100", "Mercy Air", []), new ProviderScope("200", "Limited", ["9"]), new ProviderScope("400", "Limited too", ["9"])]);
    }

    public void Dispose() => factory.Dispose();

    public static TheoryData<string> DivisionRoutes()
    {
        var data = new TheoryData<string>();
        foreach (string route in RouteByKey.Values.Append("divisions"))
        {
            data.Add(route);
        }

        return data;
    }

    [Fact]
    public void Every_metric_that_lists_the_division_dimension_has_a_route_in_this_table()
    {
        string[] withDivision = [.. MetricCatalog.Entries.Where(e => e.Dimensions.Contains(MetricDimensions.Division)).Select(e => e.Key).Order(StringComparer.Ordinal)];

        Assert.Equal([.. RouteByKey.Keys.Order(StringComparer.Ordinal)], withDivision);
    }

    private async Task<HttpStatusCode> StatusAsync(string url)
    {
        using CookieClient client = factory.NewSignedInClient();
        using HttpResponseMessage response = await client.GetAsync(url);
        return response.StatusCode;
    }

    [Theory]
    [MemberData(nameof(DivisionRoutes))]
    public async Task A_user_of_one_provider_asking_for_another_providers_data_with_a_division_is_a_404_and_nothing_is_read(string route)
    {
        foreach (string provider in new[] { "101", "999" })
        {
            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"{Root}/{provider}/{Tail}/{route}?division=rotor-base"));
            Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"{Root}/{provider}/{Tail}/{route}"));
        }

        Assert.Equal((0, 0), (directory.DivisionLookups, warehouse.Calls));
    }

    [Theory]
    [MemberData(nameof(DivisionRoutes))]
    public async Task A_facility_restricted_user_asking_for_a_sibling_division_or_their_own_is_a_404_and_nothing_is_read(string route)
    {
        foreach (string provider in new[] { "200", "400" })
        {
            foreach (string division in new[] { "fixed-base", "rotor-base" })
            {
                Assert.Equal(HttpStatusCode.NotFound, await StatusAsync($"{Root}/{provider}/{Tail}/{route}?division={division}"));
            }
        }

        Assert.Equal((0, 0), (directory.DivisionLookups, warehouse.Calls));
    }

    [Theory]
    [MemberData(nameof(DivisionRoutes))]
    public async Task The_same_request_for_the_providers_own_data_is_not_refused_by_scope_so_the_404s_above_are_the_scope_check(string route)
    {
        HttpStatusCode status = await StatusAsync($"{Root}/100/{Tail}/{route}?division=rotor-base");

        Assert.NotEqual(HttpStatusCode.NotFound, status);
        Assert.NotEqual(HttpStatusCode.Forbidden, status);
    }

    private sealed class CountingDirectory : IProviderDivisionDirectory
    {
        private int lookups;

        public int DivisionLookups => Volatile.Read(ref lookups);

        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId is 200 or 400 ? new HashSet<string> { "9", "10" } : []);

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref lookups);
            return Task.FromResult<IReadOnlyList<ReportingDivision>>([new("fixed-base", "Mercy Flights - Fixed"), new("rotor-base", "Mercy Flights - Rotor")]);
        }
    }

    private sealed class CountingWarehouse : IWarehouse
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult<IReadOnlyList<T>>([]);
        }
    }
}
