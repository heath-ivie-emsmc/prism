namespace Prism.Infrastructure.Metrics;

/// <summary>
/// The Reporting Division as a SQL fragment, shared by every query that reads <c>reporting.trip_detail_snapshot</c>. The warehouse names a division in text
/// (<c>Mercy Flights Incorporated OR - Fixed</c>). A legacy division carries a "- Old AR ..." suffix that is removed so it counts under its current one, and an
/// empty value or <c>&lt;none&gt;</c> is no division. One copy means the directory that lists the divisions and the filter that applies one can never disagree.
/// </summary>
internal static class DivisionSql
{
    /// <summary>The normalized division of a trip row, or NULL for none. The table's own <c>reporting_division</c> column.</summary>
    internal const string NormalizedName =
        @"nullif(nullif(trim(regexp_replace(reporting_division, '\s*-\s*Old AR.*$', '', 'i')), ''), '<none>')";

    /// <summary>The name parameter every filtered query binds: the division's name, or an empty string for all divisions.</summary>
    internal const string Parameter = "division";

    /// <summary>The filter on a trip row. True for every row when no division is asked for (empty parameter), otherwise only that division.</summary>
    internal const string Filter = "(@division = '' OR " + NormalizedName + " = @division)";
}
