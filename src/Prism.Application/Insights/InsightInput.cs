using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>Net cash and the encounters it came from in one posting month, for the group being explained.</summary>
public sealed record MonthlyCash(ApPeriod Period, decimal Cash, int Transports);

/// <summary>
/// What one analysis needs to know about the change being explained. <paramref name="History"/> is the group's net cash by posting month for the
/// twelve months before <paramref name="Period"/> and the month itself. <paramref name="Earlier"/> holds the Findings of the analyses that ran before this
/// one, so a later analysis can work on what an earlier one left.
/// </summary>
public sealed record InsightInput(
    int ProviderId,
    ApPeriod Period,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<MonthlyCash> History,
    IReadOnlyList<Finding> Earlier)
{
    public ApPeriod PriorPeriod => Period.AddMonths(-1);

    public MonthlyCash? Current => History.FirstOrDefault(m => m.Period == Period);

    public MonthlyCash? Prior => History.FirstOrDefault(m => m.Period == PriorPeriod);

    public InsightInput After(Finding finding) => this with { Earlier = [.. Earlier, finding] };
}

/// <summary>
/// One way of explaining a change. Analyses run in <see cref="Order"/> so their effects do not overlap: each works on what the ones before it left.
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
