using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Application.Customers;
using Prism.Infrastructure.Persistence;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Auth;

/// <summary>The real API with the portal replaced: a test key pair signs login tokens and a fake directory answers the nonce exchange.</summary>
public sealed class AuthTestFactory : WebApplicationFactory<Program>
{
    public const string PortalBase = "https://account.example.test";

    public const string InternalGroupName = "EMSbilling.com Users";

    public AuthTestFactory(
        bool allowIdpInitiated = false,
        bool secureCookies = false,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IServiceCollection>? configureServices = null,
        bool verifyWarehouseWriteProtection = false)
    {
        VerifyWarehouseWriteProtection = verifyWarehouseWriteProtection;
        AllowIdpInitiated = allowIdpInitiated;
        SecureCookies = secureCookies;
        ExtraSettings = settings ?? new Dictionary<string, string?>();
        ConfigureTestServices = configureServices;
    }

    public PortalTestKeys Portal { get; } = new();

    public FakeProviderList ProviderList { get; } = new();

    public FakePortalDirectory Directory { get; } = new();

    /// <summary>A hand-written stand-in for the Prism database, so these tests need no Postgres. The real store is tested against Postgres.</summary>
    public FakeSessionRevocationStore Revocations { get; } = new();

    public FakeAuditLog Audit { get; } = new();

    /// <summary>
    /// Stands in for the Customer Role store: a user holds exactly the customer permissions the test gave when it signed them in, in every Provider.
    /// A test of the real store replaces it (see <see cref="UseRealCustomerAccess"/>).
    /// </summary>
    public FakeCustomerAccess Customers { get; } = new();

    /// <summary>For <c>configureServices</c>: use the real Customer Role store (which needs Postgres) instead of the stand-in.</summary>
    public static void UseRealCustomerAccess(IServiceCollection services)
    {
        services.RemoveAll<ICustomerAccess>();
        services.AddScoped<ICustomerAccess>(sp => sp.GetRequiredService<Prism.Infrastructure.Customers.CustomerAccessStore>());
    }

    /// <summary>Stands in for the database connection check behind /ready.</summary>
    public FakeDatabaseProbe Database { get; } = new();

    /// <summary>Every log entry the host wrote, for tests that check what must not be logged.</summary>
    public CapturingLoggerProvider Logs { get; } = new();

    public string SessionKeyBase64 { get; } = PortalTestKeys.NewSessionKeyBase64();

    private bool AllowIdpInitiated { get; }

