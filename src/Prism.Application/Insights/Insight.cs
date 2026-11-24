using Prism.Application.Metrics;

namespace Prism.Application.Insights;

/// <summary>How the part of the change that no Driver explains compares with how much this group's cash normally moves from month to month.</summary>
public enum RemainderJudgment
{
    /// <summary>It is inside the usual month-to-month range of the last twelve months.</summary>
    WithinNormalRange,

    /// <summary>It is outside that range, so the drivers we can measure do not explain it.</summary>
    OutsideNormalRange,

    /// <summary>There are not twelve months of history, so nothing is said about whether it is normal.</summary>
    NotEnoughHistory,
}

/// <summary>The part of the change in posted cash that no Driver explains, and whether it looks normal.</summary>
public sealed record Remainder(decimal Amount, RemainderJudgment Judgment);

/// <summary>
/// The change being explained: posted net cash for the group in <paramref name="Period"/> against <paramref name="PriorPeriod"/>.
/// <paramref name="DeltaRatio"/> is the change as a share of last month's cash (0.22 is 22%), or null when last month had no cash.
/// <paramref name="DivisionName"/> is the Reporting Division the figures are limited to, or null for every division.
/// </summary>
public sealed record InsightHeadline(
    ApPeriod Period,
    ApPeriod PriorPeriod,
    string? GroupKey,
    string GroupName,
    decimal CashNow,
    decimal CashPrior,
    decimal Delta,
    decimal? DeltaRatio,
    string? DivisionName = null);

/// <summary>
/// The Findings for one group and period. <paramref name="Drivers"/> are posted-cash Findings, ranked by size, and with <paramref name="Remainder"/> they
/// add up to the headline change. <paramref name="Expected"/> are Findings on another basis, shown beside the Drivers and never added to them.
/// <paramref name="NormalizedDeltaRatio"/> is the change with unusually high aged payments taken out of both months, or null when there were none.
/// </summary>
public sealed record Insight(
    InsightHeadline Headline,
    IReadOnlyList<Finding> Drivers,
    IReadOnlyList<Finding> Expected,
    Remainder Remainder,
    decimal? NormalizedDeltaRatio);
