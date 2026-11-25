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
            .AddEndpointFilter(MetricCacheHeaders.Catalog)
            .WithTags("Metrics");

        group.MapGet("/catalog", Catalog)
            .WithName("GetMetricCatalog")
            .WithSummary("The Metric Catalog")
            .WithDescription(
                "Every Metric a Saved View can show, grouped by `categories`. `status` is `available` or `planned`: a planned Metric is listed so the palette can show it as coming soon, and cannot be placed. "
                + "`placeable` is false for something computed on the dashboard only, such as `what_changed`. `dimensions` are the filters the Metric honors (`period`, `months`, `serviceLine`, `group`, `division`); "
                + "a Metric that does not list `division` ignores the Reporting Division filter. `presentations` are the ways it can be drawn, each with the preset sizes (`S`, `M`, `L`) that suit it and its bounds in columns and rows of the 12-column grid. "
                + "`options` are the settings a widget exposes, as `choice` or `toggle`. `claimLevel` marks a Metric that would carry claim detail and so stays planned until it has its own permission and an audit record. "
                + "`context` says where the Metric applies: `providerScoped`, `serviceLines`, `divisionSupport` (`none` or `supported`), `groupDimensions`, `timeGrain` (`period` or `month`) and `appliesTo` (`air`, `ground` or `both`: an air-only Metric treats `serviceLine=all` as air and refuses `serviceLine=ground` with a 400). "
                + "`freshness.policy` is `dailyLoad` (cached against Data Through) or `noCache` (claim-level). `helpKey` names the Metric's help entry, and `drills` lists links to other Metrics. "
                + "Definitions only: no figures, and the same for every Provider the caller can read.")
            .Produces<MetricCatalogResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    public static async Task<IResult> Catalog(string providerId, ClaimsPrincipal user, ProviderAccess access, CancellationToken cancellationToken)
    {
        if (await access.ResolveAggregateAsync(user, providerId, cancellationToken) is null)
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
        entry.Options.Select(o => new CatalogOptionResponse(o.Key, o.Name, o.Type, o.Choices, o.Default, o.AppliesTo)).ToArray(),
        new CatalogContextResponse(
            entry.Context.ProviderScoped,
            entry.Context.ServiceLines,
            entry.Context.DivisionSupport == DivisionSupport.Supported ? "supported" : "none",
            entry.Context.GroupDimensions,
            entry.Context.TimeGrain,
            entry.Context.AppliesTo.ToString().ToLowerInvariant()),
        new CatalogFreshnessResponse(entry.Freshness.Policy == FreshnessPolicy.NoCache ? "noCache" : "dailyLoad"),
        entry.HelpKey,
        entry.Drills.Select(d => new CatalogDrillResponse(d.Rel, d.TargetKey, d.BindsDimension)).ToArray());
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
    IReadOnlyList<CatalogOptionResponse> Options,
    CatalogContextResponse Context,
    CatalogFreshnessResponse Freshness,
    string HelpKey,
    IReadOnlyList<CatalogDrillResponse> Drills);

/// <summary>
/// Where a Metric applies. <c>DivisionSupport</c> is <c>none</c> or <c>supported</c>, <c>TimeGrain</c> is <c>period</c> or <c>month</c>. <c>AppliesTo</c> is <c>air</c>, <c>ground</c> or <c>both</c>:
/// an air-only or ground-only Metric has data for that Service Line only, so <c>serviceLine=all</c> means that line and the other line is a 400.
/// <c>ServiceLines</c> lists the <c>serviceLine</c> values it accepts: air and both, ground and both, or air, ground and both.
/// </summary>
public sealed record CatalogContextResponse(bool ProviderScoped, IReadOnlyList<string> ServiceLines, string DivisionSupport, IReadOnlyList<string> GroupDimensions, string TimeGrain, string AppliesTo);

/// <summary>Caching policy. <c>Policy</c> is <c>dailyLoad</c> or <c>noCache</c>.</summary>
public sealed record CatalogFreshnessResponse(string Policy);

/// <summary>A link to another Metric: <c>Rel</c> names it, <c>TargetKey</c> is the Metric, <c>BindsDimension</c> is the dimension the clicked value fills.</summary>
public sealed record CatalogDrillResponse(string Rel, string TargetKey, string BindsDimension);

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
