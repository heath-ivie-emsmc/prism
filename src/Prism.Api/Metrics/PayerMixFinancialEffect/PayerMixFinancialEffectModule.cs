using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the PayerMixFinancialEffect Metric.</summary>
public sealed class PayerMixFinancialEffectModule : IMetricModule
{
    public string MetricKey => IPayerMixFinancialEffectMetric.Definition.Key;

    public int RouteOrder => 14;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IPayerMixFinancialEffectMetric, PayerMixFinancialEffectMetric, CachedPayerMixFinancialEffectMetric>();

    public void MapRoutes(RouteGroupBuilder group) => PayerMixFinancialEffectEndpoints.Map(group);
}
