using System.Globalization;

namespace Prism.Api.Auth;

/// <summary>Validates post-login return targets. Only same-origin root-relative paths are accepted. Keep in step with the SPA's return-url utility.</summary>
public static class RedirectTargets
{
    private const string LoginPath = "/login";
    private const int MaxLength = 2048;
    private static readonly Uri ProbeBase = new("https://prism-origin.invalid/");

    /// <summary>Returns the target when it is safe, otherwise null.</summary>
    public static string? TryNormalize(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxLength)
        {
            return null;
        }

        string value = candidate.Trim(' ');
        if (value.Length == 0 || value[0] != '/' || value.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        // Browsers strip tab, CR and LF from URLs, so "/<TAB>/evil.com" would turn into "//evil.com".
        if (value.Contains('\\') || value.Contains('@') || value.Any(IsBrowserNormalizedCharacter))
        {
            return null;
        }

        if (!Uri.TryCreate(ProbeBase, value, out Uri? resolved) || resolved.Host != ProbeBase.Host)
        {
            return null;
        }

        int cut = value.IndexOfAny(['?', '#']);
        string pathOnly = cut < 0 ? value : value[..cut];
        return pathOnly == LoginPath || pathOnly.StartsWith(LoginPath + "/", StringComparison.Ordinal) ? null : value;
    }

    private static bool IsBrowserNormalizedCharacter(char c) =>
        char.IsControl(c)
        || char.IsWhiteSpace(c)
        || CharUnicodeInfo.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
}
