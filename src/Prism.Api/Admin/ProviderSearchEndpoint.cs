using Prism.Application.Admin;

namespace Prism.Api.Admin;

/// <summary>Lets an Internal User find a Provider by name or number. It lists the whole portal directory, so it is for staff only; a Provider found here is still checked by whatever route uses it.</summary>
internal static class ProviderSearchEndpoint
{
    public static void Map(RouteGroupBuilder v1) =>
        v1.MapGet("/admin/providers", Search)
            .RequireAuthorization(AdminServiceCollectionExtensions.InternalPolicy)
            .WithName("SearchProviders")
            .WithTags("Admin")
            .WithSummary("Find a Provider by name or number")
            .WithDescription(
                "Returns up to 20 Providers whose name contains `q` (any case) or, when `q` is digits, whose number starts with it. `q` is trimmed and cut to 100 characters; an empty `q` returns none. "
                + "Example: `GET /api/v1/admin/providers?q=abbev` returns `{\"providers\":[{\"id\":\"9001\",\"name\":\"Abbeville County Emergency Services\"}]}`. Returns 503 when the Provider list cannot be read from the warehouse.")
            .Produces<ProviderSearchResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> Search(string? q, IProviderCatalog catalog, CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ProviderChoice> found = await catalog.SearchAsync(q ?? "", cancellationToken);
            return Results.Ok(new ProviderSearchResponse(found));
        }
        catch (Exception ex) when (ex is Prism.Application.Warehouse.WarehouseUnavailableException or Prism.Application.Warehouse.WarehouseResultTooLargeException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "The Provider list is not available right now.");
        }
    }
}

/// <summary>The Providers that matched, at most 20.</summary>
public sealed record ProviderSearchResponse(IReadOnlyList<ProviderChoice> Providers);
