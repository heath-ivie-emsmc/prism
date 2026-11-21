using System.Text.RegularExpressions;

namespace Prism.Infrastructure.Persistence;

/// <summary>
/// EMSMC PostgreSQL scripts for the things EF cannot express: the timestamp trigger function, a trigger on every table
/// that has <c>created_at_timestamp</c> and <c>updated_at_timestamp</c>, and primary key fill factor 100.
/// Run <see cref="AfterTablesCreated"/> from every migration that creates a table. Both parts are idempotent.
/// </summary>
/// <remarks>
/// Ported from the M6 project (<c>EmsmcPostgreSqlScripts</c>). The timestamp columns are <c>timestamp with time zone</c>
/// and the function assigns <c>now()</c>. Assigning <c>now() AT TIME ZONE 'UTC'</c> (as the DBA skill text shows) yields
/// a <c>timestamp without time zone</c> that PostgreSQL then coerces with the session time zone, shifting stored
/// instants when a client is not on UTC. This divergence from the skill text is deliberate and needs DBA sign-off.
/// </remarks>
public static partial class EmsmcPostgreSqlScripts
{
    public const string DefaultSchema = "public";

    private const int MaxIdentifierLength = 63;

    /// <summary>Replaces <c>fn_set_timestamps</c> only. The function lives in <c>public</c> and is shared by every schema.</summary>
    public const string FnSetTimestampsCreateOrReplace = """
CREATE OR REPLACE FUNCTION public.fn_set_timestamps()
RETURNS trigger AS $$
BEGIN
    IF TG_OP = 'INSERT' THEN
        NEW.created_at_timestamp = now();
        NEW.updated_at_timestamp = now();
    ELSIF TG_OP = 'UPDATE' THEN
        NEW.created_at_timestamp = OLD.created_at_timestamp;
        NEW.updated_at_timestamp = now();
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

""";

    /// <summary>Function plus triggers for the <c>public</c> schema.</summary>
    public static string TimestampTriggers => TimestampTriggersFor(DefaultSchema);

    /// <summary>Primary key fill factor 100 for the <c>public</c> schema.</summary>
    public static string PrimaryKeyFillFactor => PrimaryKeyFillFactorFor(DefaultSchema);

    /// <summary>Everything a migration needs after creating tables, for the <c>public</c> schema.</summary>
    public static string AfterTablesCreated => AfterTablesCreatedFor(DefaultSchema);

    public static string AfterTablesCreatedFor(params string[] schemas) =>
        TimestampTriggersFor(schemas) + PrimaryKeyFillFactorFor(schemas);

    public static string TimestampTriggersFor(params string[] schemas) =>
        FnSetTimestampsCreateOrReplace + $$"""
DO $$
DECLARE
    r RECORD;
    trg text;
BEGIN
    FOR r IN
        SELECT c.table_schema, c.table_name
        FROM information_schema.columns c
        WHERE c.table_schema IN ({{SchemaList(schemas)}})
          AND c.column_name = 'created_at_timestamp'
          AND EXISTS (
              SELECT 1 FROM information_schema.columns c2
              WHERE c2.table_schema = c.table_schema
                AND c2.table_name = c.table_name
                AND c2.column_name = 'updated_at_timestamp')
        ORDER BY c.table_schema, c.table_name
    LOOP
        trg := left('trg_' || r.table_name || '__timestamps', {{MaxIdentifierLength}});
        EXECUTE format('DROP TRIGGER IF EXISTS %I ON %I.%I', trg, r.table_schema, r.table_name);
        EXECUTE format(
            'CREATE TRIGGER %I BEFORE INSERT OR UPDATE ON %I.%I FOR EACH ROW EXECUTE FUNCTION public.fn_set_timestamps()',
            trg, r.table_schema, r.table_name);
    END LOOP;
END $$;

""";

    public static string PrimaryKeyFillFactorFor(params string[] schemas) => $$"""
DO $$
DECLARE r RECORD;
BEGIN
    FOR r IN
        SELECT n.nspname AS schema_name, ic.relname AS index_name
        FROM pg_index i
        JOIN pg_class ic ON ic.oid = i.indexrelid
        JOIN pg_namespace n ON n.oid = ic.relnamespace
        WHERE i.indisprimary
          AND n.nspname IN ({{SchemaList(schemas)}})
    LOOP
        EXECUTE format('ALTER INDEX %I.%I SET (fillfactor = 100)', r.schema_name, r.index_name);
    END LOOP;
END $$;

""";

    // Schema names are interpolated into SQL, so only plain lowercase identifiers are accepted.
    private static string SchemaList(string[] schemas)
    {
        ArgumentNullException.ThrowIfNull(schemas);
        if (schemas.Length == 0)
        {
            throw new ArgumentException("At least one schema is required.", nameof(schemas));
        }

        if (schemas.Any(schema => schema is null || !SchemaNamePattern().IsMatch(schema)))
        {
            throw new ArgumentException(
                "Schema names must be lowercase snake_case identifiers of at most 63 characters.",
                nameof(schemas));
        }

        return string.Join(", ", schemas.Select(schema => $"'{schema}'"));
    }

    [GeneratedRegex("^[a-z][a-z0-9_]{0,62}$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SchemaNamePattern();
}
