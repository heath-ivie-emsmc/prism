namespace Prism.Api.Metrics;

/// <summary>The accounting-period list shared by every Metric's Period drop-down. It serves no single Metric.</summary>
public sealed class PeriodsModule : IMetricModule
{
    public string? MetricKey => null;

    public int RouteOrder => 100;

    public void AddServices(IServiceCollection services)
    {
        // No services of its own: the periods come from the NetCollections Metric.
    }

    public void MapRoutes(RouteGroupBuilder group) => NetCollectionsEndpoints.MapPeriods(group);
}
