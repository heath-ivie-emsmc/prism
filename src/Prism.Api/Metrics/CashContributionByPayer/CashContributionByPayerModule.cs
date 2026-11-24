using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the CashContributionByPayer Metric.</summary>
public sealed class CashContributionByPayerModule : IMetricModule
{
    public string MetricKey => ICashContributionByPayerMetric.Definition.Key;

    public int RouteOrder => 18;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ICashContributionByPayerMetric, CashContributionByPayerMetric, CachedCashContributionByPayerMetric>();

    public void MapRoutes(RouteGroupBuilder group) => CashContributionByPayerEndpoints.Map(group);
}
