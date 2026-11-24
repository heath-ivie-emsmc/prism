using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;

namespace Prism.Api.Metrics;

/// <summary>
/// The Reporting Divisions a Provider can filter Metrics by. It serves no single Metric: it gives the <c>key</c> values the <c>division</c> query parameter accepts.
/// </summary>
public sealed class DivisionsModule : IMetricModule
{
    public string? MetricKey => null;

    public int RouteOrder => 101;

    public void AddServices(IServiceCollection services)
    {
        // No services of its own: the divisions come from the Provider division directory.
    }

    public void MapRoutes(RouteGroupBuilder group) =>
        group.MapGet("/divisions", DivisionsRoute)
            .WithName("GetReportingDivisions")
            .WithSummary("Reporting Divisions of the Provider")
            .WithDescription(
                "The Reporting Divisions the Provider's transports are filed under in the latest warehouse load, in name order, each with the stable `key` that the `division` query parameter of the Transport-based Metrics accepts "
                + "and the warehouse's `name` for it. A division that only exists under a legacy \"- Old AR\" name is listed under its current name. The optional `serviceLine` (`air`, `ground` or `all`, default `all`) keeps only the divisions that have at least one Transport under that Service Line in the latest load; keys are the same under every value. At most 500 are returned. Names and keys only, no counts and no patient data. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<ReportingDivisionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    internal static async Task<IResult> DivisionsRoute(string providerId, ClaimsPrincipal user, ProviderAccess access, IProviderDivisionDirectory directory, string? serviceLine, CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter line = ServiceLineFilter.Both;
        if (serviceLine is not null && !MetricEndpointSupport.TryParseServiceLine(serviceLine, out line))
        {
            return MetricEndpointSupport.Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        IReadOnlyList<ReportingDivision> divisions = await directory.GetReportingDivisionsAsync(id, line, cancellationToken);
        return Results.Ok(new ReportingDivisionsResponse(providerId, divisions.Select(d => new ReportingDivisionResponse(d.Key, d.Name)).ToArray()));
    }
}

/// <summary>The Reporting Divisions of one Provider.</summary>
public sealed record ReportingDivisionsResponse(string ProviderId, IReadOnlyList<ReportingDivisionResponse> Divisions);

/// <summary>A Reporting Division: <c>Key</c> is stable and is what the <c>division</c> parameter takes, <c>Name</c> is the warehouse's name for display.</summary>
public sealed record ReportingDivisionResponse(string Key, string Name);
