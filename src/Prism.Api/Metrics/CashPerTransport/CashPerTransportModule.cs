using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the CashPerTransport Metric.</summary>
public sealed class CashPerTransportModule : IMetricModule
{
    public string MetricKey => ICashPerTransportMetric.Definition.Key;

    public int RouteOrder => 5;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ICashPerTransportMetric, CashPerTransportMetric, CachedCashPerTransportMetric>();

    public void MapRoutes(RouteGroupBuilder group) => CashPerTransportEndpoints.Map(group);
}
