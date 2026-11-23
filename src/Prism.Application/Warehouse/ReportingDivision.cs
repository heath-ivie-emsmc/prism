using System.Text;

namespace Prism.Application.Warehouse;

/// <summary>
/// A Reporting Division of one Provider as a Metric can filter on it. <paramref name="Key"/> is the stable, URL-safe value the API accepts as <c>division</c>,
/// and <paramref name="Name"/> is the warehouse's own name for it, which only the server uses to filter and the UI shows (after the Provider's Label for the term).
/// </summary>
public sealed record ReportingDivision(string Key, string Name)
{
    /// <summary>The longest key. The API refuses a longer <c>division</c> before it looks anything up.</summary>
    public const int MaxKeyLength = 60;

    /// <summary>The most divisions read for one Provider.</summary>
    public const int MaxPerProvider = 500;

    /// <summary>
    /// The divisions for these warehouse names (already without the legacy "- Old AR" suffix), in name order. The key is the name in lower case with every run of
    /// characters other than letters and digits turned into one hyphen, no hyphen at either end, cut to <see cref="MaxKeyLength"/>. A name with no letter or digit
    /// becomes <c>division</c>. Two names that give the same key get <c>-2</c>, <c>-3</c> and so on in name order, so keys are unique within the Provider.
    /// </summary>
    public static IReadOnlyList<ReportingDivision> FromNames(IEnumerable<string> names)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return
        [
            .. names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(MaxPerProvider)
                .Select(name => new ReportingDivision(UniqueKey(Slug(name), used), name)),
        ];
    }

    private static string UniqueKey(string slug, HashSet<string> used)
    {
        if (used.Add(slug))
        {
            return slug;
        }

        string key;
        int n = 1;
        do
        {
            n++;
            string suffix = "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            key = slug[..Math.Min(slug.Length, MaxKeyLength - suffix.Length)].TrimEnd('-') + suffix;
        }
        while (!used.Add(key));

        return key;
    }

    internal static string Slug(string name)
    {
        var builder = new StringBuilder(MaxKeyLength);
        bool pendingHyphen = false;
        foreach (char c in name.Length > 400 ? name[..400] : name)
        {
            char lower = char.ToLowerInvariant(c);
            if (lower is not ((>= 'a' and <= 'z') or (>= '0' and <= '9')))
            {
                pendingHyphen = true;
                continue;
            }

            if (pendingHyphen && builder.Length > 0)
            {
                builder.Append('-');
            }

            pendingHyphen = false;
            builder.Append(lower);
            if (builder.Length >= MaxKeyLength)
            {
                break;
            }
        }

        string slug = builder.ToString().TrimEnd('-');
        return slug.Length == 0 ? "division" : slug;
    }
}
