using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Prism.Application.Auth;
using Prism.Application.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Net cash collected for a Provider: the periods that have data, and a run of months ending at one of them.</summary>
internal static partial class NetCollectionsEndpoints
{
    private static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static void Map(RouteGroupBuilder v1)
    {
        RouteGroupBuilder group = v1.MapGroup("/providers/{providerId}/metrics/net-collections")
            .RequireAuthorization(Permissions.DashboardView)
            .WithTags("Metrics");

        group.MapGet("/", Series)
            .WithName("GetNetCollections")
            .WithSummary("Net cash collected by month")
            .WithDescription(
                "Net cash collected for the Provider in each of the last `months` accounting periods ending with `endPeriod`, from the latest warehouse load. "
                + "`endPeriod` is `yyyyMM` and defaults to the newest period that has data. `months` defaults to 6 and is at most 24. "
                + "`serviceLine` is `all` (default, the Provider's whole figure), `air` or `ground`: air or ground alone is added up from the payments ledger by the transport's level of service, "
                + "because the warehouse's summary table has no such split. "
                + "Months with no data are left out, not returned as zero. The figures are aggregates: no patient or claim detail. "
                + "Returns 404 when the Provider is not one the caller may read in full (not assigned, or limited to some Facilities).")
            .Produces<NetCollectionsResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/projection", Projection)
            .WithName("GetNetCashProjection")
            .WithSummary("Projected net cash for the next three months")
            .WithDescription(
                "The cash the warehouse forecasts for up to three months after `endPeriod` (`yyyyMM`, default the newest period with net cash data), from the latest forecast load. "
                + "`serviceLine` is `all` (default), `air` or `ground`. Each month has the projected `value` and a likely range (`low` to `high`), the lower and higher of the AR-based and budget-based forecasts. "
                + "`total`, `low` and `high` are the sums over the returned months. Fewer than three months, or none, come back when the forecast is shorter. Aggregates only. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashProjectionResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/breakdown", Breakdown)
            .WithName("GetNetCashBreakdown")
            .WithSummary("Net cash by aircraft type or level of service")
            .WithDescription(
                "Net cash collected in `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, split by aircraft type (air) or level of service (ground), "
                + "and by base and vehicle (tail number or unit). `serviceLine` is `all`, `air` (default) or `ground`. Net cash is payments less refunds by the accounting period they posted in, "
                + "without the Old AR import, so the groups add up to the net cash for the period. `transports` are the encounters with cash posted in the period. "
                + "Legacy Old AR bases are counted under their current base. Aggregates only: no patient or claim detail. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<NetCashBreakdownResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/payer-mix", PayerMixRoute)
            .WithName("GetPayerMix")
            .WithSummary("Transports by primary payer group")
            .WithDescription(
                "Transports by the primary payer's group for `period` (`yyyyMM`, default the newest period with net cash data) and the month before it, by date of service. "
                + "`serviceLine` is `all`, `air` (default) or `ground`. `group` narrows it to one aircraft type or level of service, using the `key` the breakdown route returns (for example `fixed-wing`). "
                + "Groups are Commercial (insurance and third-party liability), Medicare (with Medicare Advantage), Medicaid (with Medicaid managed care), Other government, VA (VA, CHAMPVA and TRICARE payers by name), Facility and Patient responsibility. "
                + "`not-recorded` appears only when some transports have no payer. Counts only: no patient, claim or payer names. "
                + "Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<PayerMixResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/transports", TransportsRoute)
            .WithName("GetTransportVolume")
            .WithSummary("Transports and loaded miles by month")
            .WithDescription(
                "Transports and loaded miles (statute miles, billed with the patient on board) for each of the last `months` months ending with `endPeriod` (`yyyyMM`, default the newest period with net cash data), "
                + "by date of service. `months` defaults to 6 and is at most 24. `serviceLine` is `all` (default), `air` or `ground`. Months with no transports are left out. `groups` splits the last month by aircraft type or level of service. "
                + "`periodOpen` is true while the accounting period of the last month is open, so transports for it may still be added. A transport belongs to the month it happened, so these counts differ from the net cash breakdown, "
                + "which counts encounters with cash posted in the month. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<TransportVolumeResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/clean-claims", CleanClaimsRoute)
            .WithName("GetCleanClaimRate")
            .WithSummary("Clean claim rate by month")
            .WithDescription(
                "The share of claims accepted first time, by month of service, for each of the last `months` months up to `endPeriod` (`yyyyMM`, default the newest period with net cash data) that the warehouse has a figure for. "
                + "`months` defaults to 6 and is at most 24. `rate` is 0 to 1, or null when there were no claims. Months the warehouse has no figure for are left out, so the newest point can be older than `endPeriod`: "
                + "the warehouse table is refreshed on its own schedule. The figure covers all of the Provider's claims, not one Service Line. Aggregates only. Returns 404 when the Provider is not one the caller may read in full.")
            .Produces<CleanClaimRateResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/periods", Periods)
            .WithName("GetNetCollectionsPeriods")
            .WithSummary("Accounting periods with net cash data")
            .WithDescription("The accounting periods the Provider has net cash data for, newest first, as `yyyyMM` with a display label. Fills the Period drop-down.")
            .Produces<NetCollectionsPeriodsResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    public static async Task<IResult> Periods(
        string providerId, ClaimsPrincipal user, ProviderAccess access, INetCollectionsMetric metric, CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        IReadOnlyList<ApPeriod> periods = await metric.GetPeriodsAsync(id, cancellationToken);
        return Results.Ok(new NetCollectionsPeriodsResponse(providerId, periods.Select(Describe).ToArray()));
    }

    public static async Task<IResult> Series(
        [AsParameters] SeriesRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months, string? serviceLine) = (request.ProviderId, request.EndPeriod, request.Months, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        int count = months ?? INetCollectionsMetric.DefaultMonths;
        if (count is < 1 or > INetCollectionsMetric.MaxMonths)
        {
            return Invalid("months", $"months must be between 1 and {INetCollectionsMetric.MaxMonths}.");
        }

        if (!TryParseServiceLine(serviceLine, out ServiceLineFilter filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await metric.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(Empty(providerId, count));
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        NetCollectionsSeries series = await metric.GetSeriesAsync(id, end, count, filter, cancellationToken);
        return Results.Ok(new NetCollectionsResponse(
            new MetricResponse(series.Metric.Key, series.Metric.Name, series.Metric.Description, series.Metric.Unit),
            providerId,
            series.EndPeriod.ToString(),
            series.Months,
            series.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            series.Points.Select(p => new NetCollectionsPointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.Value)).ToArray()));
    }

    public static async Task<IResult> Projection(
        [AsParameters] ProjectionRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashProjectionMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, string? serviceLine) = (request.ProviderId, request.EndPeriod, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        if (!TryParseServiceLine(serviceLine, out ServiceLineFilter filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(EmptyProjection(providerId, filter));
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        NetCashProjection projection = await metric.GetProjectionAsync(id, end, filter, cancellationToken);
        return Results.Ok(new NetCashProjectionResponse(
            new MetricResponse(projection.Metric.Key, projection.Metric.Name, projection.Metric.Description, projection.Metric.Unit),
            providerId,
            projection.AfterPeriod.ToString(),
            ServiceLineName(filter),
            projection.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            projection.Total,
            projection.Low,
            projection.High,
            projection.Months.Select(m => new NetCashProjectionMonthResponse(m.Period.ToString(), Months[m.Period.Month - 1], m.Period.Year, m.Value, m.Low, m.High)).ToArray()));
    }

    public static async Task<IResult> Breakdown(
        [AsParameters] BreakdownRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        INetCashBreakdownMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine) = (request.ProviderId, request.Period, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (serviceLine is not null && !TryParseServiceLine(serviceLine, out filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(EmptyBreakdown(providerId, filter));
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        NetCashBreakdown breakdown = await metric.GetBreakdownAsync(id, chosen, filter, cancellationToken);
        return Results.Ok(ToResponse(providerId, breakdown));
    }

    public static async Task<IResult> PayerMixRoute(
        [AsParameters] PayerMixRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        IPayerMixMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? period, string? serviceLine, string? groupKey) = (request.ProviderId, request.Period, request.ServiceLine, request.Group);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        ServiceLineFilter filter = ServiceLineFilter.Air;
        if (serviceLine is not null && !TryParseServiceLine(serviceLine, out filter))
        {
            return Invalid("serviceLine", "serviceLine must be all, air or ground.");
        }

        if (groupKey is not null && !GroupKeyPattern().IsMatch(groupKey))
        {
            return Invalid("group", "group must be a key from the breakdown route, such as fixed-wing.");
        }

        ApPeriod chosen;
        if (period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new PayerMixResponse(MetricOf(IPayerMixMetric.Definition), providerId, null, null, ServiceLineName(filter), groupKey, null, []));
            }

            chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(period, out chosen))
        {
            return Invalid("period", "period must be a month written yyyyMM, for example 202610.");
        }

        PayerMix mix = await metric.GetPayerMixAsync(id, chosen, filter, groupKey, cancellationToken);
        return Results.Ok(new PayerMixResponse(
            MetricOf(mix.Metric),
            providerId,
            mix.Period.ToString(),
            mix.PriorPeriod.ToString(),
            ServiceLineName(mix.ServiceLine),
            mix.GroupKey,
            mix.GroupName,
            mix.Shares.Select(s => new PayerMixShareResponse(s.Key, s.Name, s.Current, s.Prior)).ToArray()));
    }

    public static async Task<IResult> TransportsRoute(
        [AsParameters] TransportsRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ITransportVolumeMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months, string? serviceLine) = (request.ProviderId, request.EndPeriod, request.Months, request.ServiceLine);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
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

        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new TransportVolumeResponse(MetricOf(ITransportVolumeMetric.Definition), providerId, null, count, ServiceLineName(filter), false, [], []));
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        TransportVolume volume = await metric.GetVolumeAsync(id, end, count, filter, cancellationToken);
        return Results.Ok(new TransportVolumeResponse(
            MetricOf(volume.Metric),
            providerId,
            volume.EndPeriod.ToString(),
            volume.Months,
            ServiceLineName(volume.ServiceLine),
            volume.PeriodOpen,
            volume.Points.Select(p => new TransportVolumePointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.Transports, p.LoadedMiles)).ToArray(),
            volume.Groups.Select(g => new TransportVolumeGroupResponse(g.Key, g.Name, g.IsAir ? "air" : "ground", g.Transports)).ToArray()));
    }

