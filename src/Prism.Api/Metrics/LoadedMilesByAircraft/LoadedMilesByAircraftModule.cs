using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the LoadedMilesByAircraft Metric.</summary>
public sealed class LoadedMilesByAircraftModule : IMetricModule
{
    public string MetricKey => ILoadedMilesByAircraftMetric.Definition.Key;

    public int RouteOrder => 13;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<ILoadedMilesByAircraftMetric, LoadedMilesByAircraftMetric, CachedLoadedMilesByAircraftMetric>();

    public void MapRoutes(RouteGroupBuilder group) => LoadedMilesByAircraftEndpoints.Map(group);
}
