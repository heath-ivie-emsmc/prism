using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the TransportMixByCallType Metric.</summary>
public sealed class TransportMixByCallTypeModule : IMetricModule
{
    public string MetricKey => ITransportMixByCallTypeMetric.Definition.Key;

    public int RouteOrder => 10;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ITransportMixByCallTypeMetric, TransportMixByCallTypeMetric, CachedTransportMixByCallTypeMetric>();

    public void MapRoutes(RouteGroupBuilder group) => TransportMixByCallTypeEndpoints.Map(group);
}
