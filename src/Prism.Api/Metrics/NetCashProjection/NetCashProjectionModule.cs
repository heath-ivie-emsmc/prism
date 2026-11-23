using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the NetCashProjection Metric.</summary>
public sealed class NetCashProjectionModule : IMetricModule
{
    public string MetricKey => INetCashProjectionMetric.Definition.Key;

    public int RouteOrder => 1;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<INetCashProjectionMetric, NetCashProjectionMetric, CachedNetCashProjectionMetric>();

    public void MapRoutes(RouteGroupBuilder group) => NetCashProjectionEndpoints.Map(group);
}
