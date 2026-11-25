using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;

namespace Prism.Tests.Warehouse;

/// <summary>The one query that names no Provider is still a plain read: no parameters, no writes, and a normal query without a Provider filter is still refused.</summary>
public sealed class AcrossProvidersTests
{
    private static readonly IReadOnlyDictionary<string, object?> None = new Dictionary<string, object?>();

    [Fact]
    public void The_provider_list_query_is_accepted_as_an_across_providers_query()
    {
        NpgsqlWarehouse.Validate(new WarehouseQuery(WarehouseProviderList.ListSql, None) { AcrossProviders = true });
    }

    [Fact]
    public void The_same_query_without_the_flag_is_refused_for_having_no_provider_filter()
    {
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(new WarehouseQuery(WarehouseProviderList.ListSql, None)));
    }

    [Theory]
    [InlineData("DELETE FROM ods.provider")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT pg_sleep(5)")]
    [InlineData("UPDATE ods.provider SET id = 1")]
    public void An_across_providers_query_still_has_to_be_a_plain_read(string sql)
    {
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(new WarehouseQuery(sql, None) { AcrossProviders = true }));
    }

    [Fact]
    public void An_across_providers_query_takes_no_parameters_and_does_not_use_the_provider_parameter()
    {
        var withParameter = new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = 1 };

        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(new WarehouseQuery("SELECT id FROM ods.provider WHERE id = @provider_id", withParameter) { AcrossProviders = true }));
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(new WarehouseQuery("SELECT id FROM ods.provider WHERE id = @provider_id", None) { AcrossProviders = true }));
    }
}
