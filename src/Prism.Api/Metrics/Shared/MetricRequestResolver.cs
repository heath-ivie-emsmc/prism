using System.Security.Claims;
using Prism.Application.Metrics;
using Prism.Application.Warehouse;
using static Prism.Api.Metrics.MetricEndpointSupport;

namespace Prism.Api.Metrics;

/// <summary>The raw route and query values the fleet, call type, payer effect and pickup routes share. <c>Months</c> is null on routes that return no trend.</summary>
internal readonly record struct MetricValues(string ProviderId, string? Period, int? Months, string? ServiceLine, string? Group, string? Division);

/// <summary>
/// The checked values of a request. <paramref name="Period"/> is null when the Provider has no period at all and none was asked for, so the route answers with an empty
/// response. <paramref name="Through"/> is the Data Through date of the Metric's source.
/// </summary>
internal sealed record ResolvedMetric(int Id, ApPeriod? Period, int Months, ServiceLineFilter ServiceLine, string? Group, ReportingDivision? Division, string? Through);

/// <summary>
/// The one checking sequence of the shared routes: the Provider against the user's assignments (404), then <c>serviceLine</c> with what the Metric is about, <c>months</c>, the
/// <c>group</c> key's pattern, the <c>division</c> key against the route Provider's own divisions, the Data Through date and the period. Every refusal is decided before the
/// warehouse is asked for figures, and no refusal echoes the value that caused it.
/// </summary>
internal static class MetricRequestResolver
{
    public static async Task<(ResolvedMetric? Request, IResult? Failure)> ResolveAsync(
        MetricCatalogEntry entry,
        ClaimsPrincipal user,
        ProviderAccess access,
        INetCollectionsMetric periods,
        MetricValues values,
        ServiceLineFilter defaultLine,
        CancellationToken cancellationToken)
    {
        if (await access.ResolveAsync(user, values.ProviderId, cancellationToken) is not { } id)
        {
            return (null, Results.NotFound());
        }

        if (ResolveServiceLine(entry, values.ServiceLine, defaultLine, out ServiceLineFilter line) is { } lineError)
        {
            return (null, lineError);
        }

        int months = values.Months ?? TrendMonths.Default;
        if (values.Months is not null && !TrendMonths.IsAllowed(months))
        {
            return (null, Invalid("months", "months must be 6 or 12."));
        }

        if (values.Group is not null && !GroupKeyPattern().IsMatch(values.Group))
        {
            return (null, Invalid("group", "group must be a key from the breakdown route, such as fixed-wing."));
        }

        DivisionSelection selection = await access.ResolveDivisionAsync(id, values.Division, cancellationToken);
        if (selection.Error is { } divisionError)
        {
            return (null, Invalid("division", divisionError));
        }

        string? through = await access.DataThroughAsync(id, entry.Key, line, cancellationToken);
        ApPeriod? chosen = null;
        if (values.Period is null)
        {
            IReadOnlyList<ApPeriod> available = await periods.GetPeriodsAsync(id, cancellationToken);
            if (available.Count > 0)
            {
                chosen = available.OrderDescending(Comparer<ApPeriod>.Create(ComparePeriods)).First();
            }
        }
        else if (ApPeriod.TryParse(values.Period, out ApPeriod parsed))
        {
            chosen = parsed;
        }
        else
        {
            return (null, Invalid("period", "period must be a month written yyyyMM, for example 202610."));
        }

        return (new ResolvedMetric(id, chosen, months, line, values.Group, selection.Division, through), null);
    }
}

/// <summary>Route and query values of a route with no trend.</summary>
internal readonly record struct ScopeRequest(string ProviderId, string? Period, string? ServiceLine, string? Group, string? Division)
{
    public MetricValues Values => new(ProviderId, Period, null, ServiceLine, Group, Division);
}

/// <summary>Route and query values of a route that returns a monthly trend. <c>Months</c> is 6 (default) or 12.</summary>
internal readonly record struct TrendRequest(string ProviderId, string? Period, int? Months, string? ServiceLine, string? Group, string? Division)
{
    public MetricValues Values => new(ProviderId, Period, Months, ServiceLine, Group, Division);
}