    public static async Task<IResult> CleanClaimsRoute(
        [AsParameters] CleanClaimsRequest request,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        ICleanClaimRateMetric metric,
        CancellationToken cancellationToken)
    {
        (string providerId, string? endPeriod, int? months) = (request.ProviderId, request.EndPeriod, request.Months);
        if (await access.ResolveAsync(user, providerId, cancellationToken) is not { } id)
        {
            return Results.NotFound();
        }

        int count = months ?? INetCollectionsMetric.DefaultMonths;
        if (count is < 1 or > ICleanClaimRateMetric.MaxMonths)
        {
            return Invalid("months", $"months must be between 1 and {ICleanClaimRateMetric.MaxMonths}.");
        }

        ApPeriod end;
        if (endPeriod is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count == 0)
            {
                return Results.Ok(new CleanClaimRateResponse(MetricOf(ICleanClaimRateMetric.Definition), providerId, null, count, null, []));
            }

            end = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
        }
        else if (!ApPeriod.TryParse(endPeriod, out end))
        {
            return Invalid("endPeriod", "endPeriod must be a month written yyyyMM, for example 202610.");
        }

        CleanClaimRate rate = await metric.GetRateAsync(id, end, count, cancellationToken);
        return Results.Ok(new CleanClaimRateResponse(
            MetricOf(rate.Metric),
            providerId,
            rate.EndPeriod.ToString(),
            rate.Months,
            rate.SnapshotDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            rate.Points.Select(p => new CleanClaimPointResponse(p.Period.ToString(), Months[p.Period.Month - 1], p.Period.Year, p.CleanClaims, p.RejectedClaims, p.TotalClaims, p.Rate)).ToArray()));
    }

    private static MetricResponse MetricOf(MetricDefinition d) => new(d.Key, d.Name, d.Description, d.Unit);

    [GeneratedRegex("^[a-z0-9-]{1,40}$", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    private static partial Regex GroupKeyPattern();

    private static NetCashBreakdownResponse ToResponse(string providerId, NetCashBreakdown breakdown) =>
        new(
            new MetricResponse(breakdown.Metric.Key, breakdown.Metric.Name, breakdown.Metric.Description, breakdown.Metric.Unit),
            providerId,
            breakdown.Period.ToString(),
            breakdown.PriorPeriod.ToString(),
            ServiceLineName(breakdown.ServiceLine),
            breakdown.PeriodOpen,
            breakdown.Groups.Select(g => new BreakdownGroupResponse(g.Key, g.Name, g.IsAir ? "air" : "ground", Figures(g.Current), Figures(g.Prior))).ToArray(),
            breakdown.Rows.Select(r => new BreakdownRowResponse(r.GroupKey, r.GroupName, r.IsAir ? "air" : "ground", r.Base, r.Vehicle, Figures(r.Current), Figures(r.Prior))).ToArray());

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

    private static bool TryParseServiceLine(string? value, out ServiceLineFilter filter)
    {
        filter = ServiceLineFilter.Both;
        // Exactly these three words: Enum.TryParse would also accept numbers and combinations.
        switch (value)
        {
            case null or "all":
                return true;
            case "air":
                filter = ServiceLineFilter.Air;
                return true;
            case "ground":
                filter = ServiceLineFilter.Ground;
                return true;
            default:
                return false;
        }
    }

    private static string ServiceLineName(ServiceLineFilter filter) => filter switch
    {
        ServiceLineFilter.Air => "air",
        ServiceLineFilter.Ground => "ground",
        _ => "all",
    };

    private static NetCashProjectionResponse EmptyProjection(string providerId, ServiceLineFilter filter) =>
        new(
            new MetricResponse(INetCashProjectionMetric.Definition.Key, INetCashProjectionMetric.Definition.Name, INetCashProjectionMetric.Definition.Description, INetCashProjectionMetric.Definition.Unit),
            providerId,
            null,
            ServiceLineName(filter),
            null,
            0m,
            0m,
            0m,
            []);

    private static int ComparePeriods(ApPeriod a, ApPeriod b) => ((a.Year * 12) + a.Month).CompareTo((b.Year * 12) + b.Month);

    private static PeriodResponse Describe(ApPeriod period) =>
        new(period.ToString(), $"{Months[period.Month - 1]} {period.Year}");

    private static NetCollectionsResponse Empty(string providerId, int months) =>
        new(
            new MetricResponse(INetCollectionsMetric.Definition.Key, INetCollectionsMetric.Definition.Name, INetCollectionsMetric.Definition.Description, INetCollectionsMetric.Definition.Unit),
            providerId,
            null,
            months,
            null,
            []);

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>Net cash collected for a Provider, by accounting period.</summary>
public sealed record NetCollectionsResponse(
    MetricResponse Metric,
    string ProviderId,
    string? EndPeriod,
    int Months,
    string? SnapshotDate,
    IReadOnlyList<NetCollectionsPointResponse> Points);

/// <summary>What a Metric is: stable key, display name, meaning and unit.</summary>
public sealed record MetricResponse(string Key, string Name, string Description, string Unit);

/// <summary>One month: <c>Period</c> is <c>yyyyMM</c>, <c>Label</c> the short month name, <c>Value</c> the amount in the Metric's unit.</summary>
public sealed record NetCollectionsPointResponse(string Period, string Label, int Year, decimal Value);

public sealed record NetCollectionsPeriodsResponse(string ProviderId, IReadOnlyList<PeriodResponse> Periods);

public sealed record PeriodResponse(string Period, string Label);

/// <summary>Route and query values of the projection route, bound together to keep the handler's parameter list short.</summary>
internal readonly record struct ProjectionRequest(
    string ProviderId,
    string? EndPeriod,
    string? ServiceLine);

/// <summary>The cash forecast for the months after <c>AfterPeriod</c>. <c>Total</c>, <c>Low</c> and <c>High</c> are sums over <c>Months</c>.</summary>
public sealed record NetCashProjectionResponse(
    MetricResponse Metric,
    string ProviderId,
    string? AfterPeriod,
    string ServiceLine,
    string? SnapshotDate,
    decimal Total,
    decimal Low,
    decimal High,
    IReadOnlyList<NetCashProjectionMonthResponse> Months);

/// <summary>One projected month: <c>Period</c> is <c>yyyyMM</c>, <c>Value</c> the projected cash, <c>Low</c> to <c>High</c> the likely range.</summary>
public sealed record NetCashProjectionMonthResponse(string Period, string Label, int Year, decimal Value, decimal Low, decimal High);

/// <summary>Route and query values of the breakdown route, bound together to keep the handler's parameter list short.</summary>
internal readonly record struct BreakdownRequest(
    string ProviderId,
    string? Period,
    string? ServiceLine);

/// <summary>
/// Net cash for <c>Period</c> and the month before it (<c>PriorPeriod</c>), by <c>Groups</c> (aircraft type or level of service) and <c>Rows</c> (base and vehicle).
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
    IReadOnlyList<BreakdownRowResponse> Rows);

/// <summary>An aircraft type or level of service. <c>Key</c> is stable, <c>Name</c> is for display, <c>ServiceLine</c> is <c>air</c> or <c>ground</c>.</summary>
public sealed record BreakdownGroupResponse(string Key, string Name, string ServiceLine, BreakdownFiguresResponse Current, BreakdownFiguresResponse Prior);

/// <summary>A vehicle (tail number or unit) at a base or station. <c>Base</c> is null when the warehouse has none.</summary>
public sealed record BreakdownRowResponse(string GroupKey, string GroupName, string ServiceLine, string? Base, string Vehicle, BreakdownFiguresResponse Current, BreakdownFiguresResponse Prior);

/// <summary>
/// Net cash (<c>Cash</c>, USD), <c>Transports</c> (encounters with cash posted in the month) and <c>LoadedMiles</c>.
/// <c>PrimaryMix</c> and <c>SecondaryMix</c> are scene and interfacility transports for air, emergency and non-emergency for ground.
/// </summary>
public sealed record BreakdownFiguresResponse(decimal Cash, int Transports, decimal LoadedMiles, int PrimaryMix, int SecondaryMix);

/// <summary>Route and query values of the payer mix route.</summary>
internal readonly record struct PayerMixRequest(
    string ProviderId,
    string? Period,
    string? ServiceLine,
    string? Group);

/// <summary>
/// Transports by primary payer group for <c>Period</c> and the month before it (<c>PriorPeriod</c>), by date of service. <c>Group</c> and <c>GroupName</c>
/// are set when the figures are for one aircraft type or level of service.
/// </summary>
public sealed record PayerMixResponse(
    MetricResponse Metric,
    string ProviderId,
    string? Period,
    string? PriorPeriod,
    string ServiceLine,
    string? Group,
    string? GroupName,
    IReadOnlyList<PayerMixShareResponse> Shares);

/// <summary>A payer group: <c>Key</c> is stable, <c>Name</c> is for display, <c>Current</c> and <c>Prior</c> are transport counts.</summary>
public sealed record PayerMixShareResponse(string Key, string Name, int Current, int Prior);

/// <summary>Route and query values of the net cash series route.</summary>
internal readonly record struct SeriesRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months,
    string? ServiceLine);

