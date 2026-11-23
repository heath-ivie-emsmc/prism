using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the PayerMix Metric.</summary>
public sealed class PayerMixModule : IMetricModule
{
    public string MetricKey => IPayerMixMetric.Definition.Key;

    public int RouteOrder => 3;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IPayerMixMetric, PayerMixMetric, CachedPayerMixMetric>();

    public void MapRoutes(RouteGroupBuilder group) => PayerMixEndpoints.Map(group);
}
