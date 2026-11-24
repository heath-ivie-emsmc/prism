using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>
/// One contributor to the change in net cash: a tail number, a Reporting Division or a payer group (or the roll-up row). <paramref name="Key"/> is a stable key, or null
/// for the roll-up row and for a division the directory does not hold. <paramref name="Name"/> is cleaned warehouse text or a fixed label.
/// </summary>
public sealed record ContributionRow(string? Key, string Name, bool IsOther, decimal NetCash, decimal PriorNetCash)
{
    /// <summary>What this contributor added to (positive) or took from (negative) the change in net cash.</summary>
    public decimal Change => NetCash - PriorNetCash;
}

/// <summary>
/// Net cash posted in <paramref name="Period"/> and in the month before it, split by contributor. <paramref name="Rows"/> hold the largest movers first and, when there are
/// more contributors than <see cref="Contribution.TopRows"/>, one last <c>Other</c> row that sums all the rest. The rows, Other included, add up exactly to
/// <paramref name="NetCash"/> and <paramref name="PriorNetCash"/>, so the contributions always add up to the change.
/// </summary>
public sealed record CashContribution(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    decimal NetCash,
    decimal PriorNetCash,
    IReadOnlyList<ContributionRow> Rows)
{
    public decimal Change => NetCash - PriorNetCash;

    /// <summary>Builds the result from the rows, so the totals are by construction the sum of what is listed.</summary>
    public static CashContribution Of(MetricDefinition metric, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, IReadOnlyList<ContributionRow> rows) =>
        new(metric, period, period.AddMonths(-1), serviceLine, groupKey, rows.Sum(r => r.NetCash), rows.Sum(r => r.PriorNetCash), rows);
}

/// <summary>Limits and names the contribution Metrics share.</summary>
public static class Contribution
{
    /// <summary>The most contributors listed by name. The rest are summed into one Other row.</summary>
    public const int TopRows = 25;

    public const string OtherName = "Other";
}
