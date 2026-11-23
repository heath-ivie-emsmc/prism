using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One payer inside a payer group and its transport counts. <paramref name="Name"/> is the roll-up label "Other" for the payers beyond the top ones.</summary>
public sealed record PayerMixDetailPayer(string Name, int Current, int Prior);

/// <summary>
/// The payers behind one payer group of the payer mix, for one date-of-service month and the month before: the top payers by transports, then one
/// "Other" row for the rest, so the rows always add up to that group's share on the payer mix.
/// </summary>
public sealed record PayerMixDetail(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    string? GroupName,
    string PayerGroupKey,
    string PayerGroupName,
    IReadOnlyList<PayerMixDetailPayer> Payers);

/// <summary>
/// The payer detail behind a payer mix group. Only insurance-type groups have named payers worth showing: Facility and Patient responsibility are the
/// transport's billing party, not a payer to compare, and Not recorded has no name, so those three are refused (<see cref="IsDrillable"/>).
/// Counts only. Payer names are the one text value returned, trimmed, with control characters removed and a length cap.
/// </summary>
public interface IPayerMixDetailMetric
{
    /// <summary>How many named payers are listed before the rest are rolled into "Other".</summary>
    public const int TopPayers = 10;

    /// <summary>The longest payer name returned. A longer warehouse value is cut.</summary>
    public const int MaxNameLength = 120;

    /// <summary>The roll-up label for payers beyond the top ones.</summary>
    public const string OtherName = "Other";

    public static MetricDefinition Definition { get; } = new(
        "payer_mix_detail",
        "Payer mix detail",
        "Transports by payer within one payer group (Commercial, Medicare, Medicaid, Other government or VA): the top payers and the rest as Other, in the month of service and the month before.",
        "transports");

    /// <summary>The payer group keys that have a detail view.</summary>
    public static IReadOnlyList<string> DrillableGroups { get; } = ["commercial", "medicare", "medicaid", "other-gov", "va"];

    /// <summary>True only for a payer group key that has payer detail. Facility, Patient responsibility, Not recorded and anything unknown are not.</summary>
    public static bool IsDrillable(string? payerGroupKey) =>
        payerGroupKey is not null && DrillableGroups.Contains(payerGroupKey, StringComparer.Ordinal);

    Task<PayerMixDetail> GetDetailAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, string payerGroupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
