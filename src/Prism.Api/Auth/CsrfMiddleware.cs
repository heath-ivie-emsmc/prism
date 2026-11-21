using System.Security.Cryptography;
using System.Text;

namespace Prism.Api.Auth;

/// <summary>
/// Double-submit CSRF protection. Every request gets an <c>XSRF-TOKEN</c> cookie that scripts can read, and a state-changing
/// request under /api must echo it in <c>X-XSRF-TOKEN</c>. The confirm form is exempt: it is protected by its own nonce cookie.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, AuthCookies cookies)
{
    private static readonly PathString ConfirmPath = new("/api/auth/sso/confirm");

    public async Task InvokeAsync(HttpContext context)
    {
        string? token = context.Request.Cookies[AuthCookies.XsrfCookie];
        if (string.IsNullOrEmpty(token))
        {
            token = SecureToken.Base64(32);
            context.Response.Cookies.Append(AuthCookies.XsrfCookie, token, cookies.XsrfOptions());
        }

        if (RequiresCheck(context.Request) && !HeaderMatches(context.Request, token))
        {
            await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "The request is missing a valid anti-forgery header.").ExecuteAsync(context);
            return;
        }

        await next(context);
    }

    private static bool RequiresCheck(HttpRequest request) =>
        request.Path.StartsWithSegments("/api")
        && !request.Path.StartsWithSegments(ConfirmPath)
        && !HttpMethods.IsGet(request.Method)
        && !HttpMethods.IsHead(request.Method)
        && !HttpMethods.IsOptions(request.Method);

    private static bool HeaderMatches(HttpRequest request, string token)
    {
        string? header = request.Headers[AuthCookies.XsrfHeader];
        return header is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header), Encoding.UTF8.GetBytes(token));
    }
}
