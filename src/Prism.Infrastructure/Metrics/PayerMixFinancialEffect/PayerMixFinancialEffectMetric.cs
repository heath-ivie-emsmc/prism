using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The expected cash effect of a shift in payer mix. It reads no table of its own: it combines <see cref="IPayerMixMetric"/> (share of transports by payer group, by date of service)
/// with <see cref="ICashPerTransportMetric"/> (matured net cash per transport by payer group) through <see cref="PayerMixEffect"/>, the same maths the "what changed" Insight uses,
/// so the two always agree for the same data. Both reads take the same Service Line, group and Reporting Division, and the Provider filter is applied inside them.
/// </summary>
public sealed class PayerMixFinancialEffectMetric(IPayerMixMetric payerMix, ICashPerTransportMetric cashPerTransport) : IPayerMixFinancialEffectMetric
{
    public async Task<PayerMixFinancialEffect> GetEffectAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken)
    {
        // The two reads do not depend on each other, so they run together.
        Task<PayerMix> mixTask = payerMix.GetPayerMixAsync(providerId, period, serviceLine, groupKey, division, cancellationToken);
        Task<CashPerTransport> ratesTask = cashPerTransport.GetCashPerTransportAsync(providerId, period, serviceLine, groupKey, division, cancellationToken);
        await Task.WhenAll(mixTask, ratesTask);
        PayerMix mix = await mixTask;
        CashPerTransport rates = await ratesTask;
        PayerMixEffectResult effect = PayerMixEffect.Compute(mix, rates);
        return new PayerMixFinancialEffect(
            IPayerMixFinancialEffectMetric.Definition,
            period,
            mix.PriorPeriod,
            rates.CohortFrom,
            rates.CohortThrough,
            serviceLine,
            groupKey,
            effect.Now,
            effect.Before,
            effect.Rows,
            effect.TotalEffect);
    }
}
