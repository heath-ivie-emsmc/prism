using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Warehouse;

namespace Prism.Tests.Warehouse;

public sealed class WarehouseOptionsTests
{
    private const string Secret = "S3cretPassw0rd!";
    private static string Conn(string extra = "") => $"Host=dw.internal;Database=dw;Username=prism_ro;Password={Secret};{extra}";

    private static PrismWarehouseOptions Options(string connection) => new() { ConnectionString = connection };

    [Fact]
    public void No_connection_is_fine_in_development_and_required_when_deployed()
    {
        Assert.Empty(PrismWarehouseOptionsRules.Problems(Options(""), deployed: false));
        Assert.Contains("required", Assert.Single(PrismWarehouseOptionsRules.Problems(Options(""), deployed: true)), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("SSL Mode=Disable")]
    [InlineData("SSL Mode=Prefer")]
    [InlineData("SSL Mode=Allow")]
    [InlineData("")]
    public void A_deployed_host_must_encrypt_the_connection(string extra)
    {
        var problems = PrismWarehouseOptionsRules.Problems(Options(Conn(extra)), deployed: true);

        Assert.Contains(problems, p => p.Contains("SSL Mode", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("SSL Mode=Require")]
    [InlineData("SSL Mode=VerifyCA")]
    [InlineData("SSL Mode=VerifyFull")]
    public void An_encrypted_connection_passes_when_deployed(string extra) =>
        Assert.Empty(PrismWarehouseOptionsRules.Problems(Options(Conn(extra)), deployed: true));

    [Fact]
    public void Development_may_skip_encryption()
    {
        Assert.Empty(PrismWarehouseOptionsRules.Problems(Options(Conn("SSL Mode=Disable")), deployed: false));
    }

    [Fact]
    public void A_bad_connection_string_is_reported_without_echoing_it()
    {
        var problems = PrismWarehouseOptionsRules.Problems(Options($"Host=x;Password={Secret};NotAKeyword=1"), deployed: false);

        string message = Assert.Single(problems);
        Assert.DoesNotContain(Secret, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, 30, 100)]
    [InlineData(201, 30, 100)]
    [InlineData(20, 0, 100)]
    [InlineData(20, 301, 100)]
    [InlineData(20, 30, 0)]
    [InlineData(20, 30, 1_000_001)]
    public void Limits_stay_in_a_sane_range(int pool, int timeout, int rows)
    {
        var options = new PrismWarehouseOptions { MaxPoolSize = pool, CommandTimeoutSeconds = timeout, MaxRows = rows };

        Assert.NotEmpty(PrismWarehouseOptionsRules.Problems(options, deployed: false));
    }

    [Fact]
    public void The_connection_is_pooled_bounded_read_only_and_named()
    {
        var builder = new NpgsqlConnectionStringBuilder(WarehouseDataSourceFactory.BuildConnectionString(
            new PrismWarehouseOptions { ConnectionString = Conn("SSL Mode=VerifyFull"), MaxPoolSize = 12, CommandTimeoutSeconds = 7 }));

        Assert.Equal("prism-api", builder.ApplicationName);
        Assert.True(builder.Pooling);
        Assert.Equal(12, builder.MaxPoolSize);
        Assert.Equal(7, builder.CommandTimeout);
        Assert.Contains("default_transaction_read_only=on", builder.Options, StringComparison.Ordinal);
        Assert.Contains("statement_timeout=7000", builder.Options, StringComparison.Ordinal);
        Assert.Equal(SslMode.VerifyFull, builder.SslMode);
    }

    [Fact]
    public void A_connection_string_cannot_switch_the_read_only_setting_off()
    {
        var builder = new NpgsqlConnectionStringBuilder(WarehouseDataSourceFactory.BuildConnectionString(
            new PrismWarehouseOptions { ConnectionString = Conn("Options=-c default_transaction_read_only=off") }));

        Assert.DoesNotContain("=off", builder.Options, StringComparison.Ordinal);
    }
}

public sealed class WarehouseQueryRulesTests
{
    private static WarehouseQuery Query(string sql, params (string Name, object? Value)[] parameters) =>
        new(sql, parameters.ToDictionary(p => p.Name, p => p.Value));

    private static void Rejected(WarehouseQuery query) => Assert.Throws<WarehouseQueryRejectedException>(() => NpgsqlWarehouse.Validate(query));

    [Fact]
    public void A_filtered_select_is_accepted()
    {
        NpgsqlWarehouse.Validate(Query("SELECT 1 FROM t WHERE provider_id = @provider_id", ("provider_id", 526)));
        NpgsqlWarehouse.Validate(Query("  with x as (select 1) select * from x, t where t.p = @provider_id and t.d > @from", ("provider_id", 1), ("from", DateTime.UnixEpoch)));
    }

    [Fact]
    public void A_query_without_the_provider_filter_is_rejected() => Rejected(Query("SELECT 1 FROM t", ("provider_id", 1)));

    [Fact]
    public void A_query_that_does_not_pass_the_provider_value_is_rejected() => Rejected(Query("SELECT 1 FROM t WHERE p = @provider_id"));

    [Theory]
    [InlineData("DELETE FROM t WHERE p = @provider_id")]
    [InlineData("UPDATE t SET a = 1 WHERE p = @provider_id")]
    [InlineData("INSERT INTO t VALUES (@provider_id)")]
    [InlineData("DROP TABLE t -- @provider_id")]
    [InlineData("SELECT 1 WHERE @provider_id = 1; DROP TABLE t")]
    [InlineData("")]
    public void Anything_but_a_single_read_is_rejected(string sql) => Rejected(Query(sql, ("provider_id", 1)));

    [Fact]
    public void A_parameter_the_query_does_not_use_is_rejected() =>
        Rejected(Query("SELECT 1 WHERE p = @provider_id", ("provider_id", 1), ("extra", 2)));

    [Fact]
    public void An_oversized_query_is_rejected() => Rejected(Query("SELECT 1 WHERE p = @provider_id " + new string(' ', 20_001), ("provider_id", 1)));
}
