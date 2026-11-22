using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Infrastructure.Persistence;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Auth;

/// <summary>The real API with the portal replaced: a test key pair signs login tokens and a fake directory answers the nonce exchange.</summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public const string PortalBase = "https://account.example.test";

    public AuthTestFactory(
        bool allowIdpInitiated = false,
        bool secureCookies = false,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null)
    {
        AllowIdpInitiated = allowIdpInitiated;
        SecureCookies = secureCookies;
        ExtraSettings = settings ?? new Dictionary<string, string?>();
        ConfigureTestServices = configureServices;
    }

    public PortalTestKeys Portal { get; } = new();

    public FakePortalDirectory Directory { get; } = new();

    /// <summary>A hand-written stand-in for the Prism database, so these tests need no Postgres. The real store is tested against Postgres.</summary>
    public FakeSessionRevocationStore Revocations { get; } = new();

    public FakeAuditLog Audit { get; } = new();

    /// <summary>Stands in for the database connection check behind /ready.</summary>
    public FakeDatabaseProbe Database { get; } = new();

    /// <summary>Every log entry the host wrote, for tests that check what must not be logged.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    public string SessionKeyBase64 { get; } = PortalTestKeys.NewSessionKeyBase64();

    private bool AllowIdpInitiated { get; }

    private bool SecureCookies { get; }

    private IReadOnlyDictionary<string, string?> ExtraSettings { get; }

    private Action<IServiceCollection>? ConfigureTestServices { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Prism"] = "Host=unused.invalid;Database=unused",
                ["Auth:ApplicationId"] = PortalTestKeys.ApplicationId,
                ["Auth:AccountPortalBaseUrl"] = PortalBase,
                ["Auth:AdminPortalApiBaseUrl"] = "https://api.example.test",
                ["Auth:LoginTokenIssuer"] = PortalTestKeys.Issuer,
                ["Auth:AdminPortalAudience"] = "admin-portal-api-test",
                ["Auth:LoginTokenPublicKeyUrl"] = "https://account.example.test/key/public",
                ["Auth:ApplicationPrivateKeyPem"] = PortalTestKeys.NewPrivateKeyPem(),
                ["Auth:SessionSigningKeyBase64"] = SessionKeyBase64,
                ["Auth:AllowIdpInitiated"] = AllowIdpInitiated.ToString(),
                ["Auth:RequireSecureCookies"] = SecureCookies.ToString(),
            });
            config.AddInMemoryCollection(ExtraSettings);
        });
        builder.ConfigureServices(services =>
        {
            // Serilog replaces the logging providers, so the capture is a Serilog sink that the host picks up from the services.
            services.AddSingleton<Serilog.Core.ILogEventSink>(Logs);
            ConfigureTestServices?.Invoke(services);
            services.RemoveAll<IDatabaseProbe>();
            services.AddSingleton<IDatabaseProbe>(Database);
            services.RemoveAll<Prism.Application.Audit.IAuditLog>();
            services.AddSingleton<Prism.Application.Audit.IAuditLog>(Audit);
            services.RemoveAll<ISessionRevocationStore>();
            services.AddSingleton<ISessionRevocationStore>(Revocations);
            services.RemoveAll<IPortalDirectory>();
            services.AddSingleton<IPortalDirectory>(Directory);
            services.RemoveAll<IPortalPublicKeyProvider>();
            services.AddSingleton<IPortalPublicKeyProvider>(new FixedKeyProvider(Portal.PublicKeyPem));
        });
    }

    /// <summary>A client that does not follow redirects and keeps no cookies, so tests see and send them by hand.</summary>
    public CookieClient NewClient() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }));

    /// <summary>A client carrying a valid session for a user who has only the given permissions. No sign-in handshake needed.</summary>
    public CookieClient NewSignedInClient(params string[] permissions)
    {
        var user = new PortalUser("tester", "Test User", 7, ["ProviderAnalyst"], []);
        string session = Services.GetRequiredService<ISessionTokenIssuer>().Issue(user, permissions.Length > 0 ? permissions : [Permissions.App, Permissions.DashboardView]);
        CookieClient client = NewClient();
        client.Set("prism_session", session);
        return client;
    }

    /// <summary>A client for a specific user (id and name), for tests where more than one person is involved.</summary>
    public CookieClient NewSignedInClientAs(long portalUserId, string userName, params string[] permissions)
    {
        var user = new PortalUser(userName, userName, portalUserId, ["ProviderAnalyst"], []);
        string session = Services.GetRequiredService<ISessionTokenIssuer>().Issue(user, permissions.Length > 0 ? permissions : [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage]);
        CookieClient client = NewClient();
        client.Set("prism_session", session);
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Portal.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class FixedKeyProvider(string pem) : IPortalPublicKeyProvider
    {
        public Task<string> GetPublicKeyPemAsync(CancellationToken cancellationToken) => Task.FromResult(pem);
    }
}

