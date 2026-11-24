using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One call type's transports and miles (date of service), net cash (posting month) and payer mix, in the chosen month and the one before.</summary>
public sealed record CallTypeRow(string Key, string Name, int Transports, int PriorTransports, decimal LoadedMiles, decimal PriorLoadedMiles, decimal NetCash, decimal PriorNetCash, IReadOnlyList<PayerCount> Payers);

/// <summary>The transports of one call type in one month of the trend.</summary>
public sealed record CallTypeCount(string Key, int Transports);

/// <summary>One month of the trend: the transports of each call type. The shares are the counts over their total.</summary>
public sealed record CallTypeTrendPoint(ApPeriod Period, IReadOnlyList<CallTypeCount> Counts);

/// <summary>
/// Transports by call type for one month of service and the month before, with the payer mix of each call type and a monthly trend. <paramref name="GroupKey"/>
/// narrows it to one aircraft type or level of service. Net cash by call type is by posting month and is a separate figure from the transport counts.
/// </summary>
public sealed record TransportMixByCallType(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    int Months,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<CallTypeRow> CallTypes,
    IReadOnlyList<CallTypeTrendPoint> Trend);

/// <summary>
/// Transports by call type: scene and interfacility for air, emergency and non-emergency for ground (the warehouse's <c>response_type</c>). Expected payment per
/// transport is not in the warehouse and is not estimated here.
/// </summary>
public interface ITransportMixByCallTypeMetric
{
    /// <summary>The call types in a fixed order, as key and display name.</summary>
    public static IReadOnlyList<(string Key, string Name)> CallTypes { get; } =
    [
        ("scene", "Scene"),
        ("interfacility", "Interfacility"),
        ("emergency", "Emergency"),
        ("non-emergency", "Non-emergency"),
        ("not-recorded", "Not recorded"),
    ];

    public static MetricDefinition Definition { get; } = new(
        "transport_mix_by_call_type",
        "Transport mix by call type",
        "Transports by call type (scene or interfacility for air, emergency or non-emergency for ground) in the month of service and the month before, with loaded miles, the payer mix of each call type and a monthly trend of the shares. Transports and miles are by date of service. Net cash by call type is by posting month and is shown apart. Expected payment per transport is not available in the warehouse and is not estimated.",
        "transports");

    Task<TransportMixByCallType> GetMixAsync(int providerId, ApPeriod period, int months, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
