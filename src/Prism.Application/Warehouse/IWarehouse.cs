using System.Data.Common;

namespace Prism.Application.Warehouse;

/// <summary>
/// One read against the data warehouse. The SQL is a fixed string written in the Metric Catalog, never built from request values.
/// Every value reaches the database as a bound parameter. The Provider is always the <c>@provider_id</c> parameter, set by the Catalog
/// from the route after the user's assignments were checked, and the SQL has to filter on it.
/// </summary>
public sealed record WarehouseQuery(string Sql, IReadOnlyDictionary<string, object?> Parameters)
{
    /// <summary>The one parameter name every query uses for the Provider.</summary>
    public const string ProviderParameter = "provider_id";
}

/// <summary>
/// Read-only access to the data warehouse. Prism never writes to it and never migrates it.
/// Implementations enforce the timeout, the row cap and the Provider filter, so no caller has to remember to.
/// </summary>
public interface IWarehouse
{
    /// <summary>
    /// Runs the query and maps each row with <paramref name="map"/>.
    /// Throws <see cref="WarehouseUnavailableException"/> when no warehouse is configured or it cannot be reached,
    /// <see cref="WarehouseQueryRejectedException"/> when the query breaks a rule, and <see cref="WarehouseResultTooLargeException"/>
    /// when it returns more rows than allowed.
    /// </summary>
    Task<IReadOnlyList<T>> QueryAsync<T>(WarehouseQuery query, Func<DbDataReader, T> map, CancellationToken cancellationToken);
}
