using System.Data.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Testing.Common;
using Prism.Tests.Integration.Metrics;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>The Data Through lookup against a real PostgreSQL holding tables shaped like the reporting tables it reads.</summary>
public sealed class DataThroughReaderTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<(DataThroughReader Reader, CountingWarehouse Warehouse, TestTimeProvider Clock)> NewReaderAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.trip_detail_snapshot, reporting.kpi_net_collections_ap__prov, reporting.forecast_cash_per_trip, reporting.kpi_clean_claim_rate__prov;
                CREATE TABLE reporting.trip_detail_snapshot (snapshot_date date NOT NULL, provider_id int NOT NULL);
                CREATE TABLE reporting.kpi_net_collections_ap__prov (snapshot_date date NOT NULL, provider_id int NOT NULL, is_current boolean NOT NULL);
                CREATE TABLE reporting.forecast_cash_per_trip (snapshot_date date NOT NULL, provider_id int NOT NULL, is_current boolean NOT NULL);
                CREATE TABLE reporting.kpi_clean_claim_rate__prov (snapshot_date date NOT NULL, provider_id int NOT NULL);

                -- Warehouse Provider 2274: trips loaded 2026-11-21 (and an older load), KPI current load 2026-11-19 (a newer one is not current), no forecast, clean claims 2026-04-07.
                INSERT INTO reporting.trip_detail_snapshot VALUES ('2026-11-21', 2274), ('2026-11-01', 2274), ('2026-11-30', 777);
                INSERT INTO reporting.kpi_net_collections_ap__prov VALUES ('2026-11-19', 2274, true), ('2026-11-20', 2274, false), ('2026-11-30', 777, true);
                INSERT INTO reporting.kpi_clean_claim_rate__prov VALUES ('2026-04-07', 2274);
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var counting = new CountingWarehouse(new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance));
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 11, 23, 8, 0, 0, TimeSpan.Zero));
        // Portal 2193 is warehouse Provider 2274, and 404 has no warehouse Provider.
        var reader = new DataThroughReader(counting, new FakeProviderResolver(new Dictionary<int, int> { [2193] = 2274, [404] = 0, [9] = 555 }), clock);
        return (reader, counting, clock);
    }

    [Fact]
    public async Task Each_source_gives_the_newest_load_date_of_its_own_table_for_the_providers_current_load()
    {
        (DataThroughReader reader, _, _) = await NewReaderAsync();
        using IDisposable disposeReader = reader;
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal(new DateOnly(2026, 11, 21), await reader.GetAsync(2193, [DataThroughSource.TripDetail], ct));
        Assert.Equal(new DateOnly(2026, 11, 19), await reader.GetAsync(2193, [DataThroughSource.NetCollectionsKpi], ct));
        Assert.Equal(new DateOnly(2026, 4, 7), await reader.GetAsync(2193, [DataThroughSource.CleanClaims], ct));
    }

    [Fact]
    public async Task A_provider_with_no_rows_in_the_table_or_no_warehouse_provider_has_no_data_through()
    {
        (DataThroughReader reader, CountingWarehouse warehouse, _) = await NewReaderAsync();
        using IDisposable disposeReader = reader;
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Null(await reader.GetAsync(2193, [DataThroughSource.CashForecast], ct));
        Assert.Null(await reader.GetAsync(9, [DataThroughSource.TripDetail], ct));
        int before = warehouse.Queries;
        Assert.Null(await reader.GetAsync(404, [DataThroughSource.TripDetail], ct));
        Assert.Equal(before, warehouse.Queries);
    }

    [Fact]
    public async Task Several_sources_give_the_oldest_date_and_none_if_any_source_has_no_data()
    {
        (DataThroughReader reader, _, _) = await NewReaderAsync();
        using IDisposable disposeReader = reader;
        CancellationToken ct = TestContext.Current.CancellationToken;

        Assert.Equal(new DateOnly(2026, 4, 7), await reader.GetAsync(2193, [DataThroughSource.TripDetail, DataThroughSource.CleanClaims], ct));
        Assert.Null(await reader.GetAsync(2193, [DataThroughSource.TripDetail, DataThroughSource.CashForecast], ct));
        Assert.Null(await reader.GetAsync(2193, [], ct));
    }

    [Fact]
    public async Task The_answer_is_kept_for_sixty_seconds_then_read_again()
    {
        (DataThroughReader reader, CountingWarehouse warehouse, TestTimeProvider clock) = await NewReaderAsync();
        using IDisposable disposeReader = reader;
        CancellationToken ct = TestContext.Current.CancellationToken;

        DateOnly? first = await reader.GetAsync(2193, [DataThroughSource.TripDetail], ct);
        clock.Advance(TimeSpan.FromSeconds(59));
        DateOnly? second = await reader.GetAsync(2193, [DataThroughSource.TripDetail], ct);
        Assert.Equal(1, warehouse.Queries);
        Assert.Equal(first, second);

        await using (var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString)))
        {
            await admin.OpenAsync(ct);
            await using var load = new NpgsqlCommand("INSERT INTO reporting.trip_detail_snapshot VALUES ('2026-11-23', 2274)", admin);
            await load.ExecuteNonQueryAsync(ct);
        }

        Assert.Equal(new DateOnly(2026, 11, 21), await reader.GetAsync(2193, [DataThroughSource.TripDetail], ct));
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(new DateOnly(2026, 11, 23), await reader.GetAsync(2193, [DataThroughSource.TripDetail], ct));
        Assert.Equal(2, warehouse.Queries);
    }

    private sealed class CountingWarehouse(IWarehouse inner) : IWarehouse
    {
        private int queries;

        public int Queries => Volatile.Read(ref queries);

        public Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref queries);
            return inner.QueryAsync(query, map, cancellationToken);
        }
    }
}
