using Prism.Api.Metrics;
using Prism.Application.Metrics;

namespace Prism.Tests.Metrics;

/// <summary>Each served Metric is one module, and the modules and the catalog never drift apart.</summary>
public sealed class MetricModuleRegistryTests
{
    [Fact]
    public void Every_available_placeable_catalog_entry_has_exactly_one_module()
    {
        IReadOnlyList<IMetricModule> modules = MetricModules.Discover();

        foreach (MetricCatalogEntry entry in MetricCatalog.Entries.Where(e => e.CanBePlaced))
        {
            Assert.Single(modules, m => m.MetricKey == entry.Key);
        }
    }

    [Fact]
    public void Every_module_serves_an_available_catalog_entry()
    {
        IReadOnlyList<IMetricModule> modules = MetricModules.Discover();

        Assert.NotEmpty(modules);
        Assert.All(modules.Where(m => m.MetricKey is not null), module =>
        {
            MetricCatalogEntry? entry = MetricCatalog.Find(module.MetricKey!);
            Assert.NotNull(entry);
            Assert.Equal(MetricStatus.Available, entry.Status);
        });
    }

    [Fact]
    public void Route_orders_are_unique_so_discovery_is_deterministic()
    {
        int[] orders = [.. MetricModules.Discover().Select(m => m.RouteOrder)];

        Assert.Equal(orders.Length, orders.Distinct().Count());
        Assert.Equal(orders.Order(), orders);
    }
}
