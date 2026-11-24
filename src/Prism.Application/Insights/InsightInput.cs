using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Application.Insights;

/// <summary>Net cash and the encounters it came from in one posting month, for the group being explained.</summary>
public sealed record MonthlyCash(ApPeriod Period, decimal Cash, int Transports);

/// <summary>
/// What one analysis needs to know about the change being explained. <paramref name="History"/> is the group's net cash by posting month for the
/// twelve months before <paramref name="Period"/> and the month itself. <paramref name="Earlier"/> holds the analyses that come before this one, already
/// started, so a later analysis can await what an earlier one left. <paramref name="Division"/> is the Reporting Division the whole explanation is limited to (null: every division). Analyses all start at once, so the Metric reads of independent analyses overlap.
/// </summary>
public sealed record InsightInput(
    int ProviderId,
    ApPeriod Period,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<MonthlyCash> History,
    IReadOnlyList<Task<Finding?>> Earlier,
    ReportingDivision? Division = null)
{
    public ApPeriod PriorPeriod => Period.AddMonths(-1);

    public MonthlyCash? Current => History.FirstOrDefault(m => m.Period == Period);

    public MonthlyCash? Prior => History.FirstOrDefault(m => m.Period == PriorPeriod);

    /// <summary>The Findings of the earlier analyses, once they have all finished. An analysis that does not need them never calls this and never waits.</summary>
    public async Task<IReadOnlyList<Finding>> FindingsBeforeAsync()
    {
        Finding?[] done = await Task.WhenAll(Earlier);
        return [.. done.OfType<Finding>()];
    }
}

/// <summary>
/// One way of explaining a change. Analyses are ordered by <see cref="Order"/> so their effects do not overlap: each works on what the ones before it left.
/// They all start at once, so one that needs an earlier analysis awaits it with <see cref="InsightInput.FindingsBeforeAsync"/> and the rest do not wait for it.
/// An analysis returns null when it does not apply or finds nothing worth saying. It reads Metrics only, never the warehouse.
/// </summary>
public interface IInsightAnalysis
{
    int Order { get; }

    Task<Finding?> RunAsync(InsightInput input, CancellationToken cancellationToken);
}

/// <summary>The thresholds every analysis and the engine share. They are constants so they can be tuned in one place.</summary>
public static class InsightThresholds
{
    /// <summary>A Finding is shown when its impact is at least this share of the size of the change.</summary>
    public const decimal Materiality = 0.10m;

    /// <summary>The most Findings shown.</summary>
    public const int MaxFindings = 5;

    /// <summary>Fewest transports in a batch of unusually high aged payments for it to count as one.</summary>
    public const int MinBatchTransports = 3;

    /// <summary>Fewest transports behind a payer's usual payment age before the aged-payment analysis trusts it.</summary>
    public const int MinBaselineTransports = 10;

    /// <summary>Months of aged payments averaged to get the usual level.</summary>
    public const int BaselineMonths = 6;

    /// <summary>The share of a payer's transports that are paid by the usual age.</summary>
    public const decimal UsualAgePercentile = 0.9m;

    /// <summary>A payer group's share must move at least this much (0 to 1) for payer mix to be worth saying.</summary>
    public const decimal MinShareMove = 0.01m;

    /// <summary>Months of history needed before a remainder can be called normal.</summary>
    public const int BandMonths = 12;
}
