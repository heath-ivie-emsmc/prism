using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Prism.Application.Auth;
using Prism.Application.SavedViews;
using Prism.Application.Warehouse;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.SavedViews;

/// <summary>
/// The Saved View routes through the real host and real PostgreSQL: who may call them, which Provider they may name, what bad input does,
/// and the whole invitation journey. Users: 31 owns views and 32 receives them, both in Provider 100; 33 is only in Provider 300; 34 is limited to one Facility of 100.
/// </summary>
public sealed class SavedViewEndpointTests : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Views = "/api/v1/providers/100/saved-views";
    private const string Marker = "PHI-MARKER-Jane-Doe-1970-01-01";
    private static readonly string[] AllPermissions = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];
    private static readonly string[] Padded = ["  Guest@Example.org "];
    private static readonly string[] Guest = ["guest@example.org"];
    private static readonly string[] Anyone = ["a@example.org"];
    private static readonly string[] ReadOnlyPermissions = [Permissions.App, Permissions.DashboardView];

    private readonly RecordingSender sender = new();
    private readonly AuthTestFactory factory;
    private int counter;

    public SavedViewEndpointTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(
            settings: new Dictionary<string, string?>
            {
                ["ConnectionStrings:Prism"] = postgres.ConnectionString,
                ["SavedViews:Invitations:Enabled"] = "true",
                ["SavedViews:Invitations:PublicBaseUrl"] = "https://prism.example.test",
                ["SavedViews:Invitations:SmtpHost"] = "smtp.example.test",
                ["SavedViews:Invitations:FromAddress"] = "prism@example.test",
            },
            configureServices: services =>
            {
                services.RemoveAll<IInvitationEmailSender>();
                services.AddSingleton<IInvitationEmailSender>(sender);
                services.RemoveAll<IProviderDivisionDirectory>();
                services.AddSingleton<IProviderDivisionDirectory>(new Divisions());
            });
        factory.Directory.ProvidersByUser = user => user switch
        {
            33 => [new ProviderScope("300", "Elsewhere", [])],
            34 => [new ProviderScope("100", "Mercy Air", ["9"])],
            _ => [new ProviderScope("100", "Mercy Air", [])],
        };
    }

    public void Dispose() => factory.Dispose();

    private CookieClient Client(long user, params string[] permissions) => WithXsrf(factory.NewSignedInClientAs(user, $"user{user}", permissions.Length > 0 ? permissions : AllPermissions));

    private static CookieClient WithXsrf(CookieClient client)
    {
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private string Unique() => $"view-{Interlocked.Increment(ref counter)}-{Guid.NewGuid():N}";

    private static object Layout(int widgets = 1, string metric = "net_collections", string presentation = "line") => new
    {
        version = 1,
        density = "comfortable",
        widgets = Enumerable.Range(0, widgets).Select(i => new { id = $"w{i}", metricKey = metric, presentation, x = 0, y = i * 3, w = 6, h = 3 }),
    };

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text, HttpResponseMessage Response)> CallAsync(
        CookieClient client, HttpMethod method, string url, object? body = null, string? ifMatch = null)
    {
        Dictionary<string, string> headers = new() { ["X-XSRF-TOKEN"] = "test-xsrf" };
        if (ifMatch is not null)
        {
            headers["If-Match"] = ifMatch;
        }

        HttpContent? content = body is null ? null : new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.SendAsync(method, url, content, headers);
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.StartsWith('{') || text.StartsWith('[') ? JsonElement.Parse(text) : default, text, response);
    }

    private async Task<(int Id, string ETag)> CreateAsync(CookieClient owner, string? name = null, object? layout = null)
    {
        var (status, body, text, response) = await CallAsync(owner, HttpMethod.Post, Views, new { name = name ?? Unique(), layout = layout ?? Layout() });
        Assert.True(status == HttpStatusCode.Created, text);
        return (body.GetProperty("id").GetInt32(), response.Headers.ETag!.Tag);
    }

    [Fact]
    public async Task The_owner_can_create_read_list_update_and_delete_a_view()
    {
        using CookieClient owner = Client(31);
        string name = Unique();

        (int id, string etag) = await CreateAsync(owner, name);
        var (readStatus, read, _, readResponse) = await CallAsync(owner, HttpMethod.Get, $"{Views}/{id}");
        var (_, list, _, _) = await CallAsync(owner, HttpMethod.Get, Views);
        var (updateStatus, updated, updateText, updateResponse) = await CallAsync(owner, HttpMethod.Put, $"{Views}/{id}", new { name = name + "-b", description = "d", layout = Layout(2) }, etag);
        var (deleteStatus, _, _, _) = await CallAsync(owner, HttpMethod.Delete, $"{Views}/{id}");
        var (afterStatus, _, _, _) = await CallAsync(owner, HttpMethod.Get, $"{Views}/{id}");

        Assert.Equal(HttpStatusCode.OK, readStatus);
        Assert.True(read.GetProperty("isOwner").GetBoolean());
        Assert.Equal("user31", read.GetProperty("ownerName").GetString());
        Assert.Equal(etag, readResponse.Headers.ETag!.Tag);
        Assert.Contains(list.GetProperty("mine").EnumerateArray(), v => v.GetProperty("id").GetInt32() == id);
        Assert.Equal(HttpStatusCode.OK, updateStatus);
        Assert.True(updated.ValueKind == JsonValueKind.Object, updateText);
        Assert.Equal(2, updated.GetProperty("layout").GetProperty("widgets").GetArrayLength());
        Assert.NotEqual(etag, updateResponse.Headers.ETag!.Tag);
        Assert.Equal(HttpStatusCode.NoContent, deleteStatus);
        Assert.Equal(HttpStatusCode.NotFound, afterStatus);
    }

    [Fact]
    public async Task Saving_needs_the_version_and_a_stale_or_made_up_one_changes_nothing()
    {
        using CookieClient owner = Client(31);
        (int id, string etag) = await CreateAsync(owner);
        var body = new { name = Unique(), layout = Layout() };

        var (missing, _, _, _) = await CallAsync(owner, HttpMethod.Put, $"{Views}/{id}", body);
        var (garbage, _, _, _) = await CallAsync(owner, HttpMethod.Put, $"{Views}/{id}", body, "\"not-a-number\"");
        var (first, _, _, _) = await CallAsync(owner, HttpMethod.Put, $"{Views}/{id}", body, etag);
        var (stale, _, _, _) = await CallAsync(owner, HttpMethod.Put, $"{Views}/{id}", new { name = Unique(), layout = Layout() }, etag);

        Assert.Equal(HttpStatusCode.PreconditionRequired, missing);
        Assert.Equal(HttpStatusCode.BadRequest, garbage);
        Assert.Equal(HttpStatusCode.OK, first);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale);
    }

    [Fact]
    public async Task A_duplicate_name_is_a_conflict()
    {
        using CookieClient owner = Client(31);
        string name = Unique();
        await CreateAsync(owner, name);

        var (status, _, _, _) = await CallAsync(owner, HttpMethod.Post, Views, new { name, layout = Layout() });

        Assert.Equal(HttpStatusCode.Conflict, status);
    }

    [Fact]
    public async Task Anonymous_and_unpermitted_callers_are_refused()
    {
        using CookieClient anonymous = WithXsrf(factory.NewClient());
        using CookieClient noDashboard = Client(31, Permissions.App);
        using CookieClient reader = Client(31, ReadOnlyPermissions);
        using CookieClient owner = Client(31);
        (int id, _) = await CreateAsync(owner);

        var (anonymousStatus, _, _, _) = await CallAsync(anonymous, HttpMethod.Get, Views);
        var (noDashboardStatus, _, _, _) = await CallAsync(noDashboard, HttpMethod.Get, Views);
        var (readerList, _, _, _) = await CallAsync(reader, HttpMethod.Get, Views);
        var (readerCreate, _, _, _) = await CallAsync(reader, HttpMethod.Post, Views, new { name = Unique(), layout = Layout() });
        var (readerDelete, _, _, _) = await CallAsync(reader, HttpMethod.Delete, $"{Views}/{id}");
        var (readerInvite, _, _, _) = await CallAsync(reader, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Anyone });
        var (readerShares, _, _, _) = await CallAsync(reader, HttpMethod.Get, $"{Views}/{id}/shares");

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousStatus);
        Assert.Equal(HttpStatusCode.Forbidden, noDashboardStatus);
        Assert.Equal(HttpStatusCode.OK, readerList);
        Assert.Equal(HttpStatusCode.Forbidden, readerCreate);
        Assert.Equal(HttpStatusCode.Forbidden, readerDelete);
        Assert.Equal(HttpStatusCode.Forbidden, readerInvite);
        Assert.Equal(HttpStatusCode.Forbidden, readerShares);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task A_user_in_another_provider_cannot_reach_provider_100s_views_or_create_in_it()
    {
        using CookieClient owner = Client(31);
        using CookieClient outsider = Client(33);
        (int id, _) = await CreateAsync(owner);

        foreach ((HttpMethod method, string url, object? body) in new (HttpMethod, string, object?)[]
        {
            (HttpMethod.Get, Views, null),
            (HttpMethod.Get, $"{Views}/{id}", null),
            (HttpMethod.Post, Views, new { name = Unique(), layout = Layout() }),
            (HttpMethod.Put, $"{Views}/{id}", new { name = Unique(), layout = Layout() }),
            (HttpMethod.Delete, $"{Views}/{id}", null),
            (HttpMethod.Get, $"{Views}/{id}/shares", null),
            (HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Anyone }),
        })
        {
            var (status, _, _, _) = await CallAsync(outsider, method, url, body, method == HttpMethod.Put ? "\"1\"" : null);
            Assert.True(status == HttpStatusCode.NotFound, $"{method} {url} answered {(int)status}");
        }

        var (ownerOnOther, _, _, _) = await CallAsync(owner, HttpMethod.Get, "/api/v1/providers/300/saved-views");
        Assert.Equal(HttpStatusCode.NotFound, ownerOnOther);
    }

    [Fact]
    public async Task A_facility_limited_user_is_treated_as_not_having_the_provider()
    {
        using CookieClient limited = Client(34);

        var (list, _, _, _) = await CallAsync(limited, HttpMethod.Get, Views);
        var (create, _, _, _) = await CallAsync(limited, HttpMethod.Post, Views, new { name = Unique(), layout = Layout() });

        Assert.Equal(HttpStatusCode.NotFound, list);
        Assert.Equal(HttpStatusCode.NotFound, create);
    }

    [Fact]
    public async Task Owner_and_provider_in_the_body_are_ignored_and_the_view_belongs_to_the_session_user()
    {
        using CookieClient owner = Client(31);
        using CookieClient other = Client(32);
        string json = $$"""{"name":"{{Unique()}}","ownerId":99,"ownerPortalUserId":99,"providerId":"300","isShared":true,"isOwner":false,"layout":{"version":1,"density":"compact","widgets":[]},"createdByUserName":"mallory"}""";

        var (status, body, _, _) = await CallAsync(owner, HttpMethod.Post, Views, json);

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.True(body.GetProperty("isOwner").GetBoolean());
        Assert.Equal("user31", body.GetProperty("ownerName").GetString());
        var (_, theirList, _, _) = await CallAsync(other, HttpMethod.Get, Views);
        Assert.DoesNotContain(theirList.GetProperty("mine").EnumerateArray(), v => v.GetProperty("id").GetInt32() == body.GetProperty("id").GetInt32());
        Assert.DoesNotContain(theirList.GetProperty("shared").EnumerateArray(), v => v.GetProperty("id").GetInt32() == body.GetProperty("id").GetInt32());
        var (otherProvider, _, _, _) = await CallAsync(Client(33), HttpMethod.Get, "/api/v1/providers/300/saved-views");
        Assert.Equal(HttpStatusCode.OK, otherProvider);
    }

    public static TheoryData<string, string> BadBodies => new()
    {
        { "too many widgets", """{"name":"n","layout":{"version":1,"widgets":[%MANY%]}}""" },
        { "unknown metric", """{"name":"n","layout":{"version":1,"widgets":[{"id":"a","metricKey":"nope","presentation":"line","x":0,"y":0,"w":6,"h":3}]}}""" },
        { "planned metric", """{"name":"n","layout":{"version":1,"widgets":[{"id":"a","metricKey":"denied_claims","presentation":"table","x":0,"y":0,"w":6,"h":3}]}}""" },
        { "overlap", """{"name":"n","layout":{"version":1,"widgets":[{"id":"a","metricKey":"net_collections","presentation":"line","x":0,"y":0,"w":6,"h":3},{"id":"b","metricKey":"net_collections","presentation":"line","x":3,"y":0,"w":6,"h":3}]}}""" },
        { "outside the grid", """{"name":"n","layout":{"version":1,"widgets":[{"id":"a","metricKey":"net_collections","presentation":"line","x":7,"y":0,"w":6,"h":3}]}}""" },
        { "overflowing coordinate", """{"name":"n","layout":{"version":1,"widgets":[{"id":"a","metricKey":"net_collections","presentation":"line","x":2147483647,"y":0,"w":6,"h":3}]}}""" },
        { "empty name", """{"name":"   ","layout":{"version":1,"widgets":[]}}""" },
        { "no layout", """{"name":"n"}""" },
        { "long name", "{\"name\":\"" + new string('a', 81) + "\",\"layout\":{\"version\":1,\"widgets\":[]}}" },
        { "control character in name", "{\"name\":\"a\\u0000b\",\"layout\":{\"version\":1,\"widgets\":[]}}" },
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task A_body_that_breaks_a_bound_is_refused_with_codes_and_nothing_is_saved(string label, string json)
    {
        using CookieClient owner = Client(31);
        string many = string.Join(",", Enumerable.Range(0, SavedViewLimits.MaxWidgets + 1).Select(i => $$"""{"id":"w{{i}}","metricKey":"clean_claim_rate","presentation":"tile","x":0,"y":0,"w":3,"h":2}"""));
        string body = json.Replace("%MANY%", many, StringComparison.Ordinal);
        var (_, before, _, _) = await CallAsync(owner, HttpMethod.Get, Views);

        var (status, _, text, _) = await CallAsync(owner, HttpMethod.Post, Views, body);

        Assert.True(status == HttpStatusCode.BadRequest, $"{label} answered {(int)status}: {text}");
        var (_, after, _, _) = await CallAsync(owner, HttpMethod.Get, Views);
        Assert.Equal(before.GetProperty("mine").GetArrayLength(), after.GetProperty("mine").GetArrayLength());
    }

    [Fact]
    public async Task A_body_larger_than_the_cap_is_refused_before_it_is_read()
    {
        using CookieClient owner = Client(31);
        string huge = "{\"name\":\"n\",\"layout\":{\"version\":1,\"widgets\":[]},\"padding\":\"" + new string('x', SavedViewLimits.MaxRequestBodyBytes + 1024) + "\"}";

        var (status, _, _, _) = await CallAsync(owner, HttpMethod.Post, Views, huge);

        Assert.True(status is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, $"answered {(int)status}");
    }

    [Fact]
    public async Task A_body_that_declares_no_length_is_still_cut_off_at_the_cap()
    {
        using CookieClient owner = Client(31);
        byte[] payload = Encoding.UTF8.GetBytes("{\"name\":\"n\",\"layout\":{\"version\":1,\"widgets\":[]},\"padding\":\"" + new string('x', SavedViewLimits.MaxRequestBodyBytes + 1024) + "\"}");
        using var content = new StreamContent(new UnknownLengthStream(payload));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

        using HttpResponseMessage response = await owner.SendAsync(HttpMethod.Post, Views, content, new Dictionary<string, string> { ["X-XSRF-TOKEN"] = "test-xsrf" });

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, $"answered {(int)response.StatusCode}");
    }

    [Fact]
    public async Task A_refused_save_never_echoes_or_logs_what_was_sent()
    {
        using CookieClient owner = Client(31);
        string json = $$$"""{"name":"{{{Marker}}}","description":"{{{Marker}}}","layout":{"version":1,"density":"{{{Marker}}}","widgets":[{"id":"{{{Marker}}}","metricKey":"{{{Marker}}}","presentation":"{{{Marker}}}","options":{"{{{Marker}}}":"{{{Marker}}}"},"x":0,"y":0,"w":6,"h":3}]}}""";

        var (status, _, text, _) = await CallAsync(owner, HttpMethod.Post, Views, json);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.DoesNotContain("MARKER", text, StringComparison.Ordinal);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains("MARKER", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_whole_invitation_journey_works_and_each_step_is_scoped()
    {
        using CookieClient owner = Client(31);
        using CookieClient guest = Client(32);
        (int id, _) = await CreateAsync(owner, Unique());
        sender.Sent.Clear();

        var (inviteStatus, invite, inviteText, _) = await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Padded });
        InvitationEmail email = Assert.Single(sender.Sent);
        string token = email.Link[(email.Link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

        Assert.Equal(HttpStatusCode.OK, inviteStatus);
        Assert.Equal("guest@example.org", invite.GetProperty("sent")[0].GetProperty("email").GetString());
        Assert.DoesNotContain(token, inviteText, StringComparison.Ordinal);
        Assert.StartsWith("https://prism.example.test/saved-view-invitations/redeem?token=", email.Link, StringComparison.Ordinal);
        Assert.Equal("guest@example.org", email.ToAddress);
        Assert.Equal("user31", email.SenderName);
        Assert.DoesNotContain("net_collections", email.Link, StringComparison.Ordinal);

        var (before, _, _, _) = await CallAsync(guest, HttpMethod.Get, $"{Views}/{id}");
        var (redeemStatus, redeemed, _, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });
        var (after, view, _, _) = await CallAsync(guest, HttpMethod.Get, $"{Views}/{id}");
        var (replay, _, _, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });
        var (guestEdit, _, _, _) = await CallAsync(guest, HttpMethod.Put, $"{Views}/{id}", new { name = Unique(), layout = Layout() }, "\"1\"");
        var (guestDelete, _, _, _) = await CallAsync(guest, HttpMethod.Delete, $"{Views}/{id}");
        var (guestShares, _, _, _) = await CallAsync(guest, HttpMethod.Get, $"{Views}/{id}/shares");

        Assert.Equal(HttpStatusCode.NotFound, before);
        Assert.Equal(HttpStatusCode.OK, redeemStatus);
        Assert.Equal(("100", id), (redeemed.GetProperty("providerId").GetString(), redeemed.GetProperty("viewId").GetInt32()));
        Assert.Equal(HttpStatusCode.OK, after);
        Assert.False(view.GetProperty("isOwner").GetBoolean());
        Assert.Equal(HttpStatusCode.NotFound, replay);
        Assert.Equal(HttpStatusCode.NotFound, guestEdit);
        Assert.Equal(HttpStatusCode.NotFound, guestDelete);
        Assert.Equal(HttpStatusCode.NotFound, guestShares);
        var (_, guestList, _, _) = await CallAsync(guest, HttpMethod.Get, Views);
        Assert.Contains(guestList.GetProperty("shared").EnumerateArray(), v => v.GetProperty("id").GetInt32() == id);

        var (_, sharing, _, _) = await CallAsync(owner, HttpMethod.Get, $"{Views}/{id}/shares");
        JsonElement recipient = sharing.GetProperty("recipients")[0];
        Assert.Equal("guest@example.org", recipient.GetProperty("email").GetString());
        var (revoke, _, _, _) = await CallAsync(owner, HttpMethod.Delete, $"{Views}/{id}/shares/{recipient.GetProperty("shareId").GetInt32()}");
        var (revoked, _, _, _) = await CallAsync(guest, HttpMethod.Get, $"{Views}/{id}");
        Assert.Equal(HttpStatusCode.NoContent, revoke);
        Assert.Equal(HttpStatusCode.NotFound, revoked);

        await Task.Delay(200, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(factory.Logs.Entries, entry => entry.Text.Contains(token, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_user_outside_the_provider_cannot_use_a_link_and_does_not_burn_it()
    {
        using CookieClient owner = Client(31);
        using CookieClient outsider = Client(33);
        using CookieClient guest = Client(32);
        (int id, _) = await CreateAsync(owner);
        sender.Sent.Clear();
        await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Guest });
        string link = sender.Sent.Single().Link;
        string token = link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];

        var (outsiderStatus, _, _, _) = await CallAsync(outsider, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });
        var (guestStatus, _, _, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });

        Assert.Equal(HttpStatusCode.NotFound, outsiderStatus);
        Assert.Equal(HttpStatusCode.OK, guestStatus);
    }

    [Fact]
    public async Task Every_way_a_link_can_fail_looks_the_same_and_ids_in_the_body_are_ignored()
    {
        using CookieClient owner = Client(31);
        using CookieClient guest = Client(32);
        (int id, _) = await CreateAsync(owner);
        sender.Sent.Clear();
        await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Guest });
        string link = sender.Sent.Single().Link;
        string token = link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];
        string tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        List<(HttpStatusCode Status, string Text)> failures = [];
        foreach (object? body in new object?[] { null, new { }, new { token = "" }, new { token = "short" }, new { token = tampered }, new { token = new string('A', 43) }, new { token = token + "x" }, new { token = tampered, viewId = id, providerId = "100" } })
        {
            var (status, _, text, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", body ?? "null");
            failures.Add((status, text));
        }

        Assert.All(failures, f => Assert.Equal(HttpStatusCode.NotFound, f.Status));
        Assert.Single(failures.Select(f => f.Text).Distinct(StringComparer.Ordinal));
        var (stillWorks, _, _, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });
        Assert.Equal(HttpStatusCode.OK, stillWorks);
    }

    [Fact]
    public async Task Bad_invitation_requests_are_refused_and_nothing_is_sent()
    {
        using CookieClient owner = Client(31);
        using CookieClient guest = Client(32);
        (int id, _) = await CreateAsync(owner);
        sender.Sent.Clear();

        string[][] bad =
        [
            [],
            ["not-an-address"],
            ["ok@example.org", "bad address@example.org"],
            ["a@example.org\r\nBcc: x@example.org"],
            [.. Enumerable.Range(0, SavedViewLimits.MaxEmailsPerRequest + 1).Select(i => $"p{i}@example.org")],
        ];
        foreach (string[] emails in bad)
        {
            var (status, _, _, _) = await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails });
            Assert.Equal(HttpStatusCode.BadRequest, status);
        }

        var (noBody, _, _, _) = await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", "null");
        var (notOwner, _, _, _) = await CallAsync(guest, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Anyone });
        Assert.Equal(HttpStatusCode.BadRequest, noBody);
        Assert.Equal(HttpStatusCode.NotFound, notOwner);
        Assert.Empty(sender.Sent);
    }

    [Fact]
    public async Task When_the_email_cannot_be_sent_the_owner_is_told_and_no_live_link_is_left()
    {
        using CookieClient owner = Client(31);
        (int id, _) = await CreateAsync(owner);
        sender.Fail = true;
        try
        {
            var (status, _, _, _) = await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Guest });
            var (_, sharing, _, _) = await CallAsync(owner, HttpMethod.Get, $"{Views}/{id}/shares");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
            Assert.Equal(0, sharing.GetProperty("pending").GetArrayLength());
        }
        finally
        {
            sender.Fail = false;
        }
    }

    [Fact]
    public async Task Pending_invitations_can_be_cancelled_and_the_link_stops_working()
    {
        using CookieClient owner = Client(31);
        using CookieClient guest = Client(32);
        (int id, _) = await CreateAsync(owner);
        sender.Sent.Clear();
        await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Guest });
        string link = sender.Sent.Single().Link;
        string token = link[(link.IndexOf("token=", StringComparison.Ordinal) + 6)..];
        var (_, sharing, _, _) = await CallAsync(owner, HttpMethod.Get, $"{Views}/{id}/shares");
        int invitationId = sharing.GetProperty("pending")[0].GetProperty("invitationId").GetInt32();

        var (cancel, _, _, _) = await CallAsync(owner, HttpMethod.Delete, $"{Views}/{id}/invitations/{invitationId}");
        var (redeem, _, _, _) = await CallAsync(guest, HttpMethod.Post, "/api/v1/saved-view-invitations/redeem", new { token });

        Assert.Equal(HttpStatusCode.NoContent, cancel);
        Assert.Equal(HttpStatusCode.NotFound, redeem);
    }

    [Fact]
    public async Task Invitations_report_unavailable_on_a_host_that_has_not_turned_them_on()
    {
        using var disabled = new AuthTestFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = factory.Services.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>()["ConnectionStrings:Prism"] });
        disabled.Directory.Providers.Clear();
        disabled.Directory.Providers.Add(new ProviderScope("100", "Mercy Air", []));
        using CookieClient owner = WithXsrf(disabled.NewSignedInClientAs(31, "user31", AllPermissions));
        (int id, _) = await CreateAsync(owner);

        var (status, _, _, _) = await CallAsync(owner, HttpMethod.Post, $"{Views}/{id}/invitations", new { emails = Anyone });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
    }

    /// <summary>A readable stream that hides its length, so the request goes out chunked with no Content-Length.</summary>
    private sealed class UnknownLengthStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class RecordingSender : IInvitationEmailSender
    {
        public List<InvitationEmail> Sent { get; } = [];

        public bool Fail { get; set; }

        public Task SendAsync(InvitationEmail email, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new InvitationEmailUnavailableException();
            }

            Sent.Add(email);
            return Task.CompletedTask;
        }
    }

    private sealed class Divisions : IProviderDivisionDirectory
    {
        public Task<IReadOnlySet<string>> GetFacilityIdsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<string>>(portalCompanyId == 100 ? new HashSet<string> { "9", "10" } : []);

        public Task<IReadOnlyList<ReportingDivision>> GetReportingDivisionsAsync(int portalCompanyId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReportingDivision>>([]);
    }
}
