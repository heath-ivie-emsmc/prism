using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Options;
using Prism.Application.SavedViews;

namespace Prism.Infrastructure.SavedViews;

/// <summary>
/// Sends the invitation through the configured SMTP server. The message names the view, the sender and the expiry, and carries the link: no figures and no
/// Provider data. A failure is reported without the address, the server or the link.
/// </summary>
public sealed class SmtpInvitationEmailSender(IOptions<SavedViewInvitationOptions> options) : IInvitationEmailSender
{
    private static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(15);

    public async Task SendAsync(InvitationEmail email, CancellationToken cancellationToken)
    {
        SavedViewInvitationOptions settings = options.Value;
        if (!settings.Enabled)
        {
            throw new InvitationEmailUnavailableException("Invitations are not enabled on this host.");
        }

        using var message = new MailMessage
        {
            From = new MailAddress(settings.FromAddress, settings.FromName),
            Subject = "A Prism view was shared with you",
            Body = Compose(email),
            IsBodyHtml = false,
        };
        message.To.Add(new MailAddress(email.ToAddress));

        using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = true,
            Timeout = (int)SendTimeout.TotalMilliseconds,
        };
        if (!string.IsNullOrEmpty(settings.SmtpUserName))
        {
            client.Credentials = new NetworkCredential(settings.SmtpUserName, settings.SmtpPassword);
        }

        try
        {
            await client.SendMailAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is SmtpException or InvalidOperationException or FormatException)
        {
            // The inner exception is kept out of the message: it can quote the address.
            throw new InvitationEmailUnavailableException("The invitation email could not be sent.");
        }
    }

    internal static string Compose(InvitationEmail email) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"""
            {email.SenderName} shared the Prism view "{email.ViewName}" with you.

            Open it here. The link works once and expires on {email.ExpiresAt:yyyy-MM-dd HH:mm} UTC:
            {email.Link}

            You will be asked to sign in. If you were not expecting this, you can ignore the message.
            """);
}
