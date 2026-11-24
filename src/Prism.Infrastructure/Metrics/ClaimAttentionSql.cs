using Prism.Application.Attention;

namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The warehouse columns and row rules behind the Attention Checks, in one place so a warehouse rename is a one-line change.
/// The select lists name every column: the table also holds patient names, SSNs, birth dates and addresses, and none of them may be read.
/// </summary>
internal static class ClaimAttentionSql
{
    internal const string Table = "reporting.trip_detail_snapshot";

    internal const string DeniedRule = "(COALESCE(denial_code, '') <> '' AND balance_due > 0)";

    internal const string NeedsAttentionRule = $"({DeniedRule} OR status = 'On_Hold' OR in_collections = 'Yes')";

    /// <summary>The service-line, aircraft-type, period and latest-load filter shared by every read.</summary>
    internal const string Scope =
        $"""
        provider_id = @provider_id
          AND snapshot_date = (SELECT MAX(snapshot_date) FROM {Table} WHERE provider_id = @provider_id)
          AND dos >= @period_start AND dos < @period_end
          AND ((@include_air AND level_of_service IN ('Rotor Wing', 'Fixed Wing'))
               OR (@include_ground AND COALESCE(level_of_service, '') NOT IN ('Rotor Wing', 'Fixed Wing')))
          AND (@group_key = '' OR lower(replace(trim(COALESCE(level_of_service, '')), ' ', '-')) = @group_key)
        """;

    internal const string OnHoldRule = "(status = 'On_Hold')";

    internal const string InCollectionsRule = "(in_collections = 'Yes')";

    // Only positive balances are added, so a credit never reduces what is owed.
    internal const string OwedBalance = "GREATEST(COALESCE(balance_due, 0), 0)";

    internal const string CountsSql =
        $"""
        SELECT COUNT(*) FILTER (WHERE {DeniedRule}) AS denied,
               COUNT(*) FILTER (WHERE {NeedsAttentionRule}) AS needs_attention,
               COUNT(*) AS all_claims,
               COALESCE(SUM(balance_due) FILTER (WHERE {DeniedRule}), 0) AS denied_balance,
               MAX(snapshot_date) AS data_through,
               COUNT(*) FILTER (WHERE {OnHoldRule}) AS on_hold,
               COALESCE(SUM({OwedBalance}) FILTER (WHERE {OnHoldRule}), 0) AS on_hold_balance,
               COUNT(*) FILTER (WHERE {InCollectionsRule}) AS in_collections,
               COALESCE(SUM({OwedBalance}) FILTER (WHERE {InCollectionsRule}), 0) AS in_collections_balance
        FROM {Table}
        WHERE {Scope}
        """;

    // Reason text is untrusted warehouse text: control characters and whitespace runs become one space, it is trimmed, cut to 120 characters (the input is bounded first),
    // trimmed again and, if nothing is left, called "No reason given". A real reason called "Other" is shown as "Other reason" so it cannot be mistaken for the summary row.
    internal static readonly string ReasonExpression =
        $"COALESCE(NULLIF(btrim(left(btrim(regexp_replace(left(denial_reason, 2000), '[[:cntrl:][:space:]]+', ' ', 'g')), {AttentionCounts.ReasonMaxLength})), ''), '{AttentionCounts.NoReasonGiven}')";

    internal static readonly string ReasonsSql =
        $"""
        WITH denied AS (
            SELECT CASE WHEN lower(cleaned) = 'other' THEN 'Other reason' ELSE cleaned END AS reason, balance_due
            FROM (SELECT {ReasonExpression} AS cleaned, balance_due FROM {Table} WHERE {Scope} AND {DeniedRule}) AS cleaned_rows
        ),
        grouped AS (
            SELECT reason, COUNT(*) AS cnt, SUM(balance_due) AS bal FROM denied GROUP BY reason
        ),
        ranked AS (
            SELECT reason, cnt, bal, row_number() OVER (ORDER BY cnt DESC, bal DESC, reason) AS rn FROM grouped
        )
        SELECT reason, false AS is_other, cnt, bal, rn AS ord FROM ranked WHERE rn <= {AttentionCounts.MaxReasons}
        UNION ALL
        SELECT 'Other', true, SUM(cnt), SUM(bal), {AttentionCounts.MaxReasons + 1} FROM ranked WHERE rn > {AttentionCounts.MaxReasons} HAVING COUNT(*) > 0
        ORDER BY ord
        """;

