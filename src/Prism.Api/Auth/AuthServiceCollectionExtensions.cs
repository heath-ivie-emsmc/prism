using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Api.Auth;

/// <summary>Composition root for sign-in: options, portal integration, the session cookie scheme and rate limits.</summary>
public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddPrismAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<PrismAuthOptions>, PrismAuthOptionsValidator>();
        services.AddSingleton<IValidateOptions<PrismAuthOptions>, DeployedAuthOptionsValidator>();
        services.AddOptions<PrismAuthOptions>()
            .Bind(configuration.GetSection(PrismAuthOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuthCookies>();
        services.AddSingleton<PortalAppJwtFactory>();
        services.AddSingleton<PortalCompanyNames>();
        services.AddSingleton<UserScopeCache>();
        services.AddScoped<IUserScopeProvider, UserScopeProvider>();
        services.AddSingleton<ISessionTokenIssuer, SessionTokenIssuer>();
        services.AddSingleton<ILoginTokenValidator, PortalLoginTokenValidator>();
        services.AddSingleton<IPortalPublicKeyProvider, HttpPortalPublicKeyProvider>();
        services.AddScoped<SignInService>();
        services.AddScoped<SignInFlow>();

        services.AddHttpClient(HttpPortalPublicKeyProvider.HttpClientName, ConfigurePortalClient);
        services.AddHttpClient<IPortalDirectory, PortalDirectory>(ConfigurePortalClient);

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<PrismAuthOptions>, AuthCookies>(ConfigureSessionScheme);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization(options =>
        {
            options.AddPolicy(Permissions.DashboardView, policy => policy.RequireClaim(SessionTokenIssuer.PermissionsClaim, Permissions.DashboardView));
            options.AddPolicy(Permissions.ClaimsView, policy => policy.RequireClaim(SessionTokenIssuer.PermissionsClaim, Permissions.ClaimsView));
            options.AddPolicy(Permissions.ViewsManage, policy => policy.RequireClaim(SessionTokenIssuer.PermissionsClaim, Permissions.ViewsManage));
        });

        services.AddSingleton<Attention.ClaimReadLimiter>();
        services.AddRateLimiter(options => options.RejectionStatusCode = StatusCodes.Status429TooManyRequests);
        // The limits come from the bound options, read when the limiter is first used, so late-added configuration counts.
        services.AddOptions<RateLimiterOptions>().Configure<IOptions<PrismAuthOptions>>((options, auth) =>
        {
            AddPerClientPolicy(options, AuthEndpoints.RateLimitPolicy, Math.Max(1, auth.Value.RateLimitPerMinute));
            AddPerClientPolicy(options, AuthEndpoints.CallbackRateLimitPolicy, Math.Max(1, auth.Value.CallbackRateLimitPerMinute));
            AddPerClientPolicy(options, SavedViews.SavedViewEndpoints.InvitationRateLimitPolicy, Math.Max(1, auth.Value.RateLimitPerMinute));
            AddPerClientPolicy(options, Attention.ClaimAttentionEndpoints.RateLimitPolicy, Math.Max(1, auth.Value.RateLimitPerMinute));
        });

        return services;
    }

    // Partitioned by the client address that forwarded-header trust produced (the connection address when no proxy is trusted).
    private static void AddPerClientPolicy(RateLimiterOptions options, string policy, int permitLimit) =>
        options.AddPolicy(policy, httpContext =>
            RateLimitPartition.GetFixedWindowLimiter(
                ClientAddress.PartitionKey(httpContext.Connection.RemoteIpAddress),
                _ => new FixedWindowRateLimiterOptions { PermitLimit = permitLimit, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));

    private static void ConfigurePortalClient(HttpClient client)
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.MaxResponseContentBufferSize = 256 * 1024;
    }

    // A valid signature is not enough: the session must not have been ended early (sign-out or revocation).
    private static async Task RejectRevokedSession(TokenValidatedContext context)
    {
        string? sessionId = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Jti);
        if (string.IsNullOrEmpty(sessionId))
        {
            context.Fail("Session has no id.");
            return;
        }

        var store = context.HttpContext.RequestServices.GetRequiredService<ISessionRevocationStore>();
        if (await store.IsRevokedAsync(sessionId, context.HttpContext.RequestAborted))
        {
            context.Fail("Session was revoked.");
        }
    }

    private static void ConfigureSessionScheme(JwtBearerOptions options, IOptions<PrismAuthOptions> auth, AuthCookies cookies)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = SessionTokenIssuer.Issuer,
            ValidateAudience = true,
            ValidAudience = SessionTokenIssuer.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = SessionTokenIssuer.SigningKey(auth.Value.SessionSigningKeyBase64),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = SessionTokenIssuer.SubjectClaim,
            RoleClaimType = SessionTokenIssuer.RolesClaim,
        };
        // The session lives only in the HttpOnly cookie. A bearer header is never read.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                string? session = context.Request.Cookies[cookies.Session];
                if (string.IsNullOrEmpty(session))
                {
                    // Without this, JwtBearer falls back to the Authorization header when the token is empty.
                    context.NoResult();
                }
                else
                {
                    context.Token = session;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = RejectRevokedSession,
        };
    }
}
