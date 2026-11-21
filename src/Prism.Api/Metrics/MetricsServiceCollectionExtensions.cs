using Prism.Api.Http;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the Metric Catalog.</summary>
public static class MetricsServiceCollectionExtensions
{
    public static IServiceCollection AddPrismMetrics(this IServiceCollection services)
    {
        services.AddSingleton<IWarehouseProviderResolver, WarehouseProviderResolver>();
        services.AddSingleton<IProviderDivisionDirectory, ProviderDivisionDirectory>();
        services.AddScoped<ProviderAccess>();
        services.AddSingleton<INetCollectionsMetric, NetCollectionsMetric>();
        services.AddSingleton<INetCashProjectionMetric, NetCashProjectionMetric>();
        services.AddSingleton<INetCashBreakdownMetric, NetCashBreakdownMetric>();
        services.AddSingleton<IPayerMixMetric, PayerMixMetric>();
        services.AddExceptionHandler<WarehouseExceptionHandler>();
        return services;
    }

    public static void MapMetricEndpoints(this RouteGroupBuilder v1) => NetCollectionsEndpoints.Map(v1);
}
