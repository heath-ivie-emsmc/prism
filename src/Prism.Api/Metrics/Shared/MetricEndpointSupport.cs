using System.Text.RegularExpressions;
using Prism.Application.Metrics;

namespace Prism.Api.Metrics;

/// <summary>Helpers every Metric route shares: month names, parameter parsing and the validation response.</summary>
internal static partial class MetricEndpointSupport
{
    internal static readonly string[] Months = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    internal static MetricResponse MetricOf(MetricDefinition d) => new(d.Key, d.Name, d.Description, d.Unit);

    [GeneratedRegex(@"^[a-z0-9-]{1,40}\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    internal static partial Regex GroupKeyPattern();

    [GeneratedRegex(@"^[a-z0-9-]{1,60}\z", RegexOptions.NonBacktracking | RegexOptions.CultureInvariant)]
    internal static partial Regex DivisionKeyPattern();

    internal static bool TryParseServiceLine(string? value, out ServiceLineFilter filter)
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

    internal static string ServiceLineName(ServiceLineFilter filter) => filter switch
    {
        ServiceLineFilter.Air => "air",
        ServiceLineFilter.Ground => "ground",
        _ => "all",
    };

    internal static int ComparePeriods(ApPeriod a, ApPeriod b) => ((a.Year * 12) + a.Month).CompareTo((b.Year * 12) + b.Month);

    internal static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
}

/// <summary>What a Metric is: stable key, display name, meaning and unit.</summary>
public sealed record MetricResponse(string Key, string Name, string Description, string Unit);

public sealed record PeriodResponse(string Period, string Label);
