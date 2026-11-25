using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;
using Prism.Testing.Common;

namespace Prism.Tests.Auth;

/// <summary>The whole exchange against a stand-in portal: user-data, then roles, assigned companies and company names.</summary>
public sealed class PortalDirectoryExchangeTests : IDisposable
{
    private const string UserData = """{"type":"result","result":{"id":7,"admin_portal_id":7,"first_name":"Jane","last_name":"Doe","username":"jdoe"}}""";
    private const string Rbac = """{"result":{"assignments":[{"user_id":7,"item_name":"prism-localhost__ProviderAdmin"}],"items":[],"children":[]}}""";

    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly StubPortal portal = new();
    private readonly TestTimeProvider clock = new(Now);

    public void Dispose() => portal.Dispose();

    // Sign-in reads the user and roles. The Providers are read separately (they can be large), so this does both, as a request would.
    private static async Task<PortalUser> Resolve(PortalDirectory directory, string nonce)
    {
        PortalUser user = await directory.ResolveUserAsync(nonce, TestContext.Current.CancellationToken);
        IReadOnlyList<ProviderScope> providers = await directory.ResolveProvidersAsync(user.PortalUserId ?? 0, TestContext.Current.CancellationToken);
        return user with { Providers = providers };
    }

    private PortalDirectory Directory(PortalCompanyNames? names = null)
    {
        var options = Options.Create(new PrismAuthOptions
        {
            ApplicationId = "prism-localhost",
            AdminPortalAudience = "admin-portal-api-dev",
            AdminPortalApiBaseUrl = "https://api.example.test",
            ApplicationPrivateKeyPem = PortalTestKeys.NewPrivateKeyPem(),
        });
        return new PortalDirectory(
            new HttpClient(portal),
            new PortalAppJwtFactory(new TestOptionsMonitor<PrismAuthOptions>(options.Value), clock),
            options,
            names ?? new PortalCompanyNames(clock),
            NullLogger<PortalDirectory>.Instance);
    }

    private static string Company(int id, string name) => $$"""{"id":{{id}},"name":"{{name}}","integration_id":"x"}""";

    [Fact]
    public async Task Reads_roles_and_named_Providers_with_their_Facilities()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"type":"result","result":{"12":[1,2],"34":[]}}""";
        portal.Responses["company/batch?from_id=11"] = $$"""{"result":[{{Company(12, "Mercy Air")}},{{Company(13, "Next")}}]}""";
        portal.Responses["company/batch?from_id=33"] = $$"""{"result":[{{Company(34, "Acme Air Care")}}]}""";

        PortalUser user = await Resolve(Directory(), "nonce");

        Assert.Equal("jdoe", user.UserName);
        Assert.Equal(["prism-localhost__ProviderAdmin"], user.Roles);
        Assert.Equal(
            [("12", "Mercy Air", "1,2"), ("34", "Acme Air Care", "")],
            user.Providers.Select(p => (p.Id, p.Name, string.Join(',', p.FacilityIds))));
    }

    [Fact]
    public async Task Every_portal_call_carries_the_app_token_and_application_id_and_asks_by_the_portal_user_id()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"result":{"12":[]}}""";
        portal.Responses["company/batch?from_id=11"] = $$"""{"result":[{{Company(12, "Mercy Air")}}]}""";

        await Resolve(Directory(), "n-1");

        Assert.All(portal.Requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("prism-localhost", request.Headers.GetValues("X-Ems-Application-Id").Single());
        });
        Assert.Contains(portal.Requests, r => r.RequestUri!.PathAndQuery == "/user/rbac-data?user_id=7");
        Assert.Contains(portal.Requests, r => r.RequestUri!.PathAndQuery == "/user/assigned-companies?user_id=7");
        Assert.Contains(portal.Requests, r => r.RequestUri!.PathAndQuery == "/company/batch?from_id=11");
    }

    [Fact]
    public async Task A_company_the_portal_does_not_return_keeps_its_id_as_the_name()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"result":{"99":[]}}""";
        portal.Responses["company/batch?from_id=98"] = $$"""{"result":[{{Company(100, "Not the one asked for")}}]}""";

        PortalUser user = await Resolve(Directory(), "n");

        Assert.Equal(("99", "99"), (user.Providers[0].Id, user.Providers[0].Name));
    }

    [Fact]
    public async Task A_failing_name_lookup_does_not_fail_sign_in()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"result":{"12":[]}}""";
        portal.Failing.Add("company/batch");

        PortalUser user = await Resolve(Directory(), "n");

        Assert.Equal("12", user.Providers.Single().Name);
    }

    [Fact]
    public async Task A_failing_assigned_companies_call_fails_sign_in_rather_than_signing_in_with_no_scope()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Failing.Add("user/assigned-companies");

        await Assert.ThrowsAsync<HttpRequestException>(() => Resolve(Directory(), "n"));
    }

    [Fact]
    public async Task A_user_with_no_assigned_companies_gets_no_Providers_and_the_directory_is_not_read()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"type":"result","result":[]}""";

        PortalUser user = await Resolve(Directory(), "n");

        Assert.Empty(user.Providers);
        Assert.DoesNotContain(portal.Requests, r => r.RequestUri!.AbsolutePath == "/company/batch");
    }

    [Fact]
    public async Task Names_are_remembered_between_sign_ins()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = """{"result":{"12":[]}}""";
        portal.Responses["company/batch?from_id=11"] = $$"""{"result":[{{Company(12, "Mercy Air")}}]}""";
        var names = new PortalCompanyNames(clock);
        PortalDirectory directory = Directory(names);

        await Resolve(directory, "a");
        await Resolve(directory, "b");

        Assert.Equal(1, portal.Requests.Count(r => r.RequestUri!.AbsolutePath == "/company/batch"));
    }

    [Fact]
    public async Task Roles_already_in_user_data_are_used_without_asking_for_them_again()
    {
        portal.Responses["user/user-data"] = """{"result":{"id":7,"username":"jdoe","roleNames":["SuperUser"]}}""";
        portal.Responses["user/assigned-companies"] = """{"result":{}}""";

        PortalUser user = await Resolve(Directory(), "n");

        Assert.Equal(["SuperUser"], user.Roles);
        Assert.DoesNotContain(portal.Requests, r => r.RequestUri!.AbsolutePath == "/user/rbac-data");
    }

    [Fact]
    public async Task Only_the_first_few_companies_are_looked_up_by_name()
    {
        portal.Responses["user/user-data"] = UserData;
        portal.Responses["user/rbac-data"] = Rbac;
        portal.Responses["user/assigned-companies"] = "{\"result\":{" + string.Join(",", Enumerable.Range(1, 40).Select(i => $"\"{i}\":[]")) + "}}";
        portal.Responses["company/batch"] = "{\"result\":[]}";

        PortalUser user = await Resolve(Directory(), "n");

        Assert.Equal(40, user.Providers.Count);
        Assert.Equal(PortalDirectory.MaxNameLookups, portal.Requests.Count(r => r.RequestUri!.AbsolutePath == "/company/batch"));
    }

    private sealed class StubPortal : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; } = [];

        public HashSet<string> Failing { get; } = [];

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            string path = request.RequestUri!.AbsolutePath.TrimStart('/');
            string withQuery = request.RequestUri.PathAndQuery.TrimStart('/');
            if (Failing.Contains(path))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            }

            string? body = Responses.GetValueOrDefault(withQuery) ?? Responses.GetValueOrDefault(path);
            return Task.FromResult(body is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
