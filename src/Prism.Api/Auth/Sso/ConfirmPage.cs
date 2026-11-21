using System.Net;

namespace Prism.Api.Auth.Sso;

/// <summary>The one HTML page the API serves: the "Complete sign-in" confirmation, with headers that stop it being framed, cached or scripted.</summary>
internal static class ConfirmPage
{
    public static IResult Render(string loginToken, string nonce)
    {
        string html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Complete sign-in</title>
            </head>
            <body>
            <main>
            <h1>Complete sign-in to Prism</h1>
            <p>The EMSolutions Account Portal sent you here. Continue only if you just started signing in to Prism.</p>
            <form method="post" action="/api/auth/sso/confirm">
            <input type="hidden" name="loginToken" value="{{WebUtility.HtmlEncode(loginToken)}}">
            <input type="hidden" name="nonce" value="{{WebUtility.HtmlEncode(nonce)}}">
            <button type="submit">Complete sign-in</button>
            </form>
            </main>
            </body>
            </html>
            """;

        return new HtmlPageResult(html);
    }

    private sealed class HtmlPageResult(string html) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            HttpResponse response = httpContext.Response;
            response.ContentType = "text/html; charset=utf-8";
            response.Headers.CacheControl = "no-store";
            response.Headers["X-Frame-Options"] = "DENY";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers.ContentSecurityPolicy = "default-src 'none'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";
            await response.WriteAsync(html, httpContext.RequestAborted);
        }
    }
}
