using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the BaseAnalysis Metric.</summary>
public sealed class BaseAnalysisModule : IMetricModule
{
    public string MetricKey => IBaseAnalysisMetric.Definition.Key;

    public int RouteOrder => 12;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IBaseAnalysisMetric, BaseAnalysisMetric, CachedBaseAnalysisMetric>();

    public void MapRoutes(RouteGroupBuilder group) => BaseAnalysisEndpoints.Map(group);
}
