using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the CashContributionByDivision Metric.</summary>
public sealed class CashContributionByDivisionModule : IMetricModule
{
    public string MetricKey => ICashContributionByDivisionMetric.Definition.Key;

    public int RouteOrder => 17;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ICashContributionByDivisionMetric, CashContributionByDivisionMetric, CachedCashContributionByDivisionMetric>();

    public void MapRoutes(RouteGroupBuilder group) => CashContributionByDivisionEndpoints.Map(group);
}
