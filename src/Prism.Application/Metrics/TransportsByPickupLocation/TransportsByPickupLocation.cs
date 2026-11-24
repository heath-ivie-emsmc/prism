using Prism.Application.Warehouse;
namespace Prism.Application.Metrics;

/// <summary>One pickup source (a bucket of the origin modifier) with its transports, miles, posting-month net cash and payer mix, in the chosen month and the one before.</summary>
public sealed record PickupSource(string Key, string Name, bool FacilityBased, int Transports, int PriorTransports, decimal LoadedMiles, decimal PriorLoadedMiles, decimal NetCash, decimal PriorNetCash, IReadOnlyList<PayerCount> Payers);

/// <summary>A sending facility inside a facility-based source, or the one Other row. <paramref name="SourceKey"/> is null for the Other row, which sums facilities across sources.</summary>
public sealed record PickupFacility(string? SourceKey, string Name, bool IsOther, int Transports, int PriorTransports, decimal LoadedMiles);

/// <summary>Transports by where the patient was picked up, by date of service. Aggregate counts only: never an address, city, state or zip.</summary>
public sealed record TransportsByPickupLocation(
    MetricDefinition Metric,
    ApPeriod Period,
    ApPeriod PriorPeriod,
    ServiceLineFilter ServiceLine,
    string? GroupKey,
    IReadOnlyList<PickupSource> Sources,
    IReadOnlyList<PickupFacility> Facilities);

/// <summary>
/// Transports by pickup source. The source is a bucket of <c>origin_mod</c>, the CMS ambulance origin modifier. Scene (S), Residence (R) and Residential or
/// custodial facility (E) are single buckets with no facility name. Facility-based sources are also split by <c>origin_facility</c> (top 20 plus Other).
/// </summary>
public interface ITransportsByPickupLocationMetric
{
    /// <summary>The most facilities listed across the whole result. The rest are summed into one Other row.</summary>
    public const int TopFacilities = 20;

    public const int MaxNameLength = 120;

    public const string OtherSourceKey = "other";

    public const string OtherFacilityName = "Other";

    /// <summary>
    /// The origin modifier values Prism recognizes, in display order. This is general CMS ambulance billing knowledge to be confirmed against the warehouse values,
    /// not verified in Prism. <c>Mods</c> are the single letters of a bucket. <c>FacilityBased</c> is false for S, R and E, which never show a facility name.
    /// </summary>
    public static IReadOnlyList<(string Key, string Name, string Mods, bool FacilityBased)> Sources { get; } =
    [
        ("scene", "Scene", "S", false),
        ("residence", "Residence", "R", false),
        ("custodial-facility", "Residential or custodial facility", "E", false),
        ("hospital", "Hospital", "H", true),
        ("skilled-nursing", "Skilled nursing facility", "N", true),
        ("physician-office", "Physician's office", "P", true),
        ("transfer-site", "Transfer site", "I", true),
        ("diagnostic-site", "Diagnostic or therapeutic site", "D", true),
        ("dialysis", "Dialysis", "GJ", true),
        (OtherSourceKey, "Other", "", false),
    ];

    public static MetricDefinition Definition { get; } = new(
        "transports_by_pickup_location",
        "Transports by pickup location",
        "Transports by where the patient was picked up, from the origin modifier on the claim (scene, residence, hospital and so on), in the month of service and the month before, with loaded miles, the payer mix of each source and net cash by posting month. Hospitals and other facility-based sources also list the sending facilities (top 20 plus Other). Scene and residence pickups are never broken down by name or address.",
        "transports");

    Task<TransportsByPickupLocation> GetPickupsAsync(int providerId, ApPeriod period, ServiceLineFilter serviceLine, string? groupKey, ReportingDivision? division, CancellationToken cancellationToken);
}
