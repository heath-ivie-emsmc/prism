using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Prism.Api.Http;
using Prism.Testing.Common;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration;

[Collection(Prism.Tests.Integration.LogCaptureGroup.Name)]
public sealed class ObservabilityTests
{
    private const string Marker = "SECRET-LOGIN-TOKEN-8842";

    /// <summary>The request log line is written after the response has been sent, so the client can be back before the line exists.</summary>
    private static async Task EventuallyAsync(Func<bool> condition)
    {
        for (int i = 0; i < 50 && !condition(); i++)
        {
            await Task.Delay(40, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Log_events_of_a_request_carry_its_correlation_id()
    {
        using var factory = new AuthTestFactory();
        using CookieClient client = factory.NewClient();

        using HttpResponseMessage response = await client.SendAsync(
            HttpMethod.Get, "/up", headers: new Dictionary<string, string> { [CorrelationIdMiddleware.HeaderName] = "trace-in-logs" });

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        await EventuallyAsync(() => factory.Logs.Entries.Any(entry => entry.Text.Contains("CorrelationId=\"trace-in-logs\"", StringComparison.Ordinal)));
        Assert.Contains(factory.Logs.Entries, entry => entry.Text.Contains("CorrelationId=\"trace-in-logs\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Request_logs_say_what_was_asked_for_but_never_the_query_string()
    {
        using var factory = new AuthTestFactory(allowIdpInitiated: true);
        using CookieClient client = factory.NewClient();

        using HttpResponseMessage start = await client.GetAsync("/api/auth/sso/start");
        using HttpResponseMessage callback = await client.GetAsync($"/api/auth/sso/callback?loginToken={Marker}&state=whatever");

        await EventuallyAsync(() => factory.Logs.Entries.Any(entry => entry.Text.Contains("/api/auth/sso/callback", StringComparison.Ordinal)));
        Assert.Contains(factory.Logs.Entries, entry => entry.Text.Contains("/api/auth/sso/callback", StringComparison.Ordinal));
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_real_sign_in_leaves_no_login_token_or_nonce_in_logs_or_trace_tags()
    {
        var activities = new List<Activity>();
        using var factory = new AuthTestFactory(configureServices: services =>
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(activities)));
        using CookieClient client = factory.NewClient();
        string token = factory.Portal.Mint(DateTimeOffset.UtcNow, new LoginTokenSpec { TokenId = "nonce-trace-77" });

        using HttpResponseMessage start = await client.GetAsync("/api/auth/sso/start");
        string state = System.Web.HttpUtility.ParseQueryString(start.Headers.Location!.Query)["state"]!;
        using HttpResponseMessage callback = await client.GetAsync($"/api/auth/sso/callback?loginToken={Uri.EscapeDataString(token)}&state={state}");
        Assert.Equal("/", callback.Headers.Location!.OriginalString);

        string[] secrets = [token, "nonce-trace-77"];
        foreach (string secret in secrets)
        {
            Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(secret, StringComparison.Ordinal));
            Assert.DoesNotContain(
                activities.SelectMany(activity => activity.TagObjects),
                tag => tag.Value?.ToString()?.Contains(secret, StringComparison.Ordinal) == true);
        }

        Assert.NotEmpty(activities);
    }

    [Fact]
    public void Stripping_removes_the_query_from_every_url_tag_and_drops_the_query_tag()
    {
        using var source = new ActivitySource("test-source");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        using Activity activity = source.StartActivity("span")!;
        activity.SetTag("url.query", "loginToken=abc");
        activity.SetTag("url.full", "https://host/path?nonce_token=abc");
        activity.SetTag("http.url", "https://host/path?x=1");
        activity.SetTag("url.path", "/path");

        Prism.Api.Observability.ObservabilityExtensions.StripQueries(activity);

        Assert.Null(activity.GetTagItem("url.query"));
        Assert.Equal("https://host/path", activity.GetTagItem("url.full"));
        Assert.Equal("https://host/path", activity.GetTagItem("http.url"));
        Assert.Equal("/path", activity.GetTagItem("url.path"));
    }
}
