namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The payer grouping every Metric shares, as a SQL fragment over <c>reporting.trip_detail_snapshot</c> columns. The payer name decides VA first:
/// the warehouse files VA and TRICARE payers under Insurance and Facility as often as under Other Govt. Payers. Keeping one copy means payer mix,
/// cash per transport and payment age always agree on who is Commercial, Medicare, Medicaid and so on.
/// </summary>
internal static class PayerGroupSql
{
    /// <summary>A <c>CASE</c> expression giving the payer group key. It reads <c>prim_pay</c> and <c>prim_pay_category</c> from the query's own trips.</summary>
    internal const string CaseExpression =
        """
        CASE
                       WHEN prim_pay ~* '\m(VA|CHAMP[A-Z]*|TRICARE)\M' THEN 'va'
                       WHEN prim_pay_category IN ('Insurance', 'TPL') THEN 'commercial'
                       WHEN prim_pay_category IN ('Medicare', 'Medicare Advantage') THEN 'medicare'
                       WHEN prim_pay_category IN ('Medicaid', 'Medicaid MCO') THEN 'medicaid'
                       WHEN prim_pay_category = 'Other Govt. Payers' THEN 'other-gov'
                       WHEN prim_pay_category = 'Facility' THEN 'facility'
                       WHEN prim_pay_category = 'Patient' THEN 'patient'
                       ELSE 'not-recorded'
                   END
        """;
}
