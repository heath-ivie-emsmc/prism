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

    internal const string CountsSql =
        $"""
        SELECT COUNT(*) FILTER (WHERE {DeniedRule}) AS denied,
               COUNT(*) FILTER (WHERE {NeedsAttentionRule}) AS needs_attention,
               COUNT(*) AS all_claims,
               COALESCE(SUM(balance_due) FILTER (WHERE {DeniedRule}), 0) AS denied_balance,
               MAX(snapshot_date) AS data_through
        FROM {Table}
        WHERE {Scope}
        """;

    internal static string ClaimsSql(AttentionPredicate predicate) =>
        $"""
        SELECT encounter_id,
               COALESCE(NULLIF(trim(run_number), ''), encounter_id::text) AS claim_ref,
               dos, prim_pay, prim_pay_category, vehicle, reporting_division, status,
               denial_code, denial_reason, balance_due, snapshot_date
        FROM {Table}
        WHERE {Scope}
          AND {RuleFor(predicate)}
        ORDER BY dos, encounter_id
        """;

    private static string RuleFor(AttentionPredicate predicate) => predicate switch
    {
        AttentionPredicate.Denied => DeniedRule,
        AttentionPredicate.NeedsAttention => NeedsAttentionRule,
        AttentionPredicate.All => "TRUE",
        _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
    };
}
