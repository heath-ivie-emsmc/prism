namespace Prism.Application.Warehouse;

/// <summary>The warehouse tables a Metric reads whose load date says how current its figures are.</summary>
public enum DataThroughSource
{
    /// <summary><c>reporting.trip_detail_snapshot</c>: transports, payer mix, and the ledger Metrics that join it for level of service and division.</summary>
    TripDetail,

    /// <summary><c>reporting.kpi_net_collections_ap__prov</c>: the Provider's whole net cash figure.</summary>
    NetCollectionsKpi,

    /// <summary><c>reporting.forecast_cash_per_trip</c>: the cash projection.</summary>
    CashForecast,

    /// <summary><c>reporting.kpi_clean_claim_rate__prov</c>: the clean claim rate.</summary>
    CleanClaims,
}

/// <summary>
/// The Data Through date of a Provider: the load date (<c>snapshot_date</c>) of the newest load of the table a Metric reads. It is the one answer that the
/// Metric responses, the Metric cache and the HTTP validators share, so they cannot disagree about how current a figure is.
/// </summary>
public interface IDataThroughReader
{
    /// <summary>
    /// The load date for the Provider with this portal id, or null when the Provider has no data in the table (or no warehouse Provider).
    /// When several sources are given the answer is the oldest of their dates, the most cautious claim, and null if any of them has none.
    /// The answer is kept for a short time, so a new load shows within a minute.
    /// </summary>
    Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken);
}
