using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>
/// What a change in the payer mix is expected to do to cash. The effect of each payer group is its change in share of transports, times what a transport
/// of that group has earned once matured, times this month's transports. Payer mix is by date of service and cash is by the month it posted, so this is
/// an expected effect as those claims are paid, not a part of this month's posted change. It is Structural when the same group's share also moved the same
/// way the month before, and Watch when it did not.
/// </summary>
public sealed class PayerMixAnalysis(IPayerMixMetric payerMix, ICashPerTransportMetric cashPerTransport) : IInsightAnalysis
{
    public int Order => 30;

    public async Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken)
    {
        // The two reads do not depend on each other, so they run together.
        Task<PayerMix> mixTask = payerMix.GetPayerMixAsync(input.ProviderId, input.Period, input.ServiceLine, input.GroupKey, cancellationToken);
        Task<CashPerTransport> ratesTask = cashPerTransport.GetCashPerTransportAsync(input.ProviderId, input.Period, input.ServiceLine, input.GroupKey, cancellationToken);
        await Task.WhenAll(mixTask, ratesTask);
        PayerMix mix = await mixTask;
        decimal now = mix.Shares.Sum(s => s.Current);
        decimal before = mix.Shares.Sum(s => s.Prior);
        if (now == 0m || before == 0m)
        {
            return null;
        }

        CashPerTransport rates = await ratesTask;
        var moves = mix.Shares
            .Select(s => (Share: s, From: s.Prior / before, To: s.Current / now, Rate: rates.Payers.FirstOrDefault(p => p.Key == s.Key)?.CashPerTransport))
            .Where(m => m.Rate is not null)
            .Select(m => (m.Share, m.From, m.To, Effect: (m.To - m.From) * m.Rate!.Value * now))
            .ToList();
        if (moves.Count == 0)
        {
            return null;
        }

        // The story is the group whose share rose most. Shares add up to 100%, so some group always rose when any fell.
        var biggest = moves.OrderByDescending(m => m.To - m.From).First();
        if (biggest.To - biggest.From < InsightThresholds.MinShareMove)
        {
            return null;
        }

        decimal impact = moves.Sum(m => m.Effect);
        FindingLabel label = FindingLabel.Info;
        if (impact < 0m)
        {
            bool held = await HeldBeforeAsync(input, biggest.Share.Key, biggest.To - biggest.From, cancellationToken);
            label = held ? FindingLabel.Structural : FindingLabel.Watch;
        }

        return new PayerMixFinding(label, impact, biggest.Share.Key, biggest.Share.Name, biggest.From, biggest.To);
    }

    /// <summary>Whether the payer group's share also moved the same way the month before.</summary>
    private async Task<bool> HeldBeforeAsync(InsightInput input, string payerKey, decimal move, CancellationToken cancellationToken)
    {
        PayerMix earlier = await payerMix.GetPayerMixAsync(input.ProviderId, input.PriorPeriod, input.ServiceLine, input.GroupKey, cancellationToken);
        decimal now = earlier.Shares.Sum(s => s.Current);
        decimal before = earlier.Shares.Sum(s => s.Prior);
        if (now == 0m || before == 0m || earlier.Shares.FirstOrDefault(s => s.Key == payerKey) is not { } share)
        {
            return false;
        }

        decimal earlierMove = (share.Current / now) - (share.Prior / before);
        return Math.Sign(earlierMove) == Math.Sign(move) && Math.Abs(earlierMove) >= InsightThresholds.MinShareMove;
    }
}
