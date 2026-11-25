using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Net cash by posting month for each aircraft type or level of service.</summary>
internal static class NetCashByGroupEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/by-group", ByGroupRoute)
            .WithName("GetNetCashByGroup")
            .WithSummary("Net cash by month and aircraft type or level of service")
            .WithDescription(
                "Net cash collected by posting month for each aircraft type (air) or level of service (ground), for `period` (`yyyyMM`, default the newest period with net cash data) and the twelve months before it. "
                + "Net cash is payments less refunds by the accounting period they posted in, without the Old AR import, so a group's month matches the same group in the breakdown. "
                + "`transports` are the encounters with cash posted in the month. Months with no cash are left out, not returned as zero. Cash for an encounter with no Transport record has no group and is left out. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. Aggregates only: no patient or claim detail. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashByGroupResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> ByGroupRoute(
        [AsParameters] ByGroupRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashByGroupMetric metric,
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
        string? through = await access.DataThroughAsync(id, INetCashByGroupMetric.Definition.Key, filter, cancellationToken);
        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new NetCashByGroupResponse(MetricOf(INetCashByGroupMetric.Definition), providerId, null, ServiceLineName(filter), []) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        NetCashByGroup result = await metric.GetNetCashByGroupAsync(id, chosen, filter, division, cancellationToken);
        NetCashByGroupResponse response = new(
            MetricOf(result.Metric),
            providerId,
            result.Period.ToString(),
            ServiceLineName(result.ServiceLine),
            result.Points.Select(p => new NetCashByGroupPointResponse(p.GroupKey, p.GroupName, ServiceLineName(p.IsAir ? ServiceLineFilter.Air : ServiceLineFilter.Ground), p.Period.ToString(), p.Cash, p.Transports)).ToArray());
        return Results.Ok(response with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }
}

/// <summary>Net cash by posting month for each group, for <c>Period</c> and the twelve months before it.</summary>
public sealed record NetCashByGroupResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string ServiceLine,
    IReadOnlyList<NetCashByGroupPointResponse> Points)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>Cash (USD) posted in <c>Period</c> for one group, from <c>Transports</c> encounters. <c>GroupKey</c> is stable, <c>GroupName</c> is for display.</summary>
public sealed record NetCashByGroupPointResponse(string GroupKey, string GroupName, string ServiceLine, string Period, decimal Cash, int Transports);

/// <summary>Route and query values of the net cash by group route.</summary>
internal readonly record struct ByGroupRequest(
    string ProviderId,
    string? Period,
    string? ServiceLine,
    string? Division);
