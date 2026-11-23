using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the PayerMixDetail Metric, a drill target of payer mix.</summary>
public sealed class PayerMixDetailModule : IMetricModule
{
    public string MetricKey => IPayerMixDetailMetric.Definition.Key;

    public int RouteOrder => 9;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IPayerMixDetailMetric, PayerMixDetailMetric, CachedPayerMixDetailMetric>();

    public void MapRoutes(RouteGroupBuilder group) => PayerMixDetailEndpoints.Map(group);
}
