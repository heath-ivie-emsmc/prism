using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Warehouse;

/// <summary>The portal-to-warehouse Provider mapping against a real PostgreSQL holding tables shaped like <c>ods.provider</c> and <c>ods.company</c>.</summary>
public sealed class WarehouseProviderResolverTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly SemaphoreSlim SeedLock = new(1, 1);

    private async Task<WarehouseProviderResolver> NewResolverAsync(int? developmentProviderId = null)
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
                DROP TABLE IF EXISTS ods.provider, ods.company;
                CREATE TABLE ods.provider (id int PRIMARY KEY, integration_id int);
                CREATE TABLE ods.company (id int PRIMARY KEY, integration_id int);
                -- Portal company 2193 is Provider 2274 (through company 100000), and warehouse Provider 2193 is a different company, portal id 517.
                INSERT INTO ods.company VALUES (100000, 2193), (517, 517), (200000, 900), (300000, 900);
                INSERT INTO ods.provider VALUES (2274, 100000), (2193, 517), (5001, 200000), (5002, 300000);
                """,
                admin);
            await seed.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            SeedLock.Release();
        }

        var options = new PrismWarehouseOptions { ConnectionString = postgres.ConnectionString, DevelopmentProviderId = developmentProviderId };
        var warehouse = new NpgsqlWarehouse(WarehouseDataSourceFactory.Create(options), Options.Create(options), NullLogger<NpgsqlWarehouse>.Instance);
        return new WarehouseProviderResolver(warehouse, Options.Create(options));
    }

    [Fact]
    public async Task A_portal_company_id_becomes_the_warehouse_provider_it_belongs_to_not_the_one_with_the_same_number()
    {
        using WarehouseProviderResolver resolver = await NewResolverAsync();

        Assert.Equal(2274, await resolver.ResolveAsync(2193, TestContext.Current.CancellationToken));
        Assert.Equal(2193, await resolver.ResolveAsync(517, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_unknown_portal_id_is_unmapped_and_never_falls_back_to_the_same_number()
    {
        using WarehouseProviderResolver resolver = await NewResolverAsync();

        // 2274 is a warehouse Provider id, but no portal company has that id, so it must not read Provider 2274.
        Assert.Equal(IWarehouseProviderResolver.Unmapped, await resolver.ResolveAsync(2274, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_ambiguous_mapping_is_treated_as_none()
    {
        using WarehouseProviderResolver resolver = await NewResolverAsync();

        // Two warehouse Providers claim portal id 900.
        Assert.Equal(IWarehouseProviderResolver.Unmapped, await resolver.ResolveAsync(900, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_development_stand_in_still_wins_when_set()
    {
        using WarehouseProviderResolver resolver = await NewResolverAsync(developmentProviderId: 777);

        Assert.Equal(777, await resolver.ResolveAsync(2193, TestContext.Current.CancellationToken));
    }
}
