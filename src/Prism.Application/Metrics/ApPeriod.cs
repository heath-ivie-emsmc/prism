using System.Globalization;

namespace Prism.Application.Metrics;

/// <summary>
/// An accounting period: a calendar month, written <c>yyyyMM</c> in the warehouse (<c>ap_year_month</c>, which is padded with a space).
/// Months are validated, so a bad value from a request or a row never reaches a query.
/// </summary>
public readonly record struct ApPeriod(int Year, int Month)
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>Six digits: year then month, "202610". Trailing spaces from the warehouse's fixed-width column are ignored.</summary>
    public static bool TryParse(string? value, out ApPeriod period)
    {
        period = default;
        // Cap the raw length first: a trimmed value is six characters, and the column pads with a few spaces.
        if (value is { Length: > 16 })
        {
            return false;
        }

        string text = value?.Trim() ?? "";
        if (text.Length != 6 || !text.All(char.IsAsciiDigit))
        {
            return false;
        }

        int year = int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture);
        int month = int.Parse(text.AsSpan(4, 2), CultureInfo.InvariantCulture);
        if (year is < MinYear or > MaxYear || month is < 1 or > 12)
        {
            return false;
        }

        period = new ApPeriod(year, month);
        return true;
    }

    public ApPeriod AddMonths(int months)
    {
        int index = (Year * 12) + (Month - 1) + months;
        return new ApPeriod(Math.DivRem(index, 12, out int month), month + 1);
    }

    /// <summary>The warehouse form, "202610".</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Year:D4}{Month:D2}");
}
