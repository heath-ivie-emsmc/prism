using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>Transports and loaded miles in one month of service. Months with no transports are left out of the series, not returned as zero.</summary>
public sealed record TransportVolumePoint(ApPeriod Period, int Transports, decimal LoadedMiles);

/// <summary>Transports in the last month of the series for one aircraft type or level of service.</summary>
public sealed record TransportVolumeGroup(string Key, string Name, bool IsAir, int Transports);

/// <summary>
/// Transports and loaded miles for a run of months ending at <paramref name="EndPeriod"/>, by date of service, and the split of the last month
/// by aircraft type or level of service. <paramref name="PeriodOpen"/> is true while the accounting period of the last month is still open, so
/// transports for it may still be added.
/// </summary>
public sealed record TransportVolume(
    MetricDefinition Metric,
    ApPeriod EndPeriod,
    int Months,
    ServiceLineFilter ServiceLine,
    bool PeriodOpen,
    IReadOnlyList<TransportVolumePoint> Points,
    IReadOnlyList<TransportVolumeGroup> Groups);

/// <summary>
/// Transports and loaded miles by month. A transport belongs to the month it happened (its date of service), so this differs from the net cash
/// breakdown, which counts the encounters that had cash posted in the month. Loaded miles are statute miles, the miles billed with the patient on board.
/// </summary>
public interface ITransportVolumeMetric
{
    public const int MaxMonths = 24;

    public static MetricDefinition Definition { get; } = new(
        "transport_volume",
        "Transports and loaded miles",
        "Transports by month of service and the loaded miles billed with the patient on board, in statute miles, with the last month split by aircraft type or level of service.",
        "transports");

    Task<TransportVolume> GetVolumeAsync(int providerId, ApPeriod endPeriod, int months, ServiceLineFilter serviceLine, ReportingDivision? division, CancellationToken cancellationToken);
}
