using Prism.Application.Warehouse;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Metrics;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>A Provider's portal Facility ids against a real PostgreSQL holding tables shaped like the <c>ods</c> division tables.</summary>
public sealed class ProviderDivisionDirectoryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<ProviderDivisionDirectory> NewDirectoryAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(postgres.ConnectionString);
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS ods;
                DROP TABLE IF EXISTS ods.provider_reporting_division, ods.location;
                CREATE TABLE ods.location (id int PRIMARY KEY, integration_id bigint);
                CREATE TABLE ods.provider_reporting_division (id int PRIMARY KEY, provider_id int NOT NULL, location_id int NOT NULL);
                INSERT INTO ods.location VALUES (1, 1000079251), (2, 1000079252), (3, 1000079253), (4, NULL), (5, 555);
                -- Warehouse Provider 2274 has three divisions with portal Facility ids, and one with none. Provider 9 has another.
                INSERT INTO ods.provider_reporting_division VALUES (1, 2274, 1), (2, 2274, 2), (3, 2274, 3), (4, 2274, 4), (5, 9, 5);

                CREATE SCHEMA IF NOT EXISTS reporting;
                DROP TABLE IF EXISTS reporting.trip_detail_snapshot;
                CREATE TABLE reporting.trip_detail_snapshot (snapshot_date date NOT NULL, encounter_id bigint NOT NULL, provider_id int NOT NULL, reporting_division text);
                -- Provider 2274: two names in the latest load (one with a legacy suffix and a second spelling of it), none, <none>, and a blank. An older load has a base that is gone.
                -- Provider 9 has a base of its own.
                INSERT INTO reporting.trip_detail_snapshot VALUES
                    ('2026-11-21', 1, 2274, 'Mercy Flights - Rotor'), ('2026-11-21', 2, 2274, 'Mercy Flights - Rotor - Old AR Logis'), ('2026-11-21', 3, 2274, 'Mercy Flights - Fixed'),
                    ('2026-11-21', 4, 2274, NULL), ('2026-11-21', 5, 2274, '<none>'), ('2026-11-21', 6, 2274, '  '), ('2026-11-21', 7, 2274, 'Mercy Flights - Fixed'),
                    ('2026-10-01', 8, 2274, 'Retired Base'), ('2026-11-21', 9, 9, 'Other Provider - Base');
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        // Portal company 2193 is warehouse Provider 2274.
        return new ProviderDivisionDirectory(warehouse, new FakeProviderResolver(new Dictionary<int, int> { [2193] = 2274, [404] = 0 }));
    }

    [Fact]
    public async Task It_lists_the_portal_facility_ids_of_the_providers_divisions_and_skips_ones_without()
    {
        using ProviderDivisionDirectory directory = await NewDirectoryAsync();

        IReadOnlySet<string> ids = await directory.GetFacilityIdsAsync(2193, TestContext.Current.CancellationToken);

        Assert.Equal(["1000079251", "1000079252", "1000079253"], ids.Order());
    }

    [Fact]
    public async Task An_unmapped_provider_has_no_divisions_and_never_reads_another_providers()
    {
        using ProviderDivisionDirectory directory = await NewDirectoryAsync();

        IReadOnlySet<string> ids = await directory.GetFacilityIdsAsync(404, TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task It_lists_the_reporting_divisions_of_the_latest_load_without_legacy_suffixes_blanks_or_other_providers()
    {
        using ProviderDivisionDirectory directory = await NewDirectoryAsync();

        IReadOnlyList<ReportingDivision> divisions = await directory.GetReportingDivisionsAsync(2193, TestContext.Current.CancellationToken);

        Assert.Equal([new ReportingDivision("mercy-flights-fixed", "Mercy Flights - Fixed"), new ReportingDivision("mercy-flights-rotor", "Mercy Flights - Rotor")], divisions);
    }

    [Fact]
    public async Task An_unmapped_provider_has_no_reporting_divisions_and_never_reads_another_providers()
    {
        using ProviderDivisionDirectory directory = await NewDirectoryAsync();

        IReadOnlyList<ReportingDivision> divisions = await directory.GetReportingDivisionsAsync(404, TestContext.Current.CancellationToken);

        Assert.Empty(divisions);
    }
}
