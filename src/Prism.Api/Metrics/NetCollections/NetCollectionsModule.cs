using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the NetCollections Metric.</summary>
public sealed class NetCollectionsModule : IMetricModule
{
    public string MetricKey => INetCollectionsMetric.Definition.Key;

    public int RouteOrder => 0;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<INetCollectionsMetric, NetCollectionsMetric, CachedNetCollectionsMetric>();

    public void MapRoutes(RouteGroupBuilder group) => NetCollectionsEndpoints.Map(group);
}