/// <summary>Stands in for the Admin Portal API. Records every nonce it is asked to exchange.</summary>
public sealed class FakePortalDirectory : IPortalDirectory
{
    private readonly List<string> nonces = [];

    public IReadOnlyList<string> Nonces => nonces;

    public Func<string, PortalUser> Handler { get; set; } = _ => new PortalUser(
        "jdoe", "Jane Doe", 42, ["ProviderAnalyst"], []);

    public List<ProviderScope> Providers { get; } = [new ProviderScope("100", "Mercy Air", ["1", "2"])];

    /// <summary>When set, answers the Provider lookup per user instead of giving everyone <see cref="Providers"/>.</summary>
    public Func<long, IReadOnlyList<ProviderScope>>? ProvidersByUser { get; set; }

    public int ProviderLookups { get; private set; }

    public bool FailProviders { get; set; }

    public Task<PortalUser> ResolveUserAsync(string nonce, CancellationToken cancellationToken)
    {
        nonces.Add(nonce);
        return Task.FromResult(Handler(nonce));
    }

    public Task<IReadOnlyList<ProviderScope>> ResolveProvidersAsync(long portalUserId, CancellationToken cancellationToken)
    {
        ProviderLookups++;
        if (FailProviders)
        {
            throw new HttpRequestException("portal down");
        }

        return Task.FromResult(ProvidersByUser?.Invoke(portalUserId) ?? [.. Providers]);
    }
}

/// <summary>Remembers revoked session ids in memory.</summary>
public sealed class FakeSessionRevocationStore : ISessionRevocationStore
{
    private readonly Dictionary<string, string> revoked = [];

    public IReadOnlyDictionary<string, string> Revoked => revoked;

    public Task RevokeAsync(string sessionId, string userName, DateTimeOffset sessionExpiresAt, CancellationToken cancellationToken)
    {
        revoked[sessionId] = userName;
        return Task.CompletedTask;
    }

    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.FromResult(revoked.ContainsKey(sessionId));
}

public sealed class FakeDatabaseProbe : IDatabaseProbe
{
    public bool Available { get; set; } = true;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(Available);
}

/// <summary>Keeps every log event in memory (as a Serilog sink), with its level, rendered message, properties and any exception text.</summary>
public sealed class CapturingLoggerProvider : Serilog.Core.ILogEventSink
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<(LogLevel Level, string Text)> entries = new();

    public IReadOnlyList<(LogLevel Level, string Text)> Entries => [.. entries];

    public void Emit(Serilog.Events.LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        string properties = string.Join(" ", logEvent.Properties.Select(property => $"{property.Key}={property.Value}"));
        LogLevel level = logEvent.Level switch
        {
            Serilog.Events.LogEventLevel.Verbose => LogLevel.Trace,
            Serilog.Events.LogEventLevel.Debug => LogLevel.Debug,
            Serilog.Events.LogEventLevel.Information => LogLevel.Information,
            Serilog.Events.LogEventLevel.Warning => LogLevel.Warning,
            Serilog.Events.LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Critical,
        };
        entries.Enqueue((level, $"{logEvent.RenderMessage(System.Globalization.CultureInfo.InvariantCulture)} {properties} {logEvent.Exception}"));
    }
}
