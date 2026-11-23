using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the TransportVolume Metric.</summary>
public sealed class TransportVolumeModule : IMetricModule
{
    public string MetricKey => ITransportVolumeMetric.Definition.Key;

    public int RouteOrder => 7;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ITransportVolumeMetric, TransportVolumeMetric, CachedTransportVolumeMetric>();

    public void MapRoutes(RouteGroupBuilder group) => TransportVolumeEndpoints.Map(group);
}
