using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the CleanClaimRate Metric.</summary>
public sealed class CleanClaimRateModule : IMetricModule
{
    public string MetricKey => ICleanClaimRateMetric.Definition.Key;

    public int RouteOrder => 8;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ICleanClaimRateMetric, CleanClaimRateMetric, CachedCleanClaimRateMetric>();

    public void MapRoutes(RouteGroupBuilder group) => CleanClaimRateEndpoints.Map(group);
}
