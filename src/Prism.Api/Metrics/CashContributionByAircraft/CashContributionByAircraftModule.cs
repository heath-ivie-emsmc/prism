using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the CashContributionByAircraft Metric.</summary>
public sealed class CashContributionByAircraftModule : IMetricModule
{
    public string MetricKey => ICashContributionByAircraftMetric.Definition.Key;

    public int RouteOrder => 16;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ICashContributionByAircraftMetric, CashContributionByAircraftMetric, CachedCashContributionByAircraftMetric>();

    public void MapRoutes(RouteGroupBuilder group) => CashContributionByAircraftEndpoints.Map(group);
}
