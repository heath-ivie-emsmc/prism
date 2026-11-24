using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One tail number (or the roll-up row), with its figures. <paramref name="Tail"/> is cleaned warehouse text, or <c>Not recorded</c>, or <c>Other</c> for the roll-up.</summary>
public sealed record AircraftRow(string Tail, bool IsOther, PeriodFigures Figures);

/// <summary>Transports, loaded miles and net cash by tail number for one month and the month before. <paramref name="Totals"/> adds up every row, including Other.</summary>
public sealed record AircraftPerformance(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    PeriodFigures Totals,
    IReadOnlyList<AircraftRow> Aircraft);

/// <summary>
/// Performance by tail number (the warehouse's <c>vehicle</c>): transports and loaded miles by date of service, net cash by posting month, and net cash per
/// transport (posting-month cash divided by transports, not the expected payment). The top 100 tails by transports are listed and one Other row sums the rest.
/// </summary>
public interface IAircraftPerformanceMetric
{
    /// <summary>The most tail numbers listed. The rest are summed into one Other row.</summary>
    public const int TopTails = 100;

    public const int MaxNameLength = 120;

    public const string OtherName = "Other";

    public const string NotRecordedName = "Not recorded";

    public static MetricDefinition Definition { get; } = new(
        "aircraft_performance",
        "Aircraft performance",
        "Transports and loaded miles (by date of service) and net cash (by posting month) for each tail number, with the change from the month before and net cash per transport (posting-month cash divided by transports, not the expected payment). The top 100 tail numbers are listed and the rest are summed as Other. Air only.",
        "transports");

    Task<AircraftPerformance> GetPerformanceAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
