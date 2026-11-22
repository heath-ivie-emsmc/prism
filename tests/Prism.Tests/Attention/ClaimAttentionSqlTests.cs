using Prism.Application.Attention;
using Prism.Infrastructure.Metrics;

namespace Prism.Tests.Attention;

/// <summary>The claim queries must never name a patient column. The table holds them; these tests keep them out of every select list and rule.</summary>
public sealed class ClaimAttentionSqlTests
{
    private static readonly string[] PatientColumns =
    [
        "patient", "ssn", "dob", "haddr", "hcity", "hstate", "hzip", "patient_number", "patient_deceaseddate", "patient_groups", "prim_pay_member_id", "prim_pay_group_id",
        "origin_address", "origin_city", "origin_zip", "origin_facility", "destination_address1", "destination_city", "destination_zip", "destination_facility",
        "primary_icd", "secondary_icd", "medical_record_author", "signer", "approving_biller_name", "medical_necessity_questionnaire",
    ];

    public static TheoryData<string> Queries()
    {
        var data = new TheoryData<string> { ClaimAttentionSql.CountsSql };
        foreach (AttentionPredicate predicate in Enum.GetValues<AttentionPredicate>())
        {
            data.Add(ClaimAttentionSql.ClaimsSql(predicate));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void A_query_names_no_patient_column(string sql)
    {
        foreach (string column in PatientColumns)
        {
            Assert.DoesNotMatch($@"\b{column}\b", sql);
        }
    }

    [Theory]
    [MemberData(nameof(Queries))]
    public void A_query_is_one_parameterized_read_scoped_by_provider_and_the_latest_load(string sql)
    {
        Assert.StartsWith("SELECT", sql.TrimStart(), StringComparison.Ordinal);
        Assert.Contains("provider_id = @provider_id", sql, StringComparison.Ordinal);
        Assert.Contains("MAX(snapshot_date)", sql, StringComparison.Ordinal);
        Assert.DoesNotContain(';', sql);
    }
}
