using System.Data.Common;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Warehouse;

/// <summary>Reads the warehouse over a pooled, read-only, time-limited connection. See <see cref="IWarehouse"/> for the rules it enforces.</summary>
public sealed partial class NpgsqlWarehouse(NpgsqlDataSource dataSource, IOptions<PrismWarehouseOptions> options, ILogger<NpgsqlWarehouse> logger) : IWarehouse
{
    private const int MaxSqlLength = 20_000;

    public Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(map);
        Validate(query);

        return RunAsync(query, map, cancellationToken);
    }

    private async Task<IReadOnlyList<T>> RunAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken)
    {
        int maxRows = options.Value.MaxRows;
        long started = Stopwatch.GetTimestamp();
        try
        {
            await using NpgsqlCommand command = dataSource.CreateCommand(query.Sql);
            command.CommandTimeout = options.Value.CommandTimeoutSeconds;
            foreach ((string name, object? value) in query.Parameters)
            {
                command.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<T> rows = [];
            while (await reader.ReadAsync(cancellationToken))
            {
                if (rows.Count >= maxRows)
                {
                    throw new WarehouseResultTooLargeException(maxRows);
                }

                rows.Add(map(reader));
            }

            LogCompleted(logger, query.Name, rows.Count, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return rows;
        }
        // A transient failure, a timeout on the client, or the server's own statement timeout (query_canceled) all mean "not now", not "your query is wrong".
        catch (NpgsqlException ex) when (ex is not PostgresException postgres || postgres.IsTransient || postgres.SqlState == PostgresErrorCodes.QueryCanceled)
        {
            // Connection and timeout failures. Npgsql messages never carry the password, and the SQL is not logged.
            LogUnavailable(logger, ex, query.Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, (ex as PostgresException)?.SqlState ?? "none");
            throw new WarehouseUnavailableException(ex);
        }
        // Any other database error is a bug in the query. Only the SQL state code is logged: the message can quote values.
        catch (PostgresException ex)
        {
            LogFailed(logger, query.Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds, ex.SqlState);
            throw;
        }
    }

    /// <summary>Rules every query must keep, checked before it leaves the process. A guard test checks the Catalog's queries against the same rules.</summary>
    internal static void Validate(WarehouseQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Sql) || query.Sql.Length > MaxSqlLength)
        {
            throw new WarehouseQueryRejectedException("The query is empty or too long.");
        }

        if (query.Sql.Contains(';', StringComparison.Ordinal))
        {
            throw new WarehouseQueryRejectedException("A query must be a single statement with no semicolon.");
        }

        if (!StartsWithRead().IsMatch(query.Sql))
        {
            throw new WarehouseQueryRejectedException("A query must start with SELECT or WITH.");
        }

        if (!ProviderParameterUse().IsMatch(query.Sql))
        {
            throw new WarehouseQueryRejectedException("A query must filter on @provider_id.");
        }

        if (!query.Parameters.ContainsKey(WarehouseQuery.ProviderParameter))
        {
            throw new WarehouseQueryRejectedException("A query must pass the provider_id parameter.");
        }

        if (query.Parameters.Keys.Any(name => !query.Sql.Contains('@' + name, StringComparison.Ordinal)))
        {
            throw new WarehouseQueryRejectedException("A parameter is passed that the query does not use.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The data warehouse is unavailable for query {Query} after {ElapsedMs} ms (SQL state {SqlState})")]
    private static partial void LogUnavailable(ILogger logger, Exception exception, string query, long elapsedMs, string sqlState);

    [LoggerMessage(Level = LogLevel.Error, Message = "The data warehouse refused query {Query} after {ElapsedMs} ms (SQL state {SqlState})")]
    private static partial void LogFailed(ILogger logger, string query, long elapsedMs, string sqlState);

    [LoggerMessage(Level = LogLevel.Information, Message = "Warehouse query {Query} returned {Rows} rows in {ElapsedMs} ms")]
    private static partial void LogCompleted(ILogger logger, string query, int rows, long elapsedMs);

    [GeneratedRegex(@"^\s*(select|with)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex StartsWithRead();

    [GeneratedRegex(@"@provider_id\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex ProviderParameterUse();
}

/// <summary>Used when no warehouse connection is configured (Development without the secret). Every query says so plainly.</summary>
public sealed class UnconfiguredWarehouse : IWarehouse
{
    public Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken) =>
        throw new WarehouseUnavailableException();
}
