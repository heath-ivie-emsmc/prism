using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Metrics;

/// <summary>A link the server issues: <c>Rel</c> names the relation (such as <c>drill</c>), <c>Href</c> is a same-origin path, <c>Title</c> is for display.</summary>
public sealed record LinkResponse(string Rel, string Href, string Title);

/// <summary>The filters in effect on the response a link is issued for. A null value is a filter that is not in effect.</summary>
public readonly record struct DrillContext(string? Period, ServiceLineFilter ServiceLine, string? Group, string? Division);

/// <summary>
/// Issues the drill links of one Metric's rows from the Metric Catalog's <see cref="MetricDrill"/> list, so a client never builds a drill URL itself.
/// A link carries the <b>route</b> Provider id (the one the caller was authorized for, never a value from the row or a query), only the filters in effect,
/// and the clicked value as the target's path segment. A link is left out when the target is not an available Metric, does not serve the response's
/// Service Line, rejects the clicked value, or needs a permission the caller lacks. The links of a target are never followed further, so a catalog
/// cycle cannot grow a response.
/// </summary>
public sealed class DrillLinkBuilder
{
    // Where each drill target lives under /providers/{providerId}/metrics/net-collections, what it needs, and which clicked values it accepts.
    // A template with {value} takes the clicked value as a path segment. One without it takes the value in the query, as the dimension the drill binds.
    private static readonly Dictionary<string, DrillRoute> Routes = new(StringComparer.Ordinal)
    {
        ["payer_mix_detail"] = new("payer-mix/{value}", Permissions.DashboardView, IPayerMixDetailMetric.IsDrillable),
        ["payer_mix"] = new("payer-mix", Permissions.DashboardView, IsGroupKey),
    };

    private readonly MetricCatalogEntry source;
    private readonly ClaimsPrincipal user;
    private readonly string routeProviderId;
    private readonly DrillContext context;
    private readonly Func<string, MetricCatalogEntry?> find;

    public DrillLinkBuilder(MetricCatalogEntry source, ClaimsPrincipal user, string routeProviderId, DrillContext context, Func<string, MetricCatalogEntry?>? find = null)
    {
        this.source = source;
        this.user = user;
        this.routeProviderId = routeProviderId;
        this.context = context;
        this.find = find ?? (key => MetricCatalog.Find(key));
    }

    /// <summary>The links for one row: <paramref name="value"/> is the row's key and <paramref name="valueName"/> its display name.</summary>
    public IReadOnlyList<LinkResponse> LinksFor(string value, string valueName)
    {
        List<LinkResponse> links = [];
        foreach (MetricDrill drill in source.Drills)
        {
            if (Link(drill, value, valueName) is { } link)
            {
                links.Add(link);
            }
        }

        return links;
    }

    private LinkResponse? Link(MetricDrill drill, string value, string valueName)
    {
        if (!Routes.TryGetValue(drill.TargetKey, out DrillRoute? route) || !route.Accepts(value))
        {
            return null;
        }

        if (find(drill.TargetKey) is not { Status: MetricStatus.Available } target
            || !target.ServiceLines.Contains(CatalogLine(context.ServiceLine), StringComparer.Ordinal)
            || !target.Dimensions.Contains(drill.BindsDimension, StringComparer.Ordinal)
            || !user.HasClaim(SessionTokenIssuer.PermissionsClaim, route.Permission))
        {
            return null;
        }

        bool inPath = route.Template.Contains("{value}", StringComparison.Ordinal);
        string path = $"/api/v1/providers/{Uri.EscapeDataString(routeProviderId)}/metrics/net-collections/{route.Template.Replace("{value}", Uri.EscapeDataString(value), StringComparison.Ordinal)}";
        return new LinkResponse(drill.Rel, path + Query(target, inPath ? null : drill.BindsDimension, value), $"{target.Name}: {valueName}");
    }

    private string Query(MetricCatalogEntry target, string? boundDimension, string value)
    {
        List<string> parts = [];
        // The clicked value fills the bound dimension and replaces any filter already in effect for it.
        Add(parts, target, MetricDimensions.Group, boundDimension == MetricDimensions.Group ? value : context.Group);
        Add(parts, target, MetricDimensions.Period, context.Period);
        Add(parts, target, MetricDimensions.ServiceLine, MetricEndpointSupport.ServiceLineName(context.ServiceLine));
        Add(parts, target, MetricDimensions.Division, context.Division);
        return parts.Count == 0 ? string.Empty : "?" + string.Join('&', parts);
    }

    private static void Add(List<string> parts, MetricCatalogEntry target, string name, string? value)
    {
        if (!string.IsNullOrEmpty(value) && target.Dimensions.Contains(name, StringComparer.Ordinal))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static bool IsGroupKey(string? value) => value is { Length: <= 40 } && MetricEndpointSupport.GroupKeyPattern().IsMatch(value);

    private static string CatalogLine(ServiceLineFilter filter) => filter switch
    {
        ServiceLineFilter.Air => "air",
        ServiceLineFilter.Ground => "ground",
        _ => "both",
    };

    private sealed record DrillRoute(string Template, string Permission, Func<string?, bool> Accepts);
}
