namespace Prism.Infrastructure.Metrics;

/// <summary>
/// SQL fragments over <c>reporting.trip_detail_snapshot</c> that the fleet, call type and pickup Metrics share, next to <see cref="DivisionSql"/>, <see cref="PayerGroupSql"/>
/// and <see cref="NetCashSql"/>. They only name columns and bound parameters (<c>@provider_id</c>, <c>@include_air</c>, <c>@include_ground</c>, <c>@group_key</c>,
/// <c>@max_name</c>), never a request value.
/// </summary>
internal static class TripSql
{
    /// <summary>The Provider's rows from the newest load. One row per encounter.</summary>
    internal const string LatestLoad =
        "provider_id = @provider_id AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)";

    /// <summary>The Service Line filter. Air is Rotor Wing or Fixed Wing, everything else is ground.</summary>
    internal const string ServiceLine =
        "((@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing')) OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))";

    /// <summary>The optional aircraft type or level of service, by the same key the breakdown route returns. An empty <c>@group_key</c> is every group.</summary>
    internal const string Group =
        "(@group_key = '' OR lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) = @group_key)";

    // Warehouse text made safe to return: control characters and runs of spaces become one space, the text is trimmed and cut to @max_name characters,
    // and an empty or missing value becomes a fallback literal written in the Metric, never a request value.
    private const string CleanHead = "COALESCE(NULLIF(left(trim(regexp_replace(COALESCE(";
    private const string CleanMid = ", ''), '[[:cntrl:][:space:]]+', ' ', 'g')), @max_name), ''), '";

    /// <summary>The tail number (the warehouse's <c>vehicle</c>), cleaned, or <c>Not recorded</c>.</summary>
    internal const string CleanTail = CleanHead + "vehicle" + CleanMid + "Not recorded')";

    /// <summary>The sending facility (the warehouse's <c>origin_facility</c>), cleaned, or <c>Not recorded</c>.</summary>
    internal const string CleanFacility = CleanHead + "origin_facility" + CleanMid + "Not recorded')";

    /// <summary>The Provider's accounting periods for the chosen month and the one before (and their adjustment periods), for <see cref="NetCashSql.CashCte"/>.</summary>
    internal const string TwoPeriods =
        """
        periods AS (
            SELECT id, replace(name, '-', '') AS ym
            FROM ods.accounting_period
            WHERE provider_id = @provider_id AND name IN (@period, @prior, '-' || @period, '-' || @prior)
        )
        """;
}

/// <summary>The call type of a trip: scene or interfacility for air, emergency or non-emergency for ground, from <c>response_type</c>. Anything else is <c>not-recorded</c>.</summary>
internal static class CallTypeSql
{
    internal const string CaseExpression =
        """
        CASE WHEN level_of_service IN ('Rotor Wing', 'Fixed Wing')
                    THEN CASE WHEN response_type ILIKE 'Scene%' THEN 'scene' WHEN response_type ILIKE 'Interfacility%' THEN 'interfacility' ELSE 'not-recorded' END
                    ELSE CASE WHEN response_type = 'Emergency' THEN 'emergency' WHEN response_type = 'Non-Emergency' THEN 'non-emergency' ELSE 'not-recorded' END
                END
        """;
}

/// <summary>
/// The pickup source of a trip, from <c>origin_mod</c> (the CMS ambulance origin modifier). A single letter from the whitelist maps to a source key and anything else, a blank
/// value or more than one character is <c>other</c>. The mapping is general CMS ambulance billing knowledge, to be confirmed against the warehouse values, and it matches
/// <see cref="Prism.Application.Metrics.ITransportsByPickupLocationMetric.Sources"/> (a test keeps the two in step). It never reads an address, city, state or zip column.
/// </summary>
internal static class PickupSql
{
    internal const string SourceCase =
        """
        CASE WHEN length(trim(origin_mod)) = 1
                    THEN CASE upper(trim(origin_mod))
                             WHEN 'S' THEN 'scene' WHEN 'R' THEN 'residence' WHEN 'E' THEN 'custodial-facility'
                             WHEN 'H' THEN 'hospital' WHEN 'N' THEN 'skilled-nursing' WHEN 'P' THEN 'physician-office'
                             WHEN 'I' THEN 'transfer-site' WHEN 'D' THEN 'diagnostic-site' WHEN 'G' THEN 'dialysis' WHEN 'J' THEN 'dialysis'
                             ELSE 'other' END
                    ELSE 'other' END
        """;

    /// <summary>The sources that may be broken down by sending facility: not scene, residence, custodial facility or other.</summary>
    internal const string FacilityBasedKeys = "('hospital', 'skilled-nursing', 'physician-office', 'transfer-site', 'diagnostic-site', 'dialysis')";
}