    /// <summary>
    /// False (the default) replaces the start-up write protection check with an accepting stand-in, because most hosts here name a warehouse that does not exist
    /// (dw.invalid) just to test wiring. The check itself, and a host that really runs it, are tested against a throwaway container database.
    /// </summary>
    private bool VerifyWarehouseWriteProtection { get; }

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
                ["Internal:GroupName"] = InternalGroupName,
                // Environment variables are part of host configuration. A developer with the real warehouse connection in their shell must never reach it from a test host.
                ["ConnectionStrings:Warehouse"] = "",
            });
            config.AddInMemoryCollection(ExtraSettings);
        });
        builder.ConfigureServices(services =>
        {
            // Serilog replaces the logging providers, so the capture is a Serilog sink that the host picks up from the services.
            services.AddSingleton<Serilog.Core.ILogEventSink>(Logs);
            services.RemoveAll<ICustomerAccess>();
            services.AddSingleton<ICustomerAccess>(Customers);
            ConfigureTestServices?.Invoke(services);
            if (!VerifyWarehouseWriteProtection)
            {
                services.RemoveAll<Prism.Infrastructure.Warehouse.IWarehouseWriteProtectionCheck>();
                services.AddSingleton<Prism.Infrastructure.Warehouse.IWarehouseWriteProtectionCheck, AcceptingWriteProtectionCheck>();
            }

            services.RemoveAll<IDatabaseProbe>();
            services.AddSingleton<IDatabaseProbe>(Database);
            services.RemoveAll<Prism.Application.Audit.IAuditLog>();
            services.AddSingleton<Prism.Application.Audit.IAuditLog>(Audit);
            services.RemoveAll<ISessionRevocationStore>();
            services.AddSingleton<ISessionRevocationStore>(Revocations);
            services.RemoveAll<IPortalDirectory>();
            services.AddSingleton<IPortalDirectory>(Directory);
            services.RemoveAll<IProviderListSource>();
            services.AddSingleton<IProviderListSource>(ProviderList);
            services.RemoveAll<IPortalPublicKeyProvider>();
            services.AddSingleton<IPortalPublicKeyProvider>(new FixedKeyProvider(Portal.PublicKeyPem));
        });
    }

    /// <summary>A client that does not follow redirects and keeps no cookies, so tests see and send them by hand.</summary>
    public CookieClient NewClient() => new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }));

    private long lastUserId = 70_000;

    /// <summary>
    /// Each client made without an explicit user id gets its own, so two clients with different permissions in one test never share a stand-in role.
    /// </summary>
    private long NextUserId() => Interlocked.Increment(ref lastUserId);

    /// <summary>A client carrying a valid session for a user who has only the given permissions. No sign-in handshake needed.</summary>
    public CookieClient NewSignedInClient(params string[] permissions)
    {
        long id = NextUserId();
        var user = new PortalUser("tester", "Test User", id, ["PrismUser"], []);
        string[] given = permissions.Length > 0 ? permissions : [Permissions.App, Permissions.DashboardView];
        Customers.Grant(id, given);
        string session = Services.GetRequiredService<ISessionTokenIssuer>().Issue(user, given);
        CookieClient client = NewClient();
        client.Set("prism_session", session);
        return client;
    }

    /// <summary>A client whose session was issued as if the portal reported these user groups and roles' permissions.</summary>
    public CookieClient NewSignedInClientInGroups(string[] groups, params string[] permissions)
    {
        long id = NextUserId();
        var user = new PortalUser("tester", "Test User", id, ["PrismUser"], []) { Groups = groups };
        IReadOnlyList<string> kept = InternalAccess.Filter(permissions, InternalAccess.IsInternal(groups, InternalGroupName));
        Customers.Grant(id, permissions);
        string session = Services.GetRequiredService<ISessionTokenIssuer>().Issue(user, kept);
        CookieClient client = NewClient();
        client.Set("prism_session", session);
        return client;
    }

    /// <summary>A client for a specific user (id and name), for tests where more than one person is involved.</summary>
    public CookieClient NewSignedInClientAs(long portalUserId, string userName, params string[] permissions)
    {
        var user = new PortalUser(userName, userName, portalUserId, ["PrismUser"], []);
        string[] given = permissions.Length > 0 ? permissions : [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];
        Customers.Grant(portalUserId, given);
        string session = Services.GetRequiredService<ISessionTokenIssuer>().Issue(user, given);
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

    private sealed class AcceptingWriteProtectionCheck : Prism.Infrastructure.Warehouse.IWarehouseWriteProtectionCheck
    {
        public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
        "jdoe", "Jane Doe", 42, ["PrismUser"], []);

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

/// <summary>Stands in for the warehouse's Provider list, for the staff Provider search.</summary>
public sealed class FakeProviderList : IProviderListSource
{
    public List<ProviderChoice> Companies { get; } = [new ProviderChoice("100", "Mercy Air"), new ProviderChoice("526", "Seminole County Fire Department"), new ProviderChoice("9001", "Abbeville County Emergency Services")];

    public Task<IReadOnlyList<ProviderChoice>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ProviderChoice>>([.. Companies]);
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

/// <summary>A hand-written Customer Role store: each user holds the customer permissions a test gave them, in every Provider.</summary>
public sealed class FakeCustomerAccess : ICustomerAccess
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<long, IReadOnlySet<string>> grants = new();

    /// <summary>Admin tiers and the sign-in permission are not customer permissions: they stay in the session.</summary>
    public void Grant(long portalUserId, IEnumerable<string> permissions) =>
        grants[portalUserId] = permissions.Where(p => p != Permissions.App && !Permissions.AdminTiers.Contains(p)).ToHashSet(StringComparer.Ordinal);

    private IReadOnlySet<string> Held(long portalUserId) => grants.TryGetValue(portalUserId, out IReadOnlySet<string>? held) ? held : new HashSet<string>();

    public Task<IReadOnlySet<string>> PermissionsAsync(int providerId, long portalUserId, CancellationToken cancellationToken) => Task.FromResult(Held(portalUserId));

    public Task<IReadOnlySet<string>> PermissionsInAnyProviderAsync(long portalUserId, CancellationToken cancellationToken) => Task.FromResult(Held(portalUserId));

    public Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> RecordAsync(long portalUserId, string displayName, IReadOnlyList<int> providerIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<int, IReadOnlySet<string>>>(providerIds.Distinct().ToDictionary(id => id, _ => Held(portalUserId)));
}
