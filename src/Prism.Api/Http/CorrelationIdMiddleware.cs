using Serilog.Context;

namespace Prism.Api.Http;

/// <summary>
/// Gives every request a correlation id, returns it in <c>X-Correlation-Id</c> and puts it on every log line written during the request.
/// An id sent by the caller is kept only if it is short and plain, so it cannot carry anything into the logs but a label.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    internal const int MaxLength = 64;

    public async Task InvokeAsync(HttpContext context)
    {
        string id = Accept(context.Request.Headers[HeaderName].ToString()) ?? Auth.SecureToken.Hex(8);
        context.Response.Headers[HeaderName] = id;

        // Both: the scope for Microsoft logging, and the Serilog property that the console template and JSON output read.
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = id }))
        using (LogContext.PushProperty("CorrelationId", id))
        {
            await next(context);
        }
    }

    internal static string? Accept(string? candidate) =>
        candidate is { Length: > 0 and <= MaxLength } && candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')
            ? candidate
            : null;
}
