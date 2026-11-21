using Microsoft.EntityFrameworkCore;

namespace Prism.Tests.Integration.Persistence;

/// <summary>
/// Reads the catalog of the migrated database and checks the EMSMC DBA standard, so a migration that drifts from it
/// fails here instead of in DBA review.
/// </summary>
public sealed class DbaStandardTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const int MaxIdentifierLength = 63;

    private async Task<List<T>> QueryAsync<T>(FormattableString sql)
    {
        await using var db = postgres.NewContext();
        return await db.Database.SqlQuery<T>(sql).ToListAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Every_table_has_an_always_identity_id_and_both_timestamps()
    {
        List<string> missing = await QueryAsync<string>($"""
            SELECT t.table_name AS "Value"
            FROM information_schema.tables t
            WHERE t.table_schema = 'public' AND t.table_type = 'BASE TABLE' AND t.table_name <> '__EFMigrationsHistory'
              AND (SELECT count(*) FROM information_schema.columns c
                   WHERE c.table_schema = t.table_schema AND c.table_name = t.table_name
                     AND ((c.column_name = 'id' AND c.is_identity = 'YES' AND c.identity_generation = 'ALWAYS')
                       OR (c.column_name IN ('created_at_timestamp', 'updated_at_timestamp') AND c.data_type = 'timestamp with time zone'))) <> 3
            """);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Every_table_has_a_timestamp_trigger()
    {
        List<string> missing = await QueryAsync<string>($"""
            SELECT c.relname AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r' AND c.relname <> '__EFMigrationsHistory'
              AND NOT EXISTS (SELECT 1 FROM pg_trigger g WHERE g.tgrelid = c.oid AND g.tgname = 'trg_' || c.relname || '__timestamps')
            """);

        Assert.Empty(missing);
    }

    [Fact]
    public async Task Tables_and_indexes_have_fill_factor_90_and_primary_keys_100()
    {
        List<string> wrong = await QueryAsync<string>($"""
            SELECT c.relname AS "Value"
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_index i ON i.indexrelid = c.oid
            WHERE n.nspname = 'public' AND c.relname <> '__EFMigrationsHistory' AND c.relname NOT LIKE '%EFMigrationsHistory%'
              AND ((c.relkind = 'r' AND NOT ('fillfactor=90' = ANY (coalesce(c.reloptions, ARRAY[]::text[]))))
                OR (c.relkind = 'i' AND i.indisprimary AND NOT ('fillfactor=100' = ANY (coalesce(c.reloptions, ARRAY[]::text[]))))
                OR (c.relkind = 'i' AND NOT i.indisprimary AND NOT ('fillfactor=90' = ANY (coalesce(c.reloptions, ARRAY[]::text[])))))
            """);

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task No_foreign_key_cascades_or_sets_null()
    {
        List<string> wrong = await QueryAsync<string>($"""
            SELECT conname AS "Value" FROM pg_constraint
            WHERE contype = 'f' AND (confdeltype IN ('c', 'n', 'd') OR confupdtype IN ('c', 'n', 'd'))
            """);

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task Names_are_lowercase_snake_case_with_the_right_prefix_and_fit_the_limit()
    {
        List<string> wrong = await QueryAsync<string>($"""
            SELECT conname AS "Value" FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname = 'public' AND conname NOT LIKE '%EFMigrationsHistory%'
              AND (conname !~ '^(pk|fk|uk|ck)_[a-z0-9_]+$' OR length(conname) > {MaxIdentifierLength})
            UNION ALL
            SELECT indexname FROM pg_indexes
            WHERE schemaname = 'public' AND indexname NOT LIKE '%EFMigrationsHistory%'
              AND (indexname !~ '^(pk|ix|uk)_[a-z0-9_]+$' OR length(indexname) > {MaxIdentifierLength})
            """);

        Assert.Empty(wrong);
    }

    [Fact]
    public async Task Each_table_has_at_most_five_indexes()
    {
        List<string> wrong = await QueryAsync<string>($"""
            SELECT tablename AS "Value" FROM pg_indexes WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory'
            GROUP BY tablename HAVING count(*) > 5
            """);

        Assert.Empty(wrong);
    }
}
