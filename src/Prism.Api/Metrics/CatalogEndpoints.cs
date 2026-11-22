using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;

namespace Prism.Api.Metrics;

/// <summary>The Metric Catalog as the Saved Views palette reads it: categories, Metrics and how each can be drawn. Definitions only, never data.</summary>
internal static class CatalogEndpoints
{
    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/metrics")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Metrics");

        group.MapGet("/catalog", Catalog)
            .WithName("GetMetricCatalog")
            .WithSummary("The Metric Catalog")
            .WithDescription(
                "Every Metric a Saved View can show, grouped by `categories`. `status` is `available` or `planned`: a planned Metric is listed so the palette can show it as coming soon, and cannot be placed. "
                + "`placeable` is false for something computed on the dashboard only, such as `what_changed`. `dimensions` are the filters the Metric honors (`period`, `months`, `serviceLine`, `group`, `division`); "
                + "a Metric that does not list `division` ignores the Reporting Division filter. `presentations` are the ways it can be drawn, each with the preset sizes (`S`, `M`, `L`) that suit it and its bounds in columns and rows of the 12-column grid. "
                + "`options` are the settings a widget exposes, as `choice` or `toggle`. `claimLevel` marks a Metric that would carry claim detail and so stays planned until it has its own permission and an audit record. "
                + "Definitions only: no figures, and the same for every Provider the caller can read.")
            .Produces<MetricCatalogResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static async Task<IResult> Catalog(string providerId, ClaimsPrincipal user, ProviderAccess access, CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(user, providerId, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        return Results.Ok(new MetricCatalogResponse(
            MetricCatalog.Categories.Select(c => new CatalogCategoryResponse(c.Key, c.Name)).ToArray(),
            MetricCatalog.Entries.Select(Describe).ToArray()));
    }

    private static CatalogMetricResponse Describe(MetricCatalogEntry entry) => new(
        entry.Key,
        entry.Name,
        entry.Description,
        entry.Unit,
        entry.Category,
        entry.Status == MetricStatus.Available ? "available" : "planned",
        entry.CanBePlaced,
        entry.ClaimLevel,
        entry.ServiceLines,
        entry.Dimensions,
        entry.Presentations.Select(p => new CatalogPresentationResponse(p.Key, p.Name, p.Icon, p.Sizes, p.MinWidth, p.MinHeight, p.MaxWidth, p.MaxHeight, p.IsDefault)).ToArray(),
        entry.Options.Select(o => new CatalogOptionResponse(o.Key, o.Name, o.Type, o.Choices, o.Default, o.AppliesTo)).ToArray());
}

/// <summary>The catalog: <c>Categories</c> in palette order and every <c>Metrics</c> entry.</summary>
public sealed record MetricCatalogResponse(IReadOnlyList<CatalogCategoryResponse> Categories, IReadOnlyList<CatalogMetricResponse> Metrics);

/// <summary>A palette group. <c>Key</c> is stable, <c>Name</c> is for display.</summary>
public sealed record CatalogCategoryResponse(string Key, string Name);

/// <summary>One Metric. <c>Key</c> is the stable identifier a layout stores. <c>Status</c> is <c>available</c> or <c>planned</c>.</summary>
public sealed record CatalogMetricResponse(
    string Key,
    string Name,
    string Description,
    string Unit,
    string Category,
    string Status,
    bool Placeable,
    bool ClaimLevel,
    IReadOnlyList<string> ServiceLines,
    IReadOnlyList<string> Dimensions,
    IReadOnlyList<CatalogPresentationResponse> Presentations,
    IReadOnlyList<CatalogOptionResponse> Options);

/// <summary>A way to draw a Metric, with its preset sizes and its bounds on the 12-column grid.</summary>
public sealed record CatalogPresentationResponse(
    string Key,
    string Name,
    string Icon,
    IReadOnlyList<string> Sizes,
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    bool IsDefault);

/// <summary>A widget setting. <c>Type</c> is <c>choice</c> or <c>toggle</c>, <c>Default</c> is one of <c>Choices</c> for a choice, or <c>true</c> or <c>false</c> for a toggle.</summary>
public sealed record CatalogOptionResponse(string Key, string Name, string Type, IReadOnlyList<string> Choices, string Default, IReadOnlyList<string> AppliesTo);
