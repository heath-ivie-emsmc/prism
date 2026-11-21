using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;

namespace Prism.Tests.Metrics;

public sealed class ApPeriodTests
{
    [Theory]
    [InlineData("202610", 2026, 10)]
    [InlineData("202610 ", 2026, 10)]
    [InlineData(" 202501", 2025, 1)]
    public void A_valid_period_parses(string text, int year, int month)
    {
        Assert.True(ApPeriod.TryParse(text, out ApPeriod period));
        Assert.Equal(new ApPeriod(year, month), period);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10")]
    [InlineData("20261")]
    [InlineData("2026100")]
    [InlineData("202613")]
    [InlineData("202600")]
    [InlineData("199912")]
    [InlineData("210101")]
    [InlineData("2026 10")]
    [InlineData("20261x")]
    [InlineData("202610'; DROP TABLE x;--")]
    public void Anything_else_is_rejected(string? text) => Assert.False(ApPeriod.TryParse(text, out _));

    [Theory]
    [InlineData(2026, 10, -5, "202605")]
    [InlineData(2026, 10, 0, "202610")]
    [InlineData(2026, 1, -1, "202512")]
    [InlineData(2026, 12, 1, "202701")]
    [InlineData(2026, 3, -14, "202501")]
    public void Months_can_be_added_across_years(int year, int month, int add, string expected) =>
        Assert.Equal(expected, new ApPeriod(year, month).AddMonths(add).ToString());
}

public sealed class CatalogQueryRulesTests
{
    [Fact]
    public void Every_catalog_query_obeys_the_warehouse_rules()
    {
        // The warehouse refuses a query with no Provider filter, a second statement, or an unused parameter. This catches it in the build, not on first use.
        var provider = new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = 1 };
        NpgsqlWarehouse.Validate(new WarehouseQuery(NetCollectionsMetric.PeriodsSql, provider));
        NpgsqlWarehouse.Validate(new WarehouseQuery(
            NetCollectionsMetric.SeriesSql,
            new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = 1, ["from_period"] = "202501", ["to_period"] = "202506" }));
    }

    [Fact]
    public void Catalog_queries_read_only_the_current_load_and_never_wrap_the_period_column_in_a_filter()
    {
        foreach (string sql in new[] { NetCollectionsMetric.PeriodsSql, NetCollectionsMetric.SeriesSql })
        {
            Assert.Contains("is_current", sql, StringComparison.Ordinal);
            Assert.Contains("net_collections IS NOT NULL", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("SELECT *", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotContain("WHERE trim(", NetCollectionsMetric.SeriesSql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AND trim(", NetCollectionsMetric.SeriesSql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_development_provider_cannot_be_set_on_a_deployed_host()
    {
        var options = new PrismWarehouseOptions
        {
            ConnectionString = "Host=dw;Database=d;Username=u;Password=p;SSL Mode=VerifyFull",
            DevelopmentProviderId = 2026,
        };

        Assert.Contains(PrismWarehouseOptionsRules.Problems(options, deployed: true), p => p.Contains("DevelopmentProviderId", StringComparison.Ordinal));
        Assert.Empty(PrismWarehouseOptionsRules.Problems(options, deployed: false));
    }
}
