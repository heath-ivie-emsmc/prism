using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the TransportsByPickupLocation Metric.</summary>
public sealed class TransportsByPickupLocationModule : IMetricModule
{
    public string MetricKey => ITransportsByPickupLocationMetric.Definition.Key;

    public int RouteOrder => 15;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ITransportsByPickupLocationMetric, TransportsByPickupLocationMetric, CachedTransportsByPickupLocationMetric>();

    public void MapRoutes(RouteGroupBuilder group) => TransportsByPickupLocationEndpoints.Map(group);
}
