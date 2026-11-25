using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Configuration;

namespace Prism.Infrastructure.Warehouse;

/// <summary>A data source held for the length of one warehouse operation. Disposing it releases it, it does not close the pool.</summary>
public sealed class WarehouseDataSourceLease(NpgsqlDataSource dataSource, IDisposable? release) : IDisposable
{
    public NpgsqlDataSource DataSource => dataSource;

    public void Dispose() => release?.Dispose();
}

/// <summary>Where <see cref="NpgsqlWarehouse"/> gets its connection pool from, so a rotated login can take effect while the host runs.</summary>
public interface IWarehouseConnections
{
    /// <summary>The pool for the current, verified warehouse login. Throws <see cref="WarehouseUnavailableException"/> when a changed login cannot be verified read-only.</summary>
    Task<WarehouseDataSourceLease> AcquireAsync(CancellationToken cancellationToken);
}

/// <summary>One fixed pool, for the single connection string the host started with (and for tests).</summary>
public sealed class FixedWarehouseConnections(NpgsqlDataSource dataSource) : IWarehouseConnections
{
    public Task<WarehouseDataSourceLease> AcquireAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new WarehouseDataSourceLease(dataSource, release: null));
}

/// <summary>
/// Follows <c>ConnectionStrings:Warehouse</c> as a rotated secret changes it. The login the host started with was proven read-only by the startup check.
/// A changed login gets the same proof, over the same catalog queries, <b>before</b> any query uses it: one that can write, create or own anything, that
/// breaks the connection-string rules, or that cannot be checked is refused, and every query fails as "warehouse unavailable" until it is fixed.
/// Queries already running finish on the pool they started on; a retired pool is closed when its last query ends.
/// </summary>
public sealed partial class RotatingWarehouseConnections : IWarehouseConnections, IDisposable
{
    internal static readonly TimeSpan RefusalMemory = TimeSpan.FromSeconds(30);

    private readonly Func<string> currentConnectionString;
    private readonly PrismWarehouseOptions baseOptions;
    private readonly bool deployed;
    private readonly Func<NpgsqlDataSource, PrismWarehouseOptions, CancellationToken, Task> verify;
    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly SemaphoreSlim rotation = new(1, 1);
    private readonly ReloadingResource<NpgsqlDataSource> pools;

    private string verifiedIdentity;
    private NpgsqlDataSource? verifiedButUnused;
    private string? refusedIdentity;
    private DateTimeOffset refusedUntil;

    public RotatingWarehouseConnections(
        Func<string> currentConnectionString,
        PrismWarehouseOptions baseOptions,
        NpgsqlDataSource startupDataSource,
        bool deployed,
        Func<NpgsqlDataSource, PrismWarehouseOptions, CancellationToken, Task> verify,
        TimeProvider timeProvider,
        ILogger logger)
    {
        this.currentConnectionString = currentConnectionString;
        this.baseOptions = baseOptions;
        this.deployed = deployed;
        this.verify = verify;
        this.timeProvider = timeProvider;
        this.logger = logger;
        verifiedIdentity = baseOptions.ConnectionString;
        verifiedButUnused = startupDataSource;
        pools = new ReloadingResource<NpgsqlDataSource>(() => verifiedIdentity, TakeVerified);
    }

    public async Task<WarehouseDataSourceLease> AcquireAsync(CancellationToken cancellationToken)
    {
        string wanted = currentConnectionString();
        if (!string.Equals(wanted, verifiedIdentity, StringComparison.Ordinal))
        {
            await VerifyChangedLoginAsync(wanted, cancellationToken);
        }

        ReloadingResource<NpgsqlDataSource>.Lease lease = pools.Acquire();
        return new WarehouseDataSourceLease(lease.Resource, lease);
    }

    public void Dispose()
    {
        pools.Dispose();
        verifiedButUnused?.Dispose();
        rotation.Dispose();
    }

    private async Task VerifyChangedLoginAsync(string wanted, CancellationToken cancellationToken)
    {
        await rotation.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(wanted, verifiedIdentity, StringComparison.Ordinal))
            {
                return;
            }

            DateTimeOffset now = timeProvider.GetUtcNow();
            if (string.Equals(wanted, refusedIdentity, StringComparison.Ordinal) && now < refusedUntil)
            {
                throw new WarehouseUnavailableException();
            }

            await VerifyAndKeepAsync(wanted, now, cancellationToken);
        }
        finally
        {
            rotation.Release();
        }
    }

    private async Task VerifyAndKeepAsync(string wanted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        PrismWarehouseOptions candidate = Rebuild(wanted);
        NpgsqlDataSource? dataSource = null;
        try
        {
            IReadOnlyList<string> problems = string.IsNullOrWhiteSpace(wanted) ? ["empty"] : PrismWarehouseOptionsRules.Problems(candidate, deployed);
            if (problems.Count > 0)
            {
                throw new WarehouseWriteProtectionException("The rotated warehouse connection string breaks the connection rules.");
            }

            dataSource = WarehouseDataSourceFactory.Create(candidate);
            await verify(dataSource, candidate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            if (dataSource is not null)
            {
                await dataSource.DisposeAsync();
            }

            refusedIdentity = wanted;
            refusedUntil = now + RefusalMemory;
            // The exception type only: a message from the driver or the check is not logged here.
            LogRotationRefused(logger, ex.GetType().Name);
            throw new WarehouseUnavailableException();
        }

        verifiedButUnused = dataSource;
        verifiedIdentity = wanted;
        refusedIdentity = null;
        LogRotationAccepted(logger);
    }

    private PrismWarehouseOptions Rebuild(string connectionString) => new()
    {
        ConnectionString = connectionString,
        MaxPoolSize = baseOptions.MaxPoolSize,
        CommandTimeoutSeconds = baseOptions.CommandTimeoutSeconds,
        MaxRows = baseOptions.MaxRows,
        DevelopmentProviderId = baseOptions.DevelopmentProviderId,
        SkipLoginCheck = baseOptions.SkipLoginCheck,
    };

    private NpgsqlDataSource TakeVerified(string identity)
    {
        NpgsqlDataSource dataSource = verifiedButUnused ?? throw new InvalidOperationException("No verified warehouse connection is waiting.");
        if (!string.Equals(identity, verifiedIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The warehouse connection has not been verified.");
        }

        verifiedButUnused = null;
        return dataSource;
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Error, Message = "Warehouse login rotation refused: the changed connection string failed the read-only check or could not be checked ({FailureType}). Queries fail until it is corrected.")]
    private static partial void LogRotationRefused(ILogger logger, string failureType);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Information, Message = "Warehouse login rotation accepted: the changed connection string passed the read-only check.")]
    private static partial void LogRotationAccepted(ILogger logger);
}
