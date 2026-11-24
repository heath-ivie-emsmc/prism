using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One payer group's move in share and what that move is expected to do to cash. <paramref name="CashPerTransport"/> and <paramref name="Effect"/> are null when the group has no matured transports.</summary>
public sealed record PayerMixEffectRow(PayerMixShare Share, decimal ShareFrom, decimal ShareTo, decimal? CashPerTransport, decimal? Effect);

/// <summary>The effect of every payer group, and the transports of the two months. <paramref name="TotalEffect"/> is null when no group has an effect (no transports in a month, or no matured cash per transport).</summary>
public sealed record PayerMixEffectResult(IReadOnlyList<PayerMixEffectRow> Rows, int Now, int Before, decimal? TotalEffect);

/// <summary>
/// The one place the payer mix effect is worked out, so the Insight and the Metric agree. The effect of a payer group is its change in share of transports,
/// times what a transport of that group has earned once matured (<see cref="CashPerTransport"/>), times this month's transports.
/// </summary>
public static class PayerMixEffect
{
    public static PayerMixEffectResult Compute(PayerMix mix, CashPerTransport rates)
    {
        decimal now = mix.Shares.Sum(s => s.Current);
        decimal before = mix.Shares.Sum(s => s.Prior);
        bool comparable = now != 0m && before != 0m;
        List<PayerMixEffectRow> rows = [.. mix.Shares.Select(s =>
        {
            decimal from = before == 0m ? 0m : s.Prior / before;
            decimal to = now == 0m ? 0m : s.Current / now;
            decimal? rate = rates.Payers.FirstOrDefault(p => p.Key == s.Key)?.CashPerTransport;
            decimal? effect = comparable && rate is { } r ? (to - from) * r * now : null;
            return new PayerMixEffectRow(s, from, to, rate, effect);
        })];
        decimal? total = rows.Any(r => r.Effect is not null) ? rows.Sum(r => r.Effect ?? 0m) : null;
        return new PayerMixEffectResult(rows, (int)now, (int)before, total);
    }
}

/// <summary>
/// The expected effect of a shift in payer mix on cash, in the month and if the shift persists. <paramref name="CohortFrom"/> and <paramref name="CohortThrough"/> are the date-of-service months
/// behind the matured cash per transport. <paramref name="TotalEffect"/> is null when it cannot be worked out.
/// </summary>
public sealed record PayerMixFinancialEffect(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ApPeriod CohortFrom,
    ApPeriod CohortThrough,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    int Transports,
    int PriorTransports,
    IReadOnlyList<PayerMixEffectRow> Groups,
    decimal? TotalEffect);

/// <summary>
/// The estimated cash effect of a shift in payer mix. It is an expected effect, not posted cash: payer mix is by date of service and cash is by the month it
/// posts. The recurring monthly effect is the same figure, on the assumption that the shift persists.
/// </summary>
public interface IPayerMixFinancialEffectMetric
{
    public static MetricDefinition Definition { get; } = new(
        "payer_mix_financial_effect",
        "Financial effect of payer mix",
        "The expected effect on cash of the change in payer mix since the month before: each payer group's change in share of transports, times what a transport of that group has earned once matured, times this month's transports. It is an expected effect, not posted cash. The recurring monthly effect is the same figure if the shift persists, which is an assumption.",
        "USD");

    Task<PayerMixFinancialEffect> GetEffectAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
