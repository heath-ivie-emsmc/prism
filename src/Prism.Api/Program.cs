using Microsoft.OpenApi;
using Prism.Api;
using Prism.Api.Auth;
using Prism.Api.Health;
using Prism.Api.Observability;
using Prism.Infrastructure.Configuration;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Secrets come from AWS Secrets Manager when Aws:SecretsManager:SecretIds names any (deployed environments), and from user secrets locally.
builder.Configuration.AddPrismSecretsManager();

builder.AddPrismObservability();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "Prism API";
    document.Info.Description = "Revenue cycle metrics for ambulance and fire Providers. Every route needs a signed-in session. Responses are aggregate-only unless a route says otherwise.";
    return Task.CompletedTask;
}));
builder.Services.AddProblemDetails();
builder.Services.AddPrismForwardedHeaders();
builder.Services.AddPrismPersistence(builder.Configuration);
builder.Services.AddPrismAuth(builder.Configuration);

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
app.UseSerilogRequestLogging();
app.UseExceptionHandler();

// Static files come first, before routing: the static file middleware skips any request that already matched an endpoint,
// and the SPA fallback below matches almost everything. One container serves the SPA.
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();

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
api.MapGroup(ApiRoutes.V1).MapIdentityEndpoints();

// Unknown paths fall back to index.html so client-side routes work on reload, except /api (and /api/...), where an
// unknown path must stay a plain 404 and never turn into HTML. The docs and OpenAPI routes are real endpoints and match first.
app.MapFallbackToFile("{*path:regex(^(?!api(/|$)).*$)}", "index.html");

await app.RunAsync();

// Exposed so integration tests can use WebApplicationFactory<Program>. It cannot be static.
#pragma warning disable S1118
public partial class Program;
#pragma warning restore S1118
