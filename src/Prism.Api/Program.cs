using Microsoft.OpenApi;
using Prism.Api;
using Prism.Api.Admin;
using Prism.Api.Customers;
using Prism.Api.Personas;
using Prism.Api.Auth;
using Prism.Api.Health;
using Prism.Api.Metrics;
using Prism.Api.Observability;
using Prism.Api.SavedViews;
using Prism.Infrastructure.Configuration;
using Prism.Infrastructure.FeatureManagement;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from AWS Secrets Manager when Aws:SecretsLoader is enabled (deployed environments), and from user secrets locally.
// Order matters: the loader first, then (later) the App Config merge, then the break-glass overrides last so nothing outranks them.
AwsSecretsLoaderResult secrets = await builder.Configuration.AddAwsSecretsLoaderAsync(builder.Environment);
await builder.Configuration.MergeSidecarAppSettingsIfEnabledAsync();
secrets = builder.Configuration.ApplySecretOverridesFromEnvironment(secrets);

builder.AddPrismObservability();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Prism API";
    document.Info.Description = "Revenue cycle metrics for ambulance and fire Providers. Every route needs a signed-in session. Responses are aggregate-only unless a route says otherwise.";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddPrismForwardedHeaders();
builder.Services.AddPrismFeatureManagement(builder.Configuration);
builder.Services.AddPrismPersistence(builder.Configuration);
builder.Services.AddPrismAuth(builder.Configuration);
builder.Services.AddPrismMetrics();
builder.Services.AddPrismSavedViews();
builder.Services.AddPrismAdmin();
builder.Services.AddPrismPersonas();
builder.Services.AddPrismCustomerAccess();
// Hourly refresh of the loaded secrets. Does nothing when the loader is disabled.
builder.Services.AddAwsSecretsLoaderReloading(builder.Configuration);

var app = builder.Build();

// First: later middleware (rate limiting, HTTPS checks) must see the client address and scheme the trusted proxy forwarded.
app.UsePrismForwardedHeaders();
if (app.Environment.IsDeployedEnvironment())
{
    app.UseHsts();
}

app.UsePrismSecurityHeaders();
app.UseMiddleware<Prism.Api.Http.CorrelationIdMiddleware>();

// One line per request: method, path (never the query), status, time. Before the exception handler so failures are logged too.
// The request log writes through this host's own logger, not the process-wide static one, which belongs to whichever host started last
// (several hosts share a process in the integration tests).
app.UseSerilogRequestLogging(options => options.Logger = app.Services.GetRequiredService<Serilog.ILogger>());
app.UseExceptionHandler();

// Static files come first, before routing: the static file middleware skips any request that already matched an endpoint,
// and the SPA fallback below matches almost everything. One container serves the SPA.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

// After routing, so the endpoint's declared body size is known, and before anything reads the body.
app.UseMiddleware<Prism.Api.Http.RequestSizeLimitMiddleware>();

// CSRF runs before authentication so a forged request is refused without touching the session.
app.UseMiddleware<CsrfMiddleware>();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Liveness: the process is up. Readiness: Prism's database answers. Anonymous, so the check is shared and cached and says nothing about why.
app.MapGet("/up", () => Results.Ok(new { status = "up" }))
    .AllowAnonymous()
    .WithTags("Health")
    .WithSummary("Liveness")
    .WithDescription("Returns 200 while the process is running. It never touches the database.")
    .Produces(StatusCodes.Status200OK);
app.MapGet("/ready", async (ReadinessCheck readiness) =>
        await readiness.IsReadyAsync()
            ? Results.Ok(new { status = "ready" })
            : Results.Json(new { status = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable))
    .AllowAnonymous()
    .WithTags("Health")
    .WithSummary("Readiness")
    .WithDescription("Returns 200 when Prism's database accepts connections, otherwise 503 with no detail. The check is shared and cached for 5 seconds.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status503ServiceUnavailable);

// The API description and its reference page are for signed-in users, in every environment, so the public contract is not browsable by strangers.
app.MapOpenApi().RequireAuthorization();
app.MapScalarApiReference("/docs", options => options.WithTitle("Prism API")).RequireAuthorization();

// Everything under /api needs a signed-in user unless a route says otherwise (the sign-in handshake does).
// The handshake is a browser flow registered with the portal, so it is not versioned. The JSON API lives under /api/v1.
RouteGroupBuilder api = app.MapGroup("/api").RequireAuthorization();
api.MapSsoEndpoints();
RouteGroupBuilder v1 = api.MapGroup(ApiRoutes.V1);
v1.MapIdentityEndpoints();
v1.MapMetricEndpoints();
v1.MapSavedViewEndpoints();
v1.MapAdminEndpoints();
v1.MapPersonaEndpoints();
v1.MapCustomerAccessEndpoints();

// Unknown paths fall back to index.html so client-side routes work on reload, except /api (and /api/...), where an
// unknown path must stay a plain 404 and never turn into HTML. The docs and OpenAPI routes are real endpoints and match first.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$)).*$)}", "index.html");

await app.RunAsync();

// Exposed so integration tests can use WebApplicationFactory<Program>. It cannot be static.
#pragma warning disable S1118
public partial class Program;
#pragma warning restore S1118
