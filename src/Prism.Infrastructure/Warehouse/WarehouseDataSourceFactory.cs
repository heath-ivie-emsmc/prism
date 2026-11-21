using Npgsql;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// The only place a connection to the warehouse is created. A guard test fails if any other code builds an Npgsql data source or connection,
/// so these safety settings cannot be skipped. Prism's own database is a separate connection and never shares this pool.
/// </summary>
public static class WarehouseDataSourceFactory
{
    public const string ApplicationName = "prism-api";

    /// <summary>The connection string Prism actually uses: the configured one plus the settings that make it safe and bounded.</summary>
    public static string BuildConnectionString(PrismWarehouseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var builder = new NpgsqlConnectionStringBuilder(options.ConnectionString)
        {
            ApplicationName = ApplicationName,
            Pooling = true,
            MaxPoolSize = options.MaxPoolSize,
            CommandTimeout = options.CommandTimeoutSeconds,
            Timeout = 15,
            // Read-only for every session on this connection, and a server-side time limit as a second line behind the client one.
            // The database login should also have SELECT only: that is the guarantee, these are belt and braces.
            Options = $"-c default_transaction_read_only=on -c statement_timeout={options.CommandTimeoutSeconds * 1000} -c idle_in_transaction_session_timeout=60000",
        };
        return builder.ConnectionString;
    }

    public static NpgsqlDataSource Create(PrismWarehouseOptions options)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(BuildConnectionString(options));
        return dataSourceBuilder.Build();
    }
}
