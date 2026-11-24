using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using Prism.Infrastructure.Metrics;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// Reads a Provider's reporting divisions from <c>ods.provider_reporting_division</c> and <c>ods.location</c>. The portal's Facility id is the location's
/// <c>integration_id</c> (checked against a real sign-in: the five Facility ids of Mercy Flights equal the five divisions' integration ids).
/// The Provider is the warehouse one, translated from the portal id first. Cached briefly, with a size cap.
/// </summary>
public sealed class ProviderDivisionDirectory(IWarehouse warehouse, IWarehouseProviderResolver providers) : IProviderDivisionDirectory, IDisposable
{
    internal const string DivisionsSql =
        """
        SELECT l.integration_id
        FROM ods.provider_reporting_division d
        JOIN ods.location l ON l.id = d.location_id
        WHERE d.provider_id = @provider_id AND l.integration_id IS NOT NULL
        LIMIT 500
        """;

    // The same name the net cash breakdown shows as the base, without a "- Old AR ..." suffix, so a legacy division counts under its current one.
    internal const string ReportingDivisionsSql =
        $$"""
        SELECT name
        FROM (
            SELECT DISTINCT {{DivisionSql.NormalizedName}} AS name
            FROM reporting.trip_detail_snapshot
            WHERE provider_id = @provider_id
              AND snapshot_date = (SELECT MAX(snapshot_date) FROM reporting.trip_detail_snapshot WHERE provider_id = @provider_id)
        ) d
        WHERE name IS NOT NULL
        ORDER BY name
        LIMIT 500
        """;

    // The names that have at least one Transport under the Service Line, in the same normalized form as above. Only used to pick from the full list, so keys never change.
    internal const string ServiceLineDivisionsSql =
        $$"""
        SELECT name
        FROM (
            SELECT DISTINCT {{DivisionSql.NormalizedName}} AS name
            FROM reporting.trip_detail_snapshot
            WHERE {{TripSql.LatestLoad}}
              AND {{TripSql.ServiceLine}}
        ) d
        WHERE name IS NOT NULL
        LIMIT 500
        """;

    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly MemoryCache cache = new(new MemoryCacheOptions { SizeLimit = 2000 });
    private readonly MemoryCache divisionCache = new(new MemoryCacheOptions { SizeLimit = 2000 });
    private readonly MemoryCache lineCache = new(new MemoryCacheOptions { SizeLimit = 2000 });

    public async Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken)
    {
        if (cache.TryGetValue(portalCompanyId, out IReadOnlySet<string>? cached) && cached is not null)
        {
            return cached;
        }

        int warehouseId = await providers.ResolveAsync(portalCompanyId, cancellationToken);
        if (warehouseId == IWarehouseProviderResolver.Unmapped)
        {
            return new HashSet<string>();
        }

        IReadOnlyList<long> rows = await warehouse.QueryAsync(
            new WarehouseQuery(DivisionsSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = warehouseId }) { Name = "ProviderDivisionDirectory" },
            reader => reader.GetInt64(0),
            cancellationToken);
        IReadOnlySet<string> ids = rows.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet(StringComparer.Ordinal);
        cache.Set(portalCompanyId, ids, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return ids;
    }

    public async Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken)
    {
        if (divisionCache.TryGetValue(portalCompanyId, out IReadOnlyList<ReportingDivision>? cached) && cached is not null)
        {
            return cached;
        }

        int warehouseId = await providers.ResolveAsync(portalCompanyId, cancellationToken);
        if (warehouseId == IWarehouseProviderResolver.Unmapped)
        {
            return [];
        }

        IReadOnlyList<string> names = await warehouse.QueryAsync(
            new WarehouseQuery(ReportingDivisionsSql, new Dictionary<string, object?> { [WarehouseQuery.ProviderParameter] = warehouseId }) { Name = "ProviderDivisionDirectory#2" },
            reader => reader.GetString(0),
            cancellationToken);
        IReadOnlyList<ReportingDivision> divisions = ReportingDivision.FromNames(names);
        divisionCache.Set(portalCompanyId, divisions, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return divisions;
    }

    public async Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, ServiceLineFilter serviceLine, CancellationToken cancellationToken)
    {
        IReadOnlyList<ReportingDivision> all = await GetReportingDivisionsAsync(portalCompanyId, cancellationToken);
        if (serviceLine == ServiceLineFilter.Both || all.Count == 0)
        {
            return all;
        }

        (int, ServiceLineFilter) cacheKey = (portalCompanyId, serviceLine);
        if (lineCache.TryGetValue(cacheKey, out IReadOnlyList<ReportingDivision>? cached) && cached is not null)
        {
            return cached;
        }

        int warehouseId = await providers.ResolveAsync(portalCompanyId, cancellationToken);
        if (warehouseId == IWarehouseProviderResolver.Unmapped)
        {
            return [];
        }

        IReadOnlyList<string> names = await warehouse.QueryAsync(
            new WarehouseQuery(
                ServiceLineDivisionsSql,
                new Dictionary<string, object?>
                {
                    [WarehouseQuery.ProviderParameter] = warehouseId,
                    ["include_air"] = serviceLine == ServiceLineFilter.Air,
                    ["include_ground"] = serviceLine == ServiceLineFilter.Ground,
                }) { Name = "ProviderDivisionDirectory#3" },
            reader => reader.GetString(0),
            cancellationToken);
        HashSet<string> present = names.ToHashSet(StringComparer.Ordinal);
        IReadOnlyList<ReportingDivision> filtered = [.. all.Where(d => present.Contains(d.Name))];
        lineCache.Set(cacheKey, filtered, new MemoryCacheEntryOptions { Size = 1, AbsoluteExpirationRelativeToNow = Lifetime });
        return filtered;
    }

    public void Dispose()
    {
        lineCache.Dispose();
        cache.Dispose();
        divisionCache.Dispose();
    }
}
