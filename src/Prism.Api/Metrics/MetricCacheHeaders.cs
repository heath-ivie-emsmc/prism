using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;

namespace Prism.Api.Metrics;

/// <summary>
/// Revalidation for aggregate Metric responses. It runs inside the authorized endpoint, after the Provider check, so a caller who may not read the data
/// gets the 403 or 404 the handler returns and never a 304. The ETag is a strong hash of the serialized body, so it changes when the data or the query does.
/// Claim-level routes do not use it: they stay <c>no-store</c> with no validator.
/// </summary>
internal static class MetricCacheHeaders
{
    internal const string RevalidateCacheControl = "private, no-cache";
    internal const string CatalogCacheControl = "private, max-age=300";
    private const int MaxIfNoneMatchLength = 2048;
    private const int MaxIfNoneMatchTags = 32;

    public static async ValueTask<object?> Revalidate(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        object? result = await next(context);
        HttpContext http = context.HttpContext;
        if (!HttpMethods.IsGet(http.Request.Method) || result is not IValueHttpResult { Value: { } value } ok || ok is not IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK })
        {
            return result;
        }

        JsonSerializerOptions options = http.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, value.GetType(), options);
        string etag = $"\"{Convert.ToBase64String(SHA256.HashData(body)).TrimEnd('=')}\"";
        http.Response.Headers.CacheControl = RevalidateCacheControl;
        http.Response.Headers.ETag = etag;
        return Matches(http.Request.Headers.IfNoneMatch.ToString(), etag) ? Results.StatusCode(StatusCodes.Status304NotModified) : result;
    }

    public static async ValueTask<object?> Catalog(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        object? result = await next(context);
        // Only a served catalog is cacheable: a 404 for a Provider the caller may not read must not be kept by a browser or proxy.
        if (result is IStatusCodeHttpResult { StatusCode: StatusCodes.Status200OK })
        {
            context.HttpContext.Response.Headers.CacheControl = CatalogCacheControl;
        }

        return result;
    }

    private static bool Matches(string header, string etag)
    {
        if (header.Length is 0 or > MaxIfNoneMatchLength)
        {
            return false;
        }

        if (header.Trim() == "*")
        {
            return true;
        }

        foreach (string candidate in header.Split(',', MaxIfNoneMatchTags, StringSplitOptions.TrimEntries))
        {
            string tag = candidate.StartsWith("W/", StringComparison.Ordinal) ? candidate[2..] : candidate;
            if (string.Equals(tag, etag, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
