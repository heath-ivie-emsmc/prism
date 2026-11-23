namespace Prism.Api.Metrics;

internal static class MetricCacheRegistration
{
    /// <summary>
    /// Registers an aggregate Metric behind its caching decorator: the Metric itself under its own type, and the service as the decorator around it.
    /// Claim-level services are never registered this way.
    /// </summary>
    internal static IServiceCollection AddCachedMetric<TService, TMetric, TDecorator>(this IServiceCollection services)
        where TService : class
        where TMetric : class, TService
        where TDecorator : class, TService
    {
        services.AddSingleton<TMetric>();
        services.AddSingleton<TService>(sp => ActivatorUtilities.CreateInstance<TDecorator>(sp, sp.GetRequiredService<TMetric>()));
        return services;
    }
}
