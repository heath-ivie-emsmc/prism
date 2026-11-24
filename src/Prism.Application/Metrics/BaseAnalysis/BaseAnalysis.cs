using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>The transports a tail number flew from one Reporting Division in the month. Aggregate counts only.</summary>
public sealed record DivisionTail(string Tail, bool IsOther, int Transports);

/// <summary>
/// One Reporting Division (or the roll-up row) with its figures and the tail numbers operating from it. <paramref name="Key"/> is the division key from the
/// Provider's directory, null for the Other row, for transports with no division, and for a name the directory does not hold.
/// </summary>
public sealed record DivisionRow(string? Key, string Name, bool IsOther, PeriodFigures Figures, IReadOnlyList<DivisionTail> Aircraft);

/// <summary>Transports, loaded miles and net cash by Reporting Division for one month and the month before. <paramref name="Totals"/> adds up every row.</summary>
public sealed record BaseAnalysis(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    PeriodFigures Totals,
    IReadOnlyList<DivisionRow> Divisions);

/// <summary>
/// Figures by Reporting Division: transports and loaded miles by date of service, net cash by posting month, net cash per transport (posting-month cash divided
/// by transports, not the expected payment), and the top 10 tail numbers by transports in each division plus Other.
/// </summary>
public interface IBaseAnalysisMetric
{
    /// <summary>The most divisions listed. The rest are summed into one Other row (which lists no tails).</summary>
    public const int TopDivisions = 100;

    /// <summary>The most tail numbers listed per division. The rest are summed into one Other entry.</summary>
    public const int TopTailsPerDivision = 10;

    public static MetricDefinition Definition { get; } = new(
        "base_analysis",
        "Reporting Division analysis",
        "Transports and loaded miles (by date of service) and net cash (by posting month) for each Reporting Division, with the change from the month before, net cash per transport (posting-month cash divided by transports, not the expected payment) and the tail numbers operating from each division with their transport counts (top 10 plus Other).",
        "transports");

    Task<BaseAnalysis> GetAnalysisAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
