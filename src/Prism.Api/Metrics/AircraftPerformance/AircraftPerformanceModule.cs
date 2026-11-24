using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the AircraftPerformance Metric.</summary>
public sealed class AircraftPerformanceModule : IMetricModule
{
    public string MetricKey => IAircraftPerformanceMetric.Definition.Key;

    public int RouteOrder => 11;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IAircraftPerformanceMetric, AircraftPerformanceMetric, CachedAircraftPerformanceMetric>();

    public void MapRoutes(RouteGroupBuilder group) => AircraftPerformanceEndpoints.Map(group);
}
