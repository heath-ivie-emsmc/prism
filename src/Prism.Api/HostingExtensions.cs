using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;

namespace Prism.Api;

/// <summary>Forwarded-header trust and baseline security headers.</summary>
public static class HostingExtensions
{
    private const string ForwardedSection = "ForwardedHeaders";

    /// <summary>
    /// Report-only for now: the SPA has not been run under it in a real browser yet. Once the console is clean this becomes
    /// <c>Content-Security-Policy</c>.
    /// </summary>
    internal const string ContentSecurityPolicyReportOnly =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self' data:; "
        + "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

    /// <summary>
    /// Trusts <c>X-Forwarded-For</c> and <c>X-Forwarded-Proto</c> only from the proxies and networks listed under
    /// <c>ForwardedHeaders:KnownProxies</c> and <c>ForwardedHeaders:KnownNetworks</c> (CIDR). With neither listed the headers are ignored
    /// completely, so a caller cannot choose its own rate-limit key.
    /// </summary>
    public static IServiceCollection AddPrismForwardedHeaders(this IServiceCollection services)
    {
        // Read when the options are first used, not at registration: configuration added late (tests, hosting startup) must count.
        services.AddOptions<ForwardedHeadersOptions>().Configure<IConfiguration>((options, configuration) =>
        {
            string[] proxies = configuration.GetSection($"{ForwardedSection}:KnownProxies").Get<string[]>() ?? [];
            string[] networks = configuration.GetSection($"{ForwardedSection}:KnownNetworks").Get<string[]>() ?? [];

            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (string proxy in proxies.Take(50))
            {
                options.KnownProxies.Add(System.Net.IPAddress.Parse(proxy));
            }

            foreach (string network in networks.Take(50))
            {
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
            }
        });
        return services;
    }

    public static IApplicationBuilder UsePrismForwardedHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        ForwardedHeadersOptions options = app.ApplicationServices.GetRequiredService<IOptions<ForwardedHeadersOptions>>().Value;
        return options.KnownProxies.Count + options.KnownIPNetworks.Count > 0 ? app.UseForwardedHeaders() : app;
    }

    public static IApplicationBuilder UsePrismSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.Use((context, next) =>
        {
            IHeaderDictionary headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Content-Security-Policy-Report-Only"] = ContentSecurityPolicyReportOnly;
            return next(context);
        });
    }
}
