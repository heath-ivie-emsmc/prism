namespace Prism.Application.Attention;

/// <summary>
/// The estimated timely filing deadline: the date of service plus the filing limit for the payer category. The warehouse has no deadline column,
/// so this is a rule, not a fact, and the page says "estimated". Days left count from Data Through so they match the data on screen.
/// </summary>
public static class FilingDeadline
{
    public static DateOnly DueDate(DateOnly serviceDate, int limitDays) => serviceDate.AddDays(limitDays);

    /// <summary>Whole days from <paramref name="dataThrough"/> to the due date. Negative when the deadline has passed.</summary>
    public static int DaysLeft(DateOnly dueDate, DateOnly dataThrough) => dueDate.DayNumber - dataThrough.DayNumber;
}

/// <summary>The filing limit in days per payer category, with a default for any category not listed.</summary>
public sealed class FilingLimitOptions
{
    public const string SectionName = "Attention:FilingLimitDays";

    public const int MinDays = 1;

    public const int MaxDays = 1825;

    /// <summary>A placeholder until the real limits are supplied.</summary>
    public int Default { get; set; } = 365;

    /// <summary>Keyed by the warehouse payer category (<c>prim_pay_category</c>), compared ignoring case.</summary>
    public Dictionary<string, int> ByPayerCategory { get; set; } = [];

    public int LimitFor(string? payerCategory)
    {
        if (payerCategory is null)
        {
            return Default;
        }

        KeyValuePair<string, int> match = ByPayerCategory.FirstOrDefault(limit => string.Equals(limit.Key, payerCategory, StringComparison.OrdinalIgnoreCase));
        return match.Key is null ? Default : match.Value;
    }

    public bool IsValid() =>
        Default is >= MinDays and <= MaxDays && ByPayerCategory.Values.All(days => days is >= MinDays and <= MaxDays);
}
