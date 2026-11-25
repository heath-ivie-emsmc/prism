using System.Globalization;
using System.Security.Claims;
using Prism.Application.Auth;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>Transports and loaded miles by month.</summary>
internal static class TransportVolumeEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/transports", TransportsRoute)
            .WithName("GetTransportVolume")
            .WithSummary("Transports and loaded miles by month")
            .WithDescription(
                "Transports and loaded miles (statute miles, billed with the patient on board) for each of the last `months` months ending with `endPeriod` (`yyyyMM`, default the newest period with net cash data), "
                + "by date of service. `months` defaults to 6 and is at most 24. `serviceLine` is `all` (default), `air` or `ground`. Months with no transports are left out. `groups` splits the last month by aircraft type or level of service, with each group's `priorTransports` in the month before it (same date-of-service basis, `serviceLine` and `division`), `changeTransports` and `changePercent` (percent units, one decimal, null when the prior month had none). A group with no transports in the last month is not listed. "
                + "`periodOpen` is true while the accounting period of the last month is open, so transports for it may still be added. A transport belongs to the month it happened, so these counts differ from the net cash breakdown, "
                + "which counts encounters with cash posted in the month. Aggregates only. `division` limits the figures to one Reporting Division, using a `key` from the divisions route; an unknown key is a 400. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<TransportVolumeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    }

    public static async Task<IResult> TransportsRoute(
        [AsParameters] TransportsRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ITransportVolumeMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months, string? serviceLine, string? divisionKey) = (request.ProviderId, request.EndPeriod, request.Months, request.ServiceLine, request.Division);
        if (await access.ResolveAggregateAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        int count = months ?? INetCollectionsMetric.DefaultMonths;
        if (count is < 1 or > ITransportVolumeMetric.MaxMonths)
        {
            return Invalid("months", $"months must be between 1 and {ITransportVolumeMetric.MaxMonths}.");
        }

        ServiceLineFilter filter = ServiceLineFilter.Both;
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
        string? through = await access.DataThroughAsync(id, ITransportVolumeMetric.Definition.Key, filter, cancellationToken);
        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new TransportVolumeResponse(MetricOf(ITransportVolumeMetric.Definition), providerId, null, count, ServiceLineName(filter), false, [], []) with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        TransportVolume volume = await metric.GetVolumeAsync(id, end, count, filter, division, cancellationToken);
        TransportVolumeResponse response = new(
            MetricOf(volume.Metric),
            providerId,
            volume.EndPeriod.ToString(),
            volume.Months,
            ServiceLineName(volume.ServiceLine),
            volume.PeriodOpen,
            volume.Points.Select(p => new TransportVolumePointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.Transports, p.LoadedMiles)).ToArray(),
            volume.Groups.Select(g => new TransportVolumeGroupResponse(g.Key, g.Name, g.IsAir ? "air" : "ground", g.Transports, g.PriorTransports, g.Change, g.ChangePercent)).ToArray());
        return Results.Ok(response with { DataThrough = through, Division = division?.Key, DivisionName = division?.Name });
    }
}

/// <summary>Route and query values of the transports route.</summary>
internal readonly record struct TransportsRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months,
    string? ServiceLine,
    string? Division);

/// <summary>
/// Transports and loaded miles by month of service, ending at <c>EndPeriod</c>. <c>Groups</c> splits the last month by aircraft type or level of service.
/// <c>PeriodOpen</c> is true while the accounting period of the last month is open, so transports for it may still be added.
/// </summary>
public sealed record TransportVolumeResponse(
    MetricResponse Metric,
    string ProviderId,
    string? EndPeriod,
    int Months,
    string ServiceLine,
    bool PeriodOpen,
    IReadOnlyList<TransportVolumePointResponse> Points,
    IReadOnlyList<TransportVolumeGroupResponse> Groups)
{
    /// <summary>The date of the warehouse load the figures come from, <c>yyyy-MM-dd</c> (Data Through). Null when the Provider has no data in the table the Metric reads.</summary>
    public string? DataThrough { get; init; }

    /// <summary>The <c>key</c> of the Reporting Division the figures are limited to, or null for every division.</summary>
    public string? Division { get; init; }

    /// <summary>The warehouse's name for that division, or null for every division.</summary>
    public string? DivisionName { get; init; }
}

/// <summary>One month: <c>Period</c> is <c>yyyyMM</c>, <c>Transports</c> the count and <c>LoadedMiles</c> the statute miles billed with the patient on board.</summary>
public sealed record TransportVolumePointResponse(string Period, string Label, int Year, int Transports, decimal LoadedMiles);

/// <summary>
/// An aircraft type or level of service: <c>Key</c> is stable, <c>Name</c> is for display, <c>ServiceLine</c> is <c>air</c> or <c>ground</c>.
/// <c>PriorTransports</c> is the count in the month before the last, <c>ChangeTransports</c> the difference and <c>ChangePercent</c> that difference in percent units (null when the prior month had none).
/// </summary>
public sealed record TransportVolumeGroupResponse(string Key, string Name, string ServiceLine, int Transports, int PriorTransports, int ChangeTransports, decimal? ChangePercent);
