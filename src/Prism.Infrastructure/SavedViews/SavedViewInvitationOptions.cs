namespace Prism.Infrastructure.SavedViews;

/// <summary>
/// Settings for emailed Saved View invitations. Off by default: a host turns it on in its own <c>appsettings.&lt;Environment&gt;.json</c> once it has
/// a mail server and a public address. The SMTP user name and password are secrets and come from Secrets Manager, never from the repository.
/// </summary>
public sealed class SavedViewInvitationOptions
{
    public const string ConfigurationSection = "SavedViews:Invitations";

    public bool Enabled { get; set; }

    /// <summary>How long an invitation link works, in days.</summary>
    public int ExpiryDays { get; set; } = 7;

    /// <summary>The address the SPA is served from, used to build the link in the email. Never taken from a request.</summary>
    public string PublicBaseUrl { get; set; } = "";

    public string SmtpHost { get; set; } = "";

    public int SmtpPort { get; set; } = 587;

    public string SmtpUserName { get; set; } = "";

    public string SmtpPassword { get; set; } = "";

    public string FromAddress { get; set; } = "";

    public string FromName { get; set; } = "Prism";
}

/// <summary>The rules for <see cref="SavedViewInvitationOptions"/>, kept apart from hosting so they can be tested directly.</summary>
public static class SavedViewInvitationOptionsRules
{
    public static IReadOnlyList<string> Problems(SavedViewInvitationOptions options, bool deployed)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled)
        {
            return [];
        }

        List<string> problems = [];
        if (options.ExpiryDays is < 1 or > 30)
        {
            problems.Add("SavedViews:Invitations:ExpiryDays must be between 1 and 30.");
        }

        if (!IsAllowedBaseUrl(options.PublicBaseUrl, deployed))
        {
            problems.Add(deployed ? "SavedViews:Invitations:PublicBaseUrl must be an https address." : "SavedViews:Invitations:PublicBaseUrl must be an http or https address.");
        }

        if (string.IsNullOrWhiteSpace(options.SmtpHost))
        {
            problems.Add("SavedViews:Invitations:SmtpHost is required when invitations are enabled.");
        }

        if (options.SmtpPort is < 1 or > 65535)
        {
            problems.Add("SavedViews:Invitations:SmtpPort must be a port number.");
        }

        if (string.IsNullOrWhiteSpace(options.FromAddress) || !System.Net.Mail.MailAddress.TryCreate(options.FromAddress, out _))
        {
            problems.Add("SavedViews:Invitations:FromAddress must be an email address.");
        }

        return problems;
    }

    private static bool IsAllowedBaseUrl(string value, bool deployed) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? url) && (url.Scheme == Uri.UriSchemeHttps || (!deployed && url.Scheme == Uri.UriSchemeHttp));
}