    // The needs-attention claims, banded by days left (derived due date minus the load date, the same rule FilingDeadline applies). Due today is 0 days left, so it is in the 10 day band, not past due.
    internal static readonly string FilingBandsSql =
        $"""
        SELECT COUNT(*) FILTER (WHERE days_left < 0) AS past_due,
               COALESCE(SUM(owed) FILTER (WHERE days_left < 0), 0) AS past_due_balance,
               COUNT(*) FILTER (WHERE days_left >= 0 AND days_left <= {FilingBands.SoonDays}) AS soon,
               COALESCE(SUM(owed) FILTER (WHERE days_left >= 0 AND days_left <= {FilingBands.SoonDays}), 0) AS soon_balance,
               COUNT(*) FILTER (WHERE days_left > {FilingBands.SoonDays} AND days_left <= {FilingBands.NearDays}) AS near,
               COALESCE(SUM(owed) FILTER (WHERE days_left > {FilingBands.SoonDays} AND days_left <= {FilingBands.NearDays}), 0) AS near_balance,
               COUNT(*) FILTER (WHERE days_left > {FilingBands.NearDays}) AS later,
               COALESCE(SUM(owed) FILTER (WHERE days_left > {FilingBands.NearDays}), 0) AS later_balance
        FROM (
            SELECT (dos::date + COALESCE(filing.days, @default_limit_days)) - snapshot_date::date AS days_left, {OwedBalance} AS owed
            FROM {Table}
            LEFT JOIN LATERAL (
                SELECT l.days
                FROM unnest(@limit_categories::text[], @limit_days::int[]) AS l(category, days)
                WHERE lower(l.category) = lower(prim_pay_category)
                LIMIT 1) AS filing ON TRUE
            WHERE {Scope} AND {NeedsAttentionRule}
        ) AS banded
        """;

    // The filing limit per payer category is passed as two parallel arrays and joined here, so the order, the page and the total all come from the database:
    // the deadline is the date of service plus the category's limit (or the default), the same rule FilingDeadline applies. dos is cast so a timestamp column also works.
    private static string BuildClaimsSql(AttentionPredicate predicate) =>
        $"""
        SELECT encounter_id,
               COALESCE(NULLIF(trim(run_number), ''), encounter_id::text) AS claim_ref,
               dos, prim_pay, prim_pay_category, vehicle, reporting_division, status,
               denial_code, denial_reason, balance_due, snapshot_date,
               COUNT(*) OVER () AS total
        FROM {Table}
        LEFT JOIN LATERAL (
            SELECT l.days
            FROM unnest(@limit_categories::text[], @limit_days::int[]) AS l(category, days)
            WHERE lower(l.category) = lower(prim_pay_category)
            LIMIT 1) AS filing ON TRUE
        WHERE {Scope}
          AND {RuleFor(predicate)}
        ORDER BY (dos::date + COALESCE(filing.days, @default_limit_days)), dos, encounter_id
        LIMIT CAST(@page_size AS bigint) OFFSET CAST(@page_offset AS bigint)
        """;

    private static readonly string DeniedClaimsSql = BuildClaimsSql(AttentionPredicate.Denied);
    private static readonly string NeedsAttentionClaimsSql = BuildClaimsSql(AttentionPredicate.NeedsAttention);
    private static readonly string AllClaimsSql = BuildClaimsSql(AttentionPredicate.All);

    /// <summary>The claims query for a check. Built once per predicate: the text is fixed, so it is also stable for prepared statements.</summary>
    internal static string ClaimsSql(AttentionPredicate predicate) => predicate switch
    {
        AttentionPredicate.Denied => DeniedClaimsSql,
        AttentionPredicate.NeedsAttention => NeedsAttentionClaimsSql,
        AttentionPredicate.All => AllClaimsSql,
        _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
    };

    private static string RuleFor(AttentionPredicate predicate) => predicate switch
    {
        AttentionPredicate.Denied => DeniedRule,
        AttentionPredicate.NeedsAttention => NeedsAttentionRule,
        AttentionPredicate.All => "TRUE",
        _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
    };
}
