using Prism.Application.Warehouse;

namespace Prism.Application.Metrics;

/// <summary>
/// Which warehouse tables each Metric reads, so its Data Through is the load date of those tables. The ledger Metrics (breakdown, payment age, cash per
/// transport, by group) read the transaction ledger, which has no load date, and join the trip table, so the trip table's date is the one that moves
/// with their figures. Defined once here so the response, the cache key and the validators use the same source.
/// </summary>
public static class MetricDataThrough
{
    private static readonly DataThroughSource[] Trips = [DataThroughSource.TripDetail];
    private static readonly DataThroughSource[] Kpi = [DataThroughSource.NetCollectionsKpi];
    private static readonly DataThroughSource[] Forecast = [DataThroughSource.CashForecast];
    private static readonly DataThroughSource[] Claims = [DataThroughSource.CleanClaims];

    /// <summary>The key of the "what changed" Insight. Every analysis behind it (net cash by group, payment age, payer mix, cash per transport, volume) reads the trip table, so that is its source.</summary>
    public const string WhatChanged = "what_changed";

    /// <summary>The key of the claim-level Attention Checks. They read the trip table, so it is the trip table's date. The responses are never cached.</summary>
    public const string ClaimAttention = "claim_attention";

    /// <summary>
    /// The sources for a Metric. Net cash is the one Metric that reads two tables, by Service Line: the KPI table for the Provider's whole figure, and the
    /// ledger joined to the trip table when one Service Line is asked for. An unknown key reads the trip table, the commonest source.
    /// </summary>
    public static IReadOnlyList<DataThroughSource> SourcesFor(string metricKey, ServiceLineFilter serviceLine) => metricKey switch
    {
        "net_collections" => serviceLine is ServiceLineFilter.Both ? Kpi : Trips,
        "net_cash_projection" => Forecast,
        "clean_claim_rate" => Claims,
        WhatChanged => Trips,
        ClaimAttention => Trips,
        _ => Trips,
    };
}
