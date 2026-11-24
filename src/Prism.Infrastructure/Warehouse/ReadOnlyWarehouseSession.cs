using System.Data;
using Npgsql;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// The third layer of warehouse write protection: every read runs in an explicit transaction whose first statement is
/// <c>SET TRANSACTION READ ONLY</c>, so the server refuses a write (SQL state 25006) however the session default was changed, and the transaction is
/// always rolled back, never committed. The database login having SELECT only is the real guarantee; this makes the protection independent of a session setting.
/// </summary>
internal sealed class ReadOnlyWarehouseSession : IAsyncDisposable
{
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;

    private ReadOnlyWarehouseSession(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    internal static async Task<ReadOnlyWarehouseSession> BeginAsync(NpgsqlDataSource dataSource, int commandTimeoutSeconds, CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                await using var readOnly = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction) { CommandTimeout = commandTimeoutSeconds };
                await readOnly.ExecuteNonQueryAsync(cancellationToken);
                return new ReadOnlyWarehouseSession(connection, transaction);
            }
            catch
            {
                await transaction.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal NpgsqlCommand CreateCommand(string sql, int commandTimeoutSeconds) =>
        new(sql, connection, transaction) { CommandTimeout = commandTimeoutSeconds };

    /// <summary>Rolls back (the transaction never commits) and returns the connection to the pool.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (NpgsqlException)
        {
            // A broken connection has nothing left to roll back. It is closed below and the pool discards it.
        }
        catch (InvalidOperationException)
        {
            // The connection was already closed by the failure that brought us here.
        }
        finally
        {
            await transaction.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
