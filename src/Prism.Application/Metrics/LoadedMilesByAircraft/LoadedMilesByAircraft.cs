using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One tail number's transports and loaded miles in one month. <see cref="AverageLoadedMiles"/> is null when there were no transports.</summary>
public sealed record AircraftMilesPoint(ApPeriod Period, int Transports, decimal LoadedMiles)
{
    public decimal? AverageLoadedMiles => Transports == 0 ? null : LoadedMiles / Transports;
}

/// <summary>
/// One tail number (or the Other roll-up). <paramref name="Transports"/> and <paramref name="LoadedMiles"/> are for the chosen month, <paramref name="Trend"/> has one
/// point per month of the window, with zeros for months the tail did not fly.
/// </summary>
public sealed record AircraftMilesRow(string Tail, bool IsOther, int Transports, decimal LoadedMiles, IReadOnlyList<AircraftMilesPoint> Trend)
{
    public decimal? AverageLoadedMiles => Transports == 0 ? null : LoadedMiles / Transports;
}

/// <summary>Loaded miles by tail number for one month and a trend over <paramref name="Months"/> months, all by date of service.</summary>
public sealed record LoadedMilesByAircraft(
    MetricDefinition Metric,
    ApPeriod Period,
    int Months,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<AircraftMilesRow> Aircraft);

/// <summary>Loaded miles (statute miles billed with the patient on board) by tail number, with transports, the average per transport and a monthly trend. Top 100 tails plus Other.</summary>
public interface ILoadedMilesByAircraftMetric
{
    public const int TopTails = 100;

    public static MetricDefinition Definition { get; } = new(
        "loaded_miles_by_aircraft",
        "Loaded miles by aircraft",
        "Loaded miles (statute miles billed with the patient on board) for each tail number in the month of service, with transports, the average loaded miles per transport and a monthly trend. The top 100 tail numbers are listed and the rest are summed as Other. Air only.",
        "miles");

    Task<LoadedMilesByAircraft> GetMilesAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
