namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The net cash rule as a SQL fragment, shared by every Metric that adds up the ledger. Net cash is what the warehouse's net collections KPI means:
/// credit transactions whose credit is a payment or refund (types 2 and 5), by the accounting period they posted in, without <c>Old AR Import</c>.
/// A transaction counts once, however many credit attributes it has. One copy means the breakdown, payment age and cash per transport can never
/// disagree about what counts as cash.
/// </summary>
internal static class NetCashSql
{
    private const string CashHead =
        """
        cash AS (
            SELECT et.encounter_id, p.ym, SUM(et.balance_amount) / 100.0 AS amount
            FROM ods.encounter_transaction et
            JOIN periods p ON p.id = et.accounting_period_id
            WHERE et.type = 'Credit' AND et.user_source IS DISTINCT FROM 'Old AR Import'
        """;

    private const string CashTail =
        """
              AND EXISTS (
                  SELECT 1
                  FROM ods.encounter_transaction_credit_attribute a
                  JOIN ods.credit c ON c.id = a.credit_id
                  WHERE a.encounter_transaction_id = et.id AND c.credit_type_id IN (2, 5))
            GROUP BY et.encounter_id, p.ym
        )
        """;

    /// <summary>
    /// A <c>cash</c> CTE of (<c>encounter_id</c>, <c>ym</c>, <c>amount</c>). The query defines a <c>periods</c> CTE of the Provider's accounting
    /// periods (<c>id</c>, <c>ym</c>) before it, and that is where the Provider filter and the months come from.
    /// </summary>
    internal const string CashCte = CashHead + "\n" + CashTail;

    /// <summary>
    /// The same rule as <see cref="CashCte"/>, but only for the encounters in a <c>trips</c> CTE (with an <c>encounter_id</c>) defined before it.
    /// Use it when the periods reach far back, so the database reads the transactions of those encounters and not the Provider's whole ledger.
    /// </summary>
    internal const string CashCteForTrips = CashHead + "\n      AND et.encounter_id IN (SELECT encounter_id FROM trips)\n" + CashTail;
}
