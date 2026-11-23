using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the NetCashByGroup Metric.</summary>
public sealed class NetCashByGroupModule : IMetricModule
{
    public string MetricKey => INetCashByGroupMetric.Definition.Key;

    public int RouteOrder => 6;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<INetCashByGroupMetric, NetCashByGroupMetric, CachedNetCashByGroupMetric>();

    public void MapRoutes(RouteGroupBuilder group) => NetCashByGroupEndpoints.Map(group);
}
