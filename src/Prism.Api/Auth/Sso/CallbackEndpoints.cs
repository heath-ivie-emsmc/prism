using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.Features;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Auth.Sso;

/// <summary>
/// Where the Account Portal sends the browser back, and the confirmation step for portals that do not echo state.
/// Both end in <see cref="SignInFlow"/>, which validates the login token and issues the session.
/// </summary>
internal static class CallbackEndpoints
{
    private const int MaxFormBytes = 16 * 1024;
    private const int MaxStateLength = 128;

    public static async Task<IResult> Callback(
        HttpContext context,
        AuthCookies cookies,
        SignInFlow flow,
        string? loginToken,
        string? state,
        CancellationToken cancellationToken)
    {
        string? stateCookie = context.Request.Cookies[cookies.State];
        if (stateCookie is not null)
        {
            // Single use: the state is gone whether or not it matches.
            context.Response.Cookies.Delete(cookies.State, cookies.DeleteOptions(cookies.StateOptions()));
        }

        if (string.IsNullOrWhiteSpace(loginToken) || loginToken.Length > PortalLoginTokenValidator.MaxTokenLength)
        {
            return SignInFlow.Failed("sso_failed");
        }

        if (!string.IsNullOrEmpty(stateCookie) && !string.IsNullOrEmpty(state))
        {
            return FixedTimeEquals(stateCookie, state)
                ? await flow.CompleteAsync(context, loginToken, cancellationToken)
                : SignInFlow.Failed("sso_failed");
        }

        // The portal did not echo state. Only a browser that started this sign-in itself (it holds the state cookie from
        // /start) may go on, and only after confirming on a page served by this app. A link sent by an attacker reaches a
        // browser with no state cookie and stops here, so it cannot sign a victim in as someone else.
        if (!flow.AllowIdpInitiated || string.IsNullOrEmpty(stateCookie))
        {
            return SignInFlow.Failed("sso_failed");
        }

        string nonce = SecureToken.Hex(32);
        context.Response.Cookies.Append(cookies.Confirm, nonce, cookies.ConfirmOptions());
        return ConfirmPage.Render(loginToken, nonce);
    }

    public static async Task<IResult> Confirm(
        HttpContext context,
        AuthCookies cookies,
        SignInFlow flow,
        CancellationToken cancellationToken)
    {
        if (!flow.AllowIdpInitiated)
        {
            return Results.NotFound();
        }

        string? confirmCookie = context.Request.Cookies[cookies.Confirm];
        context.Response.Cookies.Delete(cookies.Confirm, cookies.DeleteOptions(cookies.ConfirmOptions()));

        // A browser form post always declares its length. Refuse anything undeclared or large before reading it.
        if (!context.Request.HasFormContentType || context.Request.ContentLength is not (> 0 and <= MaxFormBytes))
        {
            return SignInFlow.Failed("sso_failed");
        }

        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } sizeLimit)
        {
            sizeLimit.MaxRequestBodySize = MaxFormBytes;
        }

        IFormCollection form = await context.Request.ReadFormAsync(cancellationToken);
        string? nonce = form["nonce"];
        string? loginToken = form["loginToken"];

        if (string.IsNullOrEmpty(confirmCookie)
            || string.IsNullOrEmpty(nonce)
            || nonce.Length > MaxStateLength
            || string.IsNullOrWhiteSpace(loginToken)
            || loginToken.Length > PortalLoginTokenValidator.MaxTokenLength
            || !FixedTimeEquals(confirmCookie, nonce))
        {
            return SignInFlow.Failed("sso_failed");
        }

        return await flow.CompleteAsync(context, loginToken, cancellationToken);
    }

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left.Trim()), Encoding.UTF8.GetBytes(right.Trim()));
}