/// <summary>Route and query values of the transports route.</summary>
internal readonly record struct TransportsRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months,
    string? ServiceLine);

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
    IReadOnlyList<TransportVolumeGroupResponse> Groups);

/// <summary>One month: <c>Period</c> is <c>yyyyMM</c>, <c>Transports</c> the count and <c>LoadedMiles</c> the statute miles billed with the patient on board.</summary>
public sealed record TransportVolumePointResponse(string Period, string Label, int Year, int Transports, decimal LoadedMiles);

/// <summary>An aircraft type or level of service: <c>Key</c> is stable, <c>Name</c> is for display, <c>ServiceLine</c> is <c>air</c> or <c>ground</c>.</summary>
public sealed record TransportVolumeGroupResponse(string Key, string Name, string ServiceLine, int Transports);

/// <summary>Route and query values of the clean claims route.</summary>
internal readonly record struct CleanClaimsRequest(
    string ProviderId,
    string? EndPeriod,
    int? Months);

/// <summary>
/// The clean claim rate by month of service up to <c>EndPeriod</c>. <c>Points</c> only has months the warehouse has a figure for, so the newest can be older than
/// <c>EndPeriod</c>. <c>SnapshotDate</c> is the warehouse load the figures came from.
/// </summary>
public sealed record CleanClaimRateResponse(
    MetricResponse Metric,
    string ProviderId,
    string? EndPeriod,
    int Months,
    string? SnapshotDate,
    IReadOnlyList<CleanClaimPointResponse> Points);

/// <summary>One month of service: claims accepted first time, rejected, the total, and <c>Rate</c> (0 to 1, null when there were no claims).</summary>
public sealed record CleanClaimPointResponse(string Period, string Label, int Year, int CleanClaims, int RejectedClaims, int TotalClaims, decimal? Rate);
