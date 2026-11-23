using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>
/// Explains the change in a group's posted net cash against the month before. It reads the group's monthly cash, runs every analysis in order so each
/// works on what the earlier ones left, keeps the Findings that are big enough to matter, and says what is left over. Analyses that do not apply return
/// nothing, so a month with no unusual aged payments simply has no such card.
/// </summary>
public sealed class InsightsEngine(INetCashByGroupMetric netCash, IEnumerable<IInsightAnalysis> analyses)
{
    public async Task<Insight?> ExplainAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, CancellationToken cancellationToken)
    {
        NetCashByGroup cash = await netCash.GetNetCashByGroupAsync(providerId, period, serviceLine, null, cancellationToken);
        (string name, IReadOnlyList<MonthlyCash> history) = Shape(cash, serviceLine, groupKey);
        MonthlyCash? now = history.FirstOrDefault(m => m.Period == period);
        MonthlyCash? before = history.FirstOrDefault(m => m.Period == period.AddMonths(-1));
        if (now is null && before is null)
        {
            return null;
        }

        decimal cashNow = now?.Cash ?? 0m;
        decimal cashPrior = before?.Cash ?? 0m;
        decimal delta = cashNow - cashPrior;
        var headline = new InsightHeadline(period, period.AddMonths(-1), groupKey, name, cashNow, cashPrior, delta, cashPrior == 0m ? null : delta / cashPrior);

        List<Finding> all = await RunAnalysesAsync(new InsightInput(providerId, period, serviceLine, groupKey, history, []), cancellationToken);
        decimal bar = InsightThresholds.Materiality * Math.Abs(delta);
        List<Finding> shown = delta == 0m ? [] : [.. all.Where(f => Math.Abs(f.Impact) >= bar).OrderByDescending(f => Math.Abs(f.Impact)).Take(InsightThresholds.MaxFindings)];
        Finding[] drivers = [.. shown.Where(f => f.Basis == FindingBasis.PostedCash)];
        Finding[] expected = [.. shown.Where(f => f.Basis == FindingBasis.ExpectedEffect)];

        decimal remaining = delta - drivers.Sum(f => f.Impact);
        return new Insight(headline, drivers, expected, new Remainder(remaining, Judge(remaining, history, period)), Normalized(all, cashNow, cashPrior));
    }

    /// <summary>
    /// Starts every analysis at once, in order, each given the ones before it. Analyses that read different Metrics overlap, and one that needs an
    /// earlier result awaits it. If one fails the rest are cancelled and its failure is what the caller sees.
    /// </summary>
    private async Task<List<Finding>> RunAnalysesAsync(InsightInput start, CancellationToken cancellationToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var started = new List<Task<Finding?>>();
        foreach (IInsightAnalysis analysis in analyses.OrderBy(a => a.Order))
        {
            started.Add(RunOneAsync(analysis, start with { Earlier = [.. started] }, cancel));
        }

        Finding?[] results = await Task.WhenAll(started);
        return [.. results.OfType<Finding>()];
    }

    private static async Task<Finding?> RunOneAsync(IInsightAnalysis analysis, InsightInput input, CancellationTokenSource cancel)
    {
        try
        {
            return await analysis.RunAsync(input, cancel.Token);
        }
        catch
        {
            await cancel.CancelAsync();
            throw;
        }
    }

    /// <summary>A group's own months, or every group's added up when no group is chosen.</summary>
    private static (string Name, IReadOnlyList<MonthlyCash> History) Shape(NetCashByGroup cash, ServiceLineFilter serviceLine, string? groupKey)
    {
        IEnumerable<NetCashByGroupPoint> points = groupKey is null ? cash.Points : cash.Points.Where(p => p.GroupKey == groupKey);
        string name = groupKey is null ? ServiceLineName(serviceLine) : points.Select(p => p.GroupName).FirstOrDefault() ?? groupKey;
        MonthlyCash[] history = [.. points
            .GroupBy(p => p.Period)
            .Select(g => new MonthlyCash(g.Key, g.Sum(p => p.Cash), g.Sum(p => p.Transports)))
            .OrderBy(m => m.Period.Year).ThenBy(m => m.Period.Month)];
        return (name, history);
    }

    private static string ServiceLineName(ServiceLineFilter serviceLine) => serviceLine switch
    {
        ServiceLineFilter.Air => "Air",
        ServiceLineFilter.Ground => "Ground",
        _ => "All",
    };

    /// <summary>
    /// Normal when the remainder is within one standard deviation of the average month-to-month change over the twelve months before this one, and only
    /// when all twelve months have cash, so a gap in the history never makes something look normal.
    /// </summary>
    private static RemainderJudgment Judge(decimal remaining, IReadOnlyList<MonthlyCash> history, ApPeriod period)
    {
        MonthlyCash[] months = [.. Enumerable.Range(1, InsightThresholds.BandMonths).Reverse().Select(i => history.FirstOrDefault(m => m.Period == period.AddMonths(-i))).OfType<MonthlyCash>()];
        if (months.Length < InsightThresholds.BandMonths)
        {
            return RemainderJudgment.NotEnoughHistory;
        }

        double[] changes = [.. months.Zip(months.Skip(1), (a, b) => (double)(b.Cash - a.Cash))];
        double mean = changes.Average();
        double spread = Math.Sqrt(changes.Sum(c => (c - mean) * (c - mean)) / (changes.Length - 1));
        return Math.Abs((double)remaining - mean) <= spread ? RemainderJudgment.WithinNormalRange : RemainderJudgment.OutsideNormalRange;
    }

    private static decimal? Normalized(IReadOnlyList<Finding> all, decimal cashNow, decimal cashPrior)
    {
        if (all.OfType<AgedPaymentsFinding>().FirstOrDefault() is not { } aged)
        {
            return null;
        }

        decimal basis = cashPrior - aged.ExcessPriorMonth;
        return basis == 0m ? null : ((cashNow - aged.ExcessThisMonth) - basis) / basis;
    }
}
