using System.Collections;
using System.Reflection;
using Prism.Application.Attention;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;
using Prism.Infrastructure.Warehouse;

namespace Prism.Tests.Warehouse;

/// <summary>Layer 2 of warehouse write protection: the SQL text policy. Section 4 (resource bounds) and the warehouse rule in AGENTS.md.</summary>
public sealed class WarehouseSqlPolicyTests
{
    private static WarehouseQuery Query(string sql) =>
        new(sql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = 1 });

    public static TheoryData<string> Malicious() =>
    [
        "WITH x AS (DELETE FROM t RETURNING *) SELECT 1 WHERE @provider_id = 1",
        "WITH x AS (INSERT INTO t VALUES (1) RETURNING *) SELECT 1 WHERE @provider_id = 1",
        "WITH x AS (UPDATE t SET a = 1 RETURNING *) SELECT 1 WHERE @provider_id = 1",
        "SELECT set_config('transaction_read_only', 'off', false) WHERE @provider_id = 1",
        "SELECT pg_catalog.set_config('transaction_read_only', 'off', false) WHERE @provider_id = 1",
        "SELECT \"set_config\"('transaction_read_only', 'off', false) WHERE @provider_id = 1",
        "SELECT pg_catalog . set_config ('x', 'y', false) WHERE @provider_id = 1",
        "SELECT * FROM t WHERE provider_id = @provider_id FOR UPDATE",
        "SELECT * FROM t WHERE provider_id = @provider_id FOR NO KEY UPDATE",
        "SELECT * FROM t WHERE provider_id = @provider_id FOR SHARE",
        "SELECT * FROM t WHERE provider_id = @provider_id FOR KEY SHARE",
        "SELECT * FROM t WHERE provider_id = @provider_id for\tshare",
        "SELECT 1 INTO newtable FROM t WHERE provider_id = @provider_id",
        "SELECT nextval('s') WHERE @provider_id = 1",
        "SELECT setval('s', 1) WHERE @provider_id = 1",
        "SELECT * FROM dblink('host=x', 'select 1') AS d(a int) WHERE @provider_id = 1",
        "SELECT dblink_exec('host=x', 'drop table t') WHERE @provider_id = 1",
        "SELECT pg_terminate_backend(1) WHERE @provider_id = 1",
        "SELECT pg_cancel_backend(1) WHERE @provider_id = 1",
        "SELECT pg_reload_conf() WHERE @provider_id = 1",
        "SELECT pg_rotate_logfile() WHERE @provider_id = 1",
        "SELECT pg_switch_wal() WHERE @provider_id = 1",
        "SELECT pg_create_restore_point('x') WHERE @provider_id = 1",
        "SELECT pg_create_logical_replication_slot('s', 'x') WHERE @provider_id = 1",
        "SELECT pg_drop_replication_slot('s') WHERE @provider_id = 1",
        "SELECT pg_advisory_lock(1) WHERE @provider_id = 1",
        "SELECT pg_try_advisory_lock(1) WHERE @provider_id = 1",
        "SELECT pg_notify('c', 'p') WHERE @provider_id = 1",
        "SELECT lo_import('/etc/passwd') WHERE @provider_id = 1",
        "SELECT lo_unlink(1) WHERE @provider_id = 1",
        "SELECT pg_read_file('/etc/passwd') WHERE @provider_id = 1",
        "SELECT pg_read_binary_file('/etc/passwd') WHERE @provider_id = 1",
        "SELECT pg_ls_dir('/') WHERE @provider_id = 1",
        "SELECT pg_stat_file('/') WHERE @provider_id = 1",
        "SELECT pg_sleep(100) WHERE @provider_id = 1",
        "SELECT pg_sleep_for('1 day') WHERE @provider_id = 1",
        "SELECT txid_current() WHERE @provider_id = 1",
        "SELECT pg_export_snapshot() WHERE @provider_id = 1",
        "SELECT query_to_xml('delete from t', true, true, '') WHERE @provider_id = 1",
        "SELECT cursor_to_xml('c', 1, true, true, '') WHERE @provider_id = 1",
        "SELECT 1 WHERE @provider_id = 1 /* harmless */ UNION SELECT 1 /* x */ FOR /**/ UPDATE",
        "SELECT 1/**/FROM/**/t/**/WHERE/**/provider_id=@provider_id/**/FOR/**/SHARE",
        "WITH a AS (SELECT 1) , b AS (DELETE FROM t RETURNING 1) SELECT * FROM a WHERE @provider_id = 1",
        "sElEcT 1 wHeRe @provider_id = 1 UnIoN dElEtE",
        "SELECT 1 WHERE @provider_id = 1 AND EXISTS (SELECT 1 FROM t FOR UPDATE)",
        "CREATE TABLE x AS SELECT 1 WHERE @provider_id = 1",
        "WITH x AS (SELECT 1) TRUNCATE t WHERE @provider_id = 1",
        "SELECT 1 WHERE @provider_id = 1 AND true -- \n GRANT ALL ON t TO u",
        "WITH x AS (SELECT 1)\nDROP TABLE t -- @provider_id",
        "SELECT 1 WHERE @provider_id = 1 AND 1 = (SELECT 1 FROM x WHERE a = ANY (ARRAY[1]) AND b IN (SELECT 1 FROM COPY))",
        "SELECT 1 WHERE @provider_id = 1 OR EXECUTE",
        "SELECT 1 WHERE @provider_id = 1 AND VACUUM",
        "SELECT 1 WHERE @provider_id = 1 AND LISTEN",
        "SELECT 1 WHERE @provider_id = 1 AND DISCARD",
        "SELECT 1 WHERE @provider_id = 1 AND BEGIN",
        "SELECT 1 WHERE @provider_id = 1 AND COMMIT",
        "SELECT 1 WHERE @provider_id = 1 AND ROLLBACK",
        "SELECT 1 WHERE @provider_id = 1 AND SAVEPOINT",
        "SELECT 1 WHERE @provider_id = 1 AND RESET",
        "SELECT 1 WHERE @provider_id = 1 AND SET",
        "SELECT 1 WHERE @provider_id = 1 AND LOCK",
        "SELECT 1 WHERE @provider_id = 1 AND MERGE",
        "SELECT 1 WHERE @provider_id = 1 AND CALL",
        "SELECT 1 WHERE @provider_id = 1 AND DO",
        "SELECT 1 WHERE @provider_id = 1 AND REFRESH",
        "SELECT 1 WHERE @provider_id = 1 AND SECURITY",
        "SELECT 1 WHERE @provider_id = 1 AND PREPARE",
        "SELECT 1 WHERE @provider_id = 1 AND DEALLOCATE",
        "SELECT 1 WHERE @provider_id = 1 AND NOTIFY",
        "SELECT 1 WHERE @provider_id = 1 AND UNLISTEN",
        "SELECT 1 WHERE @provider_id = 1 AND REINDEX",
        "SELECT 1 WHERE @provider_id = 1 AND CLUSTER",
        "SELECT 1 WHERE @provider_id = 1 AND ANALYZE",
        "SELECT 1 WHERE @provider_id = 1 AND COMMENT",
        "SELECT 1 WHERE @provider_id = 1 AND REVOKE",
        "SELECT 1 WHERE @provider_id = 1 AND ALTER",
        // Whitespace and lookalike tricks: a non-ASCII character in code is refused, whether or not PostgreSQL would read it as a space.
        "SELECT 1 WHERE @provider_id = 1;\u00A0DELETE FROM t",
        "WITH x AS (DELETE\u00A0FROM t RETURNING *) SELECT 1 WHERE @provider_id = 1",
        "SELECT 1 WHERE @provider_id = 1 AND \u0399NSERT",
        "SELECT\u2003set_config('a','b',false) WHERE @provider_id = 1",
        "SELECT 1 WHERE @provider_id = 1\u000B",
        "SELECT 1 WHERE @provider_id = 1\0",
        // Things that cannot be read with certainty.
        "SELECT 'unterminated WHERE @provider_id = 1",
        "SELECT 1 /* unterminated WHERE @provider_id = 1",
        "SELECT \"unterminated WHERE @provider_id = 1",
        "SELECT $$unterminated WHERE @provider_id = 1",
        "SELECT U&\"\\0073et_config\"('a','b',false) WHERE @provider_id = 1",
        // A literal or comment closed early by a quote trick leaves the verb in code.
        "SELECT 'a''' , DELETE WHERE @provider_id = 1",
        "SELECT E'a\\'' , DELETE WHERE @provider_id = 1",
        "SELECT $x$ a $x$ , DELETE WHERE @provider_id = 1",
        "SELECT /* a /* nested */ still comment */ 1 , DELETE WHERE @provider_id = 1",
    ];

    [Theory]
    [MemberData(nameof(Malicious))]
    public void A_write_administrative_or_unreadable_query_is_refused(string sql) =>
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(Query(sql)));

    [Theory]
    [MemberData(nameof(Malicious))]
    public void The_policy_alone_names_a_violation_for_each_malicious_text(string sql)
    {
        // The two texts that only fail the older rules (a semicolon) still fail the policy or are covered by Validate above.
        if (sql.Contains(';', StringComparison.Ordinal))
        {
            return;
        }

        Assert.NotNull(WarehouseSqlPolicy.FindViolation(sql));
    }

    public static TheoryData<string> Harmless() =>
    [
        "SELECT 1 WHERE provider_id = @provider_id",
        "SELECT reset_flag, update_count, created_at, into_total, set_id FROM t WHERE provider_id = @provider_id",
        "SELECT 'DELETE FROM t, DROP TABLE t' AS note FROM t WHERE provider_id = @provider_id",
        "SELECT 'it''s an INSERT' FROM t WHERE provider_id = @provider_id",
        "SELECT E'back\\' DELETE' FROM t WHERE provider_id = @provider_id",
        "SELECT $$ DELETE FROM t $$ FROM t WHERE provider_id = @provider_id",
        "SELECT $tag$ set_config('a','b',false) $tag$ AS x FROM t WHERE provider_id = @provider_id",
        "SELECT 1 FROM t WHERE provider_id = @provider_id -- DELETE everything",
        "SELECT 1 /* UPDATE */ FROM t /* /* nested DROP */ */ WHERE provider_id = @provider_id",
        "SELECT \"update\", \"delete\", \"Reset Flag\" FROM t WHERE provider_id = @provider_id",
        "SELECT substring(name FROM 1 FOR 3) FROM t WHERE provider_id = @provider_id",
        "SELECT lo_balance, lo_x1 FROM t WHERE provider_id = @provider_id",
        "SELECT nextval_total, pg_sleep_total FROM t WHERE provider_id = @provider_id",
        "SELECT x::text, $1 FROM t WHERE provider_id = @provider_id",
        "SELECT count(*) FILTER (WHERE a) FROM t WHERE provider_id = @provider_id GROUP BY b ORDER BY 1 LIMIT 10",
        "WITH x AS (SELECT 1 AS n) SELECT n FROM x WHERE @provider_id > 0",
        "SELECT has_table_privilege(c.oid, 'INSERT,UPDATE,DELETE') FROM pg_class c WHERE @provider_id > 0",
    ];

    [Theory]
    [MemberData(nameof(Harmless))]
    public void A_verb_only_inside_a_literal_comment_or_identifier_is_fine(string sql) =>
        NpgsqlWarehouse.Validate(Query(sql));

    [Fact]
    public void The_provider_parameter_must_be_in_code_not_in_a_comment_or_literal()
    {
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(Query("SELECT 1 -- @provider_id")));
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(Query("SELECT '@provider_id'")));
        Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(Query("SELECT 1 /* @provider_id */")));
    }

    [Fact]
    public void A_very_long_or_pathological_text_is_scanned_in_linear_time()
    {
        string sql = "SELECT " + new string('\'', 1) + new string('x', 15_000) + "' FROM t WHERE provider_id = @provider_id";
        Assert.Null(WarehouseSqlPolicy.FindViolation(sql));

        string nested = "SELECT " + string.Concat(Enumerable.Repeat("/*", 5_000)) + string.Concat(Enumerable.Repeat("*/", 5_000)) + " 1 WHERE @provider_id = 1";
        Assert.Null(WarehouseSqlPolicy.FindViolation(nested));
    }

    [Fact]
    public void Every_warehouse_query_in_the_code_passes_the_policy()
    {
        // Discovers every SQL text the Infrastructure assembly holds (constants and static fields whose name ends in Sql), plus the claims query of every check.
        // Queries built at run time from these pieces are run through the same policy by NpgsqlWarehouse on every call, which the integration tests exercise.
        List<(string Name, string Sql)> found = [.. DiscoverSql(), .. Enum.GetValues<AttentionPredicate>().Select(p => ($"ClaimAttentionSql.ClaimsSql({p})", ClaimAttentionSql.ClaimsSql(p)))];

        Assert.True(found.Count >= 20, $"Expected to find the catalog's SQL texts, found {found.Count}.");
        var offenders = new List<string>();
        foreach ((string name, string sql) in found)
        {
            WarehouseSqlAnalysis analysis = WarehouseSqlPolicy.Analyze(sql);
            if (analysis.Violation is not null)
            {
                offenders.Add($"{name}: {analysis.Violation}");
            }

            bool isQuery = sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) || sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase);
            if (isQuery && sql.Contains("@provider_id", StringComparison.Ordinal) && !analysis.UsesProviderParameter)
            {
                offenders.Add($"{name}: @provider_id is not used in code");
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_write_protection_catalog_queries_pass_the_policy_too()
    {
        // They read only the catalog and have no Provider parameter, but they must not trip a verb or a side-effect function.
        foreach (string sql in new[] { WarehouseWriteProtectionCheck.RoleSql, WarehouseWriteProtectionCheck.MembershipSql, WarehouseWriteProtectionCheck.SchemaSql })
        {
            Assert.Null(WarehouseSqlPolicy.FindViolation(sql));
        }
    }

    private static IEnumerable<(string Name, string Sql)> DiscoverSql()
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (Type type in typeof(NpgsqlWarehouse).Assembly.GetTypes())
        {
            foreach (FieldInfo field in type.GetFields(all).Where(f => f.FieldType == typeof(string) && f.Name.EndsWith("Sql", StringComparison.Ordinal)))
            {
                if (field.GetValue(null) is string { Length: > 0 } sql && !IsGeneratedOrMigration(type))
                {
                    yield return ($"{type.Name}.{field.Name}", sql);
                }
            }
        }
    }

    private static bool IsGeneratedOrMigration(Type type) => type.Namespace?.Contains("Persistence", StringComparison.Ordinal) == true || typeof(IEnumerable).IsAssignableFrom(type);
}
