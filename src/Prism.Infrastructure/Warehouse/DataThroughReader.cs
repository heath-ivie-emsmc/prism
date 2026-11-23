using Microsoft.Extensions.Caching.Memory;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// Reads the newest <c>snapshot_date</c> a Provider has in a reporting table. Each read filters on the warehouse Provider id (the Catalog sets it, translated from
/// the portal id the caller was authorized for) and goes through <see cref="IWarehouse"/>, so it has the same timeout, row cap and read-only session as every Metric.
/// The answer is kept for 60 seconds by the injected clock in a cache with a size cap, so the many Metric reads of one dashboard load cost one lookup per table.
/// </summary>
public sealed class DataThroughReader(IWarehouse warehouse, IWarehouseProviderResolver providers, TimeProvider time) : IDataThroughReader, IDisposable
{
    internal const int CacheSize = 2000;
    internal static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    internal const string TripDetailSql =
        "SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id";

    // The Metrics read only the current load of these two, so the date is the date of the current load.
    internal const string NetCollectionsKpiSql =
        "SELECT MAX(snapshot_date) FROM reporting.kpi_net_collections_ap__prov WHERE provider_id = @provider_id AND is_current";

    internal const string CashForecastSql =
        "SELECT MAX(snapshot_date) FROM reporting.forecast_cash_per_trip WHERE provider_id = @provider_id AND is_current";

    internal const string CleanClaimsSql =
        "SELECT MAX(snapshot_date) FROM reporting.kpi_clean_claim_rate__prov WHERE provider_id = @provider_id";

    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = CacheSize });

    public async Task<DateOnly?> GetAsync(int providerId, IReadOnlyList<DataThroughSource> sources, CancellationToken cancellationToken)
    {
        if (sources.Count == 0)
        {
            return null;
        }

        int warehouseId = await providers.ResolveAsync(providerId, cancellationToken);
        if (warehouseId == IWarehouseProviderResolver.Unmapped)
        {
            return null;
        }

        DateOnly? oldest = null;
        foreach (DataThroughSource source in sources.Distinct())
        {
            DateOnly? date = await ReadAsync(warehouseId, source, cancellationToken);
            if (date is not { } value)
            {
                return null;
            }

            oldest = oldest is { } current && current <= value ? current : value;
        }

        return oldest;
    }

    public void Dispose() => cache.Dispose();

    private static string SqlFor(DataThroughSource source) => source switch
    {
        DataThroughSource.TripDetail => TripDetailSql,
        DataThroughSource.NetCollectionsKpi => NetCollectionsKpiSql,
        DataThroughSource.CashForecast => CashForecastSql,
        DataThroughSource.CleanClaims => CleanClaimsSql,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown Data Through source."),
    };

    private async Task<DateOnly?> ReadAsync(int warehouseId, DataThroughSource source, CancellationToken cancellationToken)
    {
        var key = (warehouseId, source);
        if (cache.TryGetValue(key, out Entry? hit) && hit is not null && hit.ExpiresAt > time.GetUtcNow())
        {
            return hit.Date;
        }

        IReadOnlyList<DateOnly?> rows = await warehouse.QueryAsync(
            new WarehouseQuery(SqlFor(source), new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = warehouseId }) { Name = $"DataThroughReader.{source}" },
            reader => reader.IsDBNull(0) ? (DateOnly?)null : DateOnly.FromDateTime(reader.GetDateTime(0)),
            cancellationToken);
        DateOnly? date = rows.Count > 0 ? rows[0] : null;
        // The clock decides when an answer is stale. The cache's own lifetime is only a ceiling that lets old entries be dropped.
        cache.Set(key, new Entry(date, time.GetUtcNow() + Lifetime), new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime * 2 });
        return date;
    }

    /// <summary>Wraps the date so that "no data" is kept too.</summary>
    private sealed record Entry(DateOnly? Date, DateTimeOffset ExpiresAt);
}
