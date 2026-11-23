using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the NetCashBreakdown Metric.</summary>
public sealed class NetCashBreakdownModule : IMetricModule
{
    public string MetricKey => INetCashBreakdownMetric.Definition.Key;

    public int RouteOrder => 2;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<INetCashBreakdownMetric, NetCashBreakdownMetric, CachedNetCashBreakdownMetric>();

    public void MapRoutes(RouteGroupBuilder group) => NetCashBreakdownEndpoints.Map(group);
}
