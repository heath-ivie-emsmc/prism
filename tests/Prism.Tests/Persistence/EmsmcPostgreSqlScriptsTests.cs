using Prism.Infrastructure.Persistence;

namespace Prism.Tests.Persistence;

public sealed class EmsmcPostgreSqlScriptsTests
{
    [Fact]
    public void After_tables_created_attaches_triggers_and_sets_primary_key_fill_factor()
    {
        string sql = EmsmcPostgreSqlScripts.AfterTablesCreated;

        Assert.Contains("CREATE OR REPLACE FUNCTION public.fn_set_timestamps()", sql, StringComparison.Ordinal);
        Assert.Contains("'__timestamps'", sql, StringComparison.Ordinal);
        Assert.Contains("BEFORE INSERT OR UPDATE", sql, StringComparison.Ordinal);
        Assert.Contains("SET (fillfactor = 100)", sql, StringComparison.Ordinal);
        Assert.Contains("IN ('public')", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void Created_timestamp_is_immutable_in_the_trigger_function()
    {
        Assert.Contains(
            "NEW.created_at_timestamp = OLD.created_at_timestamp;",
            EmsmcPostgreSqlScripts.FnSetTimestampsCreateOrReplace,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Multiple_schemas_are_listed()
    {
        string sql = EmsmcPostgreSqlScripts.AfterTablesCreatedFor("public", "reporting");

        Assert.Contains("IN ('public', 'reporting')", sql, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Public")]
    [InlineData("public'; DROP TABLE x; --")]
    [InlineData("")]
    [InlineData("a b")]
    public void Unsafe_schema_names_are_rejected(string schema)
    {
        Assert.Throws<ArgumentException>(() => EmsmcPostgreSqlScripts.AfterTablesCreatedFor(schema));
    }

    [Fact]
    public void At_least_one_schema_is_required()
    {
        Assert.Throws<ArgumentException>(() => EmsmcPostgreSqlScripts.AfterTablesCreatedFor());
    }
}
