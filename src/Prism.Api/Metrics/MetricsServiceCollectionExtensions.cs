using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Prism.Api.Http;
using Prism.Application.Attention;
using Prism.Application.Auth;
using Prism.Application.Insights;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Insights;
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
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDataThroughReader, DataThroughReader>();
        services.AddSingleton<IMetricCache, MemoryMetricCache>();
        services.AddScoped<ProviderAccess>();
        foreach (IMetricModule module in MetricModules.Current)
        {
            module.AddServices(services);
        }

        services.AddSingleton<IInsightAnalysis, AgedPaymentsAnalysis>();
        services.AddSingleton<IInsightAnalysis, VolumeAnalysis>();
        services.AddSingleton<IInsightAnalysis, PayerMixAnalysis>();
        services.AddSingleton<InsightsEngine>();
        services.AddSingleton<IInsightService, CachedInsightService>();
        services.AddSingleton<IValidateOptions<FilingLimitOptions>, FilingLimitOptionsValidator>();
        services.AddOptions<FilingLimitOptions>().BindConfiguration(FilingLimitOptions.SectionName).ValidateOnStart();
        services.AddSingleton<IClaimAttention, ClaimAttentionReader>();
        services.AddExceptionHandler<WarehouseExceptionHandler>();
        return services;
    }

    public static void MapMetricEndpoints(this RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/metrics/net-collections")
            .RequireAuthorization(Permissions.DashboardView)
            .AddEndpointFilter(MetricCacheHeaders.Revalidate)
            .WithTags("Metrics");
        foreach (IMetricModule module in MetricModules.Current)
        {
            module.MapRoutes(group);
        }

        CatalogEndpoints.Map(v1);
        InsightsEndpoints.Map(v1);
        Attention.ClaimAttentionEndpoints.Map(v1);
    }
}

/// <summary>Refuses to start with a filing limit outside 1 to 1,825 days, so a typo cannot silently make every deadline wrong.</summary>
internal sealed class FilingLimitOptionsValidator : IValidateOptions<FilingLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, FilingLimitOptions options) =>
        options.IsValid()
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{FilingLimitOptions.SectionName} limits must be between {FilingLimitOptions.MinDays} and {FilingLimitOptions.MaxDays} days.");
}
