using System.Diagnostics;
using System.Globalization;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Prism.Api.Observability;

/// <summary>
/// Structured logs (Serilog) and traces and metrics (OpenTelemetry). Both keep personal data out by construction: request logs carry the
/// path and never the query string, and span tags are stripped of query strings, because a query can hold a login token or a nonce.
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>Tag names that would carry a URL's query string.</summary>
    private static readonly string[] QueryTags = ["url.query", "http.url", "url.full"];

    public static WebApplicationBuilder AddPrismObservability(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Host.UseSerilog((context, services, logger) =>
        {
            logger.ReadFrom.Configuration(context.Configuration)
                .ReadFrom.Services(services)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", "Prism.Api");

            // One JSON object per line where logs are collected. Readable lines on a developer's console.
            if (context.HostingEnvironment.IsDeployedEnvironment())
            {
                logger.WriteTo.Console(new CompactJsonFormatter());
            }
            else
            {
                logger.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {CorrelationId} {SourceContext}: {Message:lj}{NewLine}{Exception}",
                    formatProvider: CultureInfo.InvariantCulture);
            }
        });

        IOpenTelemetryBuilder telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("prism-api"))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequest = (activity, _) => StripQueries(activity);
                    options.EnrichWithHttpResponse = (activity, _) => StripQueries(activity);
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.EnrichWithHttpRequestMessage = (activity, _) => StripQueries(activity);
                    options.EnrichWithHttpResponseMessage = (activity, _) => StripQueries(activity);
                })
                .AddNpgsql())
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        // Export only when a collector is configured (the standard OTEL_EXPORTER_OTLP_ENDPOINT variable). Nothing is sent otherwise.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }

        return builder;
    }

    /// <summary>Keeps the URL of a span but not its query: <c>https://host/path?loginToken=...</c> becomes <c>https://host/path</c>.</summary>
    internal static void StripQueries(Activity activity)
    {
        foreach (string tag in QueryTags)
        {
            object? value = activity.GetTagItem(tag);
            if (value is null)
            {
                continue;
            }

            if (tag == "url.query")
            {
                activity.SetTag(tag, null);
                continue;
            }

            string text = value.ToString() ?? "";
            int cut = text.IndexOf('?', StringComparison.Ordinal);
            activity.SetTag(tag, cut < 0 ? text : text[..cut]);
        }
    }
}
