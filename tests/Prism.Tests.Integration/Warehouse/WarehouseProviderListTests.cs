using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Admin;
using Prism.Infrastructure.Warehouse;
using Prism.Testing.Common;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>The staff Provider list against a real PostgreSQL holding tables shaped like <c>ods.provider</c> and <c>ods.company</c>.</summary>
public sealed class WarehouseProviderListTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<IReadOnlyList<ProviderChoice>> ListAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await SeedLock.WaitAsync(ct);
        try
        {
            await using var admin = new NpgsqlConnection(TestDatabaseGuard.Assert(postgres.ConnectionString));
            await admin.OpenAsync(ct);
            await using var seed = new NpgsqlCommand(
                """
                CREATE SCHEMA IF NOT EXISTS ods;
                DROP TABLE IF EXISTS ods.provider, ods.company;
                CREATE TABLE ods.provider (id int PRIMARY KEY, integration_id int, legal_name varchar(128), display_name varchar(128));
                CREATE TABLE ods.company (id int PRIMARY KEY, integration_id int);
                INSERT INTO ods.company VALUES (100000, 2193), (517, 517), (200000, 900), (300000, 900), (400000, NULL), (500000, 777);
                INSERT INTO ods.provider VALUES
                  (2274, 100000, 'Mercy Flights LLC', 'Mercy Flights'),
                  (2193, 517, 'Providence Transportation Inc', '  Providence Transportation  '),
                  (5001, 200000, 'Dup A', 'Dup A'),
                  (5002, 300000, 'Dup B', 'Dup B'),
                  (5003, 400000, 'No portal number', 'No portal number'),
                  (5004, 500000, 'No display name', NULL);
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
        return await new WarehouseProviderList(warehouse).ListAsync(ct);
    }

    [Fact]
    public async Task Lists_each_provider_by_its_portal_number_and_trimmed_display_name()
    {
        IReadOnlyList<ProviderChoice> list = await ListAsync();

        Assert.Equal([new ProviderChoice("2193", "Mercy Flights"), new ProviderChoice("517", "Providence Transportation")], [.. list.OrderBy(p => p.Name, StringComparer.Ordinal)]);
    }

    [Fact]
    public async Task Leaves_out_a_portal_number_two_providers_claim_a_row_with_no_portal_number_and_one_with_no_name()
    {
        IReadOnlyList<ProviderChoice> list = await ListAsync();

        Assert.DoesNotContain(list, p => p.Id is "900" or "777");
        Assert.DoesNotContain(list, p => p.Name.StartsWith("Dup", StringComparison.Ordinal) || p.Name.StartsWith("No ", StringComparison.Ordinal));
    }
}
