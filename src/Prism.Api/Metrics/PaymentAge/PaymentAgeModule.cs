using Prism.Application.Metrics;
using Prism.Infrastructure.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Composition and routes for the PaymentAge Metric.</summary>
public sealed class PaymentAgeModule : IMetricModule
{
    public string MetricKey => IPaymentAgeMetric.Definition.Key;

    public int RouteOrder => 4;

    public void AddServices(IServiceCollection services) =>
        services.AddCachedMetric<IPaymentAgeMetric, PaymentAgeMetric, CachedPaymentAgeMetric>();

    public void MapRoutes(RouteGroupBuilder group) => PaymentAgeEndpoints.Map(group);
}
