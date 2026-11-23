namespace Prism.Api.Metrics;

/// <summary>
/// One Metric slice: registers its query service and maps its routes. Modules are discovered by
/// <see cref="MetricModules"/>, so adding a Metric means adding a module and nothing else in the host.
/// </summary>
public interface IMetricModule
{
    /// <summary>The Metric Catalog key this module serves, or null for a module that serves no single Metric.</summary>
    string? MetricKey { get; }

    /// <summary>
    /// Position among the modules when routes are mapped. Path order decides the order of the published OpenAPI
    /// document, so it is fixed here rather than left to discovery order. Ties fall back to the type name.
    /// </summary>
    int RouteOrder { get; }

    /// <summary>Registers the Metric's query service.</summary>
    void AddServices(IServiceCollection services);

    /// <summary>Maps the Metric's routes on the shared <c>/providers/{providerId}/metrics/net-collections</c> group.</summary>
    void MapRoutes(RouteGroupBuilder group);
}
