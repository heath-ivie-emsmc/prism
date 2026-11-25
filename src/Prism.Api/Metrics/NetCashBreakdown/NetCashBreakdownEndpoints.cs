using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Net cash by aircraft type or level of service, with Reporting Division and vehicle rows.</summary>
internal static class NetCashBreakdownEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/breakdown", Breakdown)
            .WithName("GetNetCashBreakdown")
            .WithSummary("Net cash by aircraft type or level of service")
            .WithDescription(
                "Net cash collected in `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, split by aircraft type (air) or level of service (ground), "
                + "and by Reporting Division and vehicle (tail number or unit). `serviceLine` is `all`, `air` (default) or `ground`. Net cash is payments less refunds by the accounting period they posted in, "
                + "without the Old AR import, so the groups add up to the net cash for the period. `transports` are the encounters with cash posted in the period. "
                + "Legacy Old AR bases are counted under their current Reporting Division. Aggregates only: no patient or claim detail. Each group has `links`: a `drill` link to the payer mix of that group, issued by the server for this Provider and these filters. "
                + "`division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashBreakdownResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> Breakdown(
        [AsParameters] BreakdownRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashBreakdownMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? divisionKey) = (request.ProviderId, request.Period, request.ServiceLine, request.Division);
        if (await access.ResolveAggregateAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (serviceLine is not null && !TryParseServiceLine(serviceLine, out filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        DivisionSelection selection = await access.ResolveDivisionAsync(id, divisionKey, cancellationToken);
        if (selection.Error is { } divisionError)
        {
            return Invalid("division", divisionError);
        }

        ReportingDivision? division = selection.Division;
        string? through = await access.DataThroughAsync(id, INetCashBreakdownMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(EmptyBreakdown(providerId, filter) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        NetCashBreakdown breakdown = await metric.GetBreakdownAsync(id, chosen, filter, division, cancellationToken);
        return Results.Ok(ToResponse(providerId, breakdown, user, division?.Key) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }

    private static NetCashBreakdownResponse ToResponse(string providerId, NetCashBreakdown breakdown, ClaimsPrincipal user, string? divisionKey)
    {
        // Each group links to the payer mix of that group, with the period, Service Line and division in effect. The links come from the catalog, not from the row.
        var links = new DrillLinkBuilder(MetricCatalog.Find(INetCashBreakdownMetric.Definition.Key)!, user, providerId, new DrillContext(breakdown.Period.ToString(), breakdown.ServiceLine, null, divisionKey));
        return new(
            new MetricResponse(breakdown.Metric.Key, breakdown.Metric.Name, breakdown.Metric.Description, breakdown.Metric.Unit),
            providerId,
            breakdown.Period.ToString(),
            breakdown.PriorPeriod.ToString(),
            ServiceLineName(breakdown.ServiceLine),
            breakdown.PeriodOpen,
            breakdown.Groups.Select(g => new BreakdownGroupResponse(g.Key, g.Name, g.IsAir ? "air" : "ground", Figures(g.Current), Figures(g.Prior), links.LinksFor(g.Key, g.Name))).ToArray(),
            breakdown.Rows.Select(r => new BreakdownRowResponse(r.GroupKey, r.GroupName, r.IsAir ? "air" : "ground", r.Base, r.Vehicle, Figures(r.Current), Figures(r.Prior))).ToArray());
    }

    private static BreakdownFiguresResponse Figures(BreakdownFigures f) => new(f.Cash, f.Transports, f.LoadedMiles, f.PrimaryMix, f.SecondaryMix);

    private static NetCashBreakdownResponse EmptyBreakdown(string providerId, ServiceLineFilter filter) =>
        new(
            new MetricResponse(INetCashBreakdownMetric.Definition.Key, INetCashBreakdownMetric.Definition.Name, INetCashBreakdownMetric.Definition.Description, INetCashBreakdownMetric.Definition.Unit),
            providerId,
            null,
            null,
            ServiceLineName(filter),
            false,
            [],
            []);
}

/// <summary>Route and query values of the breakdown route, bound together to keep the handler's parameter list short.</summary>
internal readonly record struct BreakdownRequest(
    string ProviderId,
    string? Period,
    string? ServiceLine,
    string? Division);

/// <summary>
/// Net cash for <c>Period</c> and the month before it (<c>PriorPeriod</c>), by <c>Groups</c> (aircraft type or level of service) and <c>Rows</c> (Reporting Division and vehicle).
/// <c>PeriodOpen</c> is true while the accounting period is still open: cash is still posting into it, so the figures are not final.
/// </summary>
public sealed record NetCashBreakdownResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    bool PeriodOpen,
    IReadOnlyList<BreakdownGroupResponse> Groups,
    IReadOnlyList<BreakdownRowResponse> Rows)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>
/// An aircraft type or level of service. <c>Key</c> is stable, <c>Name</c> is for display, <c>ServiceLine</c> is <c>air</c> or <c>ground</c>.
/// <c>Links</c> are the drills the server offers for this group (<c>rel</c> <c>drill</c>, to the payer mix of the group). It is empty when the caller may not read the target.
/// </summary>
public sealed record BreakdownGroupResponse(string Key, string Name, string ServiceLine, BreakdownFiguresResponse Current, BreakdownFiguresResponse Prior, IReadOnlyList<LinkResponse> Links);

/// <summary>A vehicle (tail number or unit) at a Reporting Division (a base or station). <c>Base</c> is its name, null when the warehouse has none.</summary>
public sealed record BreakdownRowResponse(string GroupKey, string GroupName, string ServiceLine, string? Base, string Vehicle, BreakdownFiguresResponse Current, BreakdownFiguresResponse Prior);

/// <summary>
/// Net cash (<c>Cash</c>, USD), <c>Transports</c> (encounters with cash posted in the month) and <c>LoadedMiles</c>.
/// <c>PrimaryMix</c> and <c>SecondaryMix</c> are scene and interfacility transports for air, emergency and non-emergency for ground.
/// </summary>
public sealed record BreakdownFiguresResponse(decimal Cash, int Transports, decimal LoadedMiles, int PrimaryMix, int SecondaryMix);
