using System.Globalization;
using Prism.Application.Admin;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Warehouse;

/// <summary>
/// Every Provider's portal number and display name, read from <c>ods.provider</c> and <c>ods.company</c>. It is the one query that names no Provider: it feeds the staff Provider
/// search and nothing else, and it is read-only, parameterless and capped like every warehouse query. A portal number that more than one warehouse Provider claims is left out,
/// as the Provider resolver would treat it as unmapped.
/// </summary>
public sealed class WarehouseProviderList(IWarehouse warehouse) : IProviderListSource
{
    internal const string ListSql =
        """
        SELECT c.integration_id, p.display_name
        FROM ods.provider p
        JOIN ods.company c ON c.id = p.integration_id
        WHERE c.integration_id IS NOT NULL AND p.display_name IS NOT NULL
        ORDER BY p.display_name
        """;

    internal const int MaxNameLength = 200;

    public async Task<IReadOnlyList<ProviderChoice>> ListAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<(int Id, string Name)> rows = await warehouse.QueryAsync(
            new WarehouseQuery(ListSql, new Dictionary<string, object?>()) { Name = "WarehouseProviderList", AcrossProviders = true },
            reader => (Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture), reader.GetString(1).Trim()),
            cancellationToken);

        return [.. rows
            .Where(row => row.Id > 0 && row.Name.Length > 0)
            .GroupBy(row => row.Id)
            .Where(group => group.Count() == 1)
            .Select(group => new ProviderChoice(group.Key.ToString(CultureInfo.InvariantCulture), group.First().Name.Length <= MaxNameLength ? group.First().Name : group.First().Name[..MaxNameLength]))];
    }
}
