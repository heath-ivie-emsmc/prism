using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Prism.Application.Auth;
using Prism.Infrastructure.Persistence;
using Prism.Tests.Integration.Auth;
using Prism.Tests.Integration.Persistence;

namespace Prism.Tests.Integration.Admin;

/// <summary>
/// Personas, the first-login questionnaire and the home page through the real host and real PostgreSQL.
/// Customers: 41 and 42 are in Provider 100, and 43 is only in Provider 300.
/// </summary>
public sealed class PersonaOnboardingTests : IClassFixture<PostgresFixture>, IAsyncLifetime, IDisposable
{
    private const string Group = AuthTestFactory.InternalGroupName;
    private const string Marker = "PHI-MARKER-Jane-Doe-1970-01-01";
    private const string Admin = "/api/v1/admin";
    private const string Me = "/api/v1/me";
    private const string Home = "/api/v1/providers/100/home";
    private static readonly string[] Both = [Permissions.App, Permissions.AdminTemplatesAuthor, Permissions.AdminTemplatesPublish];
    private static readonly string[] CustomerPermissions = [Permissions.App, Permissions.DashboardView, Permissions.ViewsManage];

    private readonly AuthTestFactory factory;
    private int counter;

    public PersonaOnboardingTests(PostgresFixture postgres)
    {
        factory = new AuthTestFactory(settings: new Dictionary<string, string?> { ["ConnectionStrings:Prism"] = postgres.ConnectionString });
        factory.Directory.ProvidersByUser = user => user == 43
            ? [new ProviderScope("300", "Elsewhere", [])]
            : [new ProviderScope("100", "Mercy Air", [])];
    }

    public async ValueTask InitializeAsync()
    {
        // The questionnaire is one global thing, so every test starts from none. The admin audit trail is append-only and stays.
        using IServiceScope scope = factory.Services.CreateScope();
        PrismDbContext db = scope.ServiceProvider.GetRequiredService<PrismDbContext>();
        CancellationToken ct = TestContext.Current.CancellationToken;
        await db.UserHomeViews.ExecuteDeleteAsync(ct);
        await db.SavedViews.Where(v => v.CreatedByUserName != null && v.Name.StartsWith("Home")).ExecuteDeleteAsync(ct);
        await db.UserProfiles.ExecuteDeleteAsync(ct);
        await db.OnboardingOptionWeights.ExecuteDeleteAsync(ct);
        await db.OnboardingOptions.ExecuteDeleteAsync(ct);
        await db.OnboardingQuestions.ExecuteDeleteAsync(ct);
        await db.Personas.ExecuteDeleteAsync(ct);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public void Dispose() => factory.Dispose();

    private string Unique() => $"p-{Interlocked.Increment(ref counter)}-{Guid.NewGuid():N}"[..24];

    private static CookieClient WithXsrf(CookieClient client)
    {
        client.Set("XSRF-TOKEN", "test-xsrf");
        return client;
    }

    private CookieClient Staff(params string[] permissions) => WithXsrf(factory.NewSignedInClientInGroups([Group], permissions.Length == 0 ? Both : permissions));

    private CookieClient Customer(long user) => WithXsrf(factory.NewSignedInClientAs(user, $"user{user}", CustomerPermissions));

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Text)> CallAsync(CookieClient client, HttpMethod method, string url, object? body = null)
    {
        HttpContent? content = body is null ? null : new StringContent(body as string ?? JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.SendAsync(method, url, content, new Dictionary<string, string> { ["X-XSRF-TOKEN"] = "test-xsrf" });
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        return (response.StatusCode, text.StartsWith('{') || text.StartsWith('[') ? JsonElement.Parse(text) : default, text);
    }

    private async Task<int> TemplateAsync(CookieClient staff, int widgets, bool publish = true)
    {
        var (status, body, text) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/view-templates", new
        {
            name = Unique(),
            layout = new
            {
                version = 1,
                density = "comfortable",
                widgets = Enumerable.Range(0, widgets).Select(i => new { id = $"w{i}", metricKey = "net_collections", presentation = "line", x = 0, y = i * 3, w = 6, h = 3 }),
            },
        });
        Assert.True(status == HttpStatusCode.Created, text);
        int id = body.GetProperty("id").GetInt32();
        if (publish)
        {
            Assert.Equal(HttpStatusCode.OK, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/view-templates/{id}/publish")).Status);
        }

        return id;
    }

    private async Task<int> PersonaAsync(CookieClient staff, int templateId, int sortOrder, bool publish = true)
    {
        var (status, body, text) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas", new { name = Unique(), viewTemplateId = templateId, sortOrder });
        Assert.True(status == HttpStatusCode.Created, text);
        int id = body.GetProperty("id").GetInt32();
        if (publish)
        {
            Assert.Equal(HttpStatusCode.OK, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas/{id}/publish")).Status);
        }

        return id;
    }

    /// <summary>A published question whose options each weigh one Persona by 10. Returns the option ids in the order given.</summary>
    private Task<(int QuestionId, int[] OptionIds)> QuestionAsync(CookieClient staff, int sortOrder, params int[] personaPerOption) =>
        QuestionAsync(staff, sortOrder, allowsMultiple: false, personaPerOption);

    private async Task<(int QuestionId, int[] OptionIds)> QuestionAsync(CookieClient staff, int sortOrder, bool allowsMultiple, params int[] personaPerOption)
    {
        var (status, body, text) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new
        {
            prompt = "Question " + Unique(),
            sortOrder,
            allowsMultiple,
            options = personaPerOption.Select((personaId, i) => new { label = $"Option {i}", sortOrder = i, weights = new[] { new { personaId, weight = 10 } } }),
        });
        Assert.True(status == HttpStatusCode.Created, text);
        int id = body.GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions/{id}/publish")).Status);
        return (id, [.. body.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt32())]);
    }

    /// <summary>Two Personas (one widget and three widgets) and two questions whose first option leans to the first Persona.</summary>
    private async Task<(int Small, int Big, (int Question, int[] Options) Q1, (int Question, int[] Options) Q2)> ConfigureAsync(CookieClient staff)
    {
        int small = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        int big = await PersonaAsync(staff, await TemplateAsync(staff, 3), 2);
        return (small, big, await QuestionAsync(staff, 1, small, big), await QuestionAsync(staff, 2, small, big));
    }

    private static async Task<bool> OnboardingRequiredAsync(CookieClient customer)
    {
        var (status, body, text) = await CallAsync(customer, HttpMethod.Get, Me);
        Assert.True(status == HttpStatusCode.OK, text);
        return body.GetProperty("onboardingRequired").GetBoolean();
    }

    private static Task<(HttpStatusCode Status, JsonElement Body, string Text)> AnswerAsync(CookieClient customer, params (int Question, int Option)[] answers) =>
        CallAsync(customer, HttpMethod.Post, $"{Me}/onboarding", new { answers = answers.Select(a => new { questionId = a.Question, optionId = a.Option }) });

    [Theory]
    [InlineData("GET", "/personas")]
    [InlineData("POST", "/personas")]
    [InlineData("GET", "/onboarding-questions")]
    [InlineData("POST", "/onboarding-questions")]
    [InlineData("POST", "/personas/1/publish")]
    [InlineData("POST", "/onboarding-questions/1/publish")]
    public async Task Admin_routes_refuse_a_customer_a_tier_without_the_group_and_the_group_without_a_tier(string method, string path)
    {
        var request = new HttpMethod(method);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(Customer(41), request, Admin + path, "{}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(WithXsrf(factory.NewSignedInClient(Both)), request, Admin + path, "{}")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(Staff(Permissions.App), request, Admin + path, "{}")).Status);
    }

    [Fact]
    public async Task Publishing_needs_the_publisher_tier_and_authoring_alone_cannot_publish()
    {
        CookieClient staff = Staff();
        int templateId = await TemplateAsync(staff, 1);
        int personaId = await PersonaAsync(staff, templateId, 1, publish: false);

        CookieClient author = Staff(Permissions.App, Permissions.AdminTemplatesAuthor);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(author, HttpMethod.Post, $"{Admin}/personas/{personaId}/publish")).Status);
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas/{personaId}/publish")).Status);
    }

    [Fact]
    public async Task A_persona_needs_an_existing_template_and_a_published_one_to_publish()
    {
        CookieClient staff = Staff();

        var (missing, _, _) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas", new { name = Unique(), viewTemplateId = 999_999, sortOrder = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, missing);

        int draft = await TemplateAsync(staff, 1, publish: false);
        int personaId = await PersonaAsync(staff, draft, 1, publish: false);
        var (notPublished, _, _) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas/{personaId}/publish");
        Assert.Equal(HttpStatusCode.Conflict, notPublished);
    }

    [Fact]
    public async Task Persona_names_are_unique_and_a_persona_in_use_cannot_be_deleted()
    {
        CookieClient staff = Staff();
        int templateId = await TemplateAsync(staff, 1);
        string name = Unique();
        Assert.Equal(HttpStatusCode.Created, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas", new { name, viewTemplateId = templateId, sortOrder = 1 })).Status);
        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas", new { name, viewTemplateId = templateId, sortOrder = 2 })).Status);

        int used = await PersonaAsync(staff, templateId, 3);
        await QuestionAsync(staff, 1, used, used);
        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(staff, HttpMethod.Delete, $"{Admin}/personas/{used}")).Status);
    }

    [Fact]
    public async Task Questions_are_bounded_and_only_three_can_be_published()
    {
        CookieClient staff = Staff();
        int persona = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        var option = new { label = "A", sortOrder = 0, weights = new[] { new { personaId = persona, weight = 10 } } };

        var oneOption = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new { prompt = "Q", sortOrder = 1, options = new[] { option } });
        var heavy = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new
        {
            prompt = "Q",
            sortOrder = 1,
            options = new[] { option, new { label = "B", sortOrder = 1, weights = new[] { new { personaId = persona, weight = 101 } } } },
        });
        var unknown = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new
        {
            prompt = "Q",
            sortOrder = 1,
            options = new[] { option, new { label = "B", sortOrder = 1, weights = new[] { new { personaId = 999_999, weight = 1 } } } },
        });
        var longPrompt = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new { prompt = new string('x', 201), sortOrder = 1, options = new[] { option, option } });
        var tooMany = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new { prompt = "Q", sortOrder = 1, options = Enumerable.Repeat(option, 9) });

        Assert.All([oneOption, heavy, unknown, longPrompt, tooMany], r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));

        for (int i = 0; i < 3; i++)
        {
            await QuestionAsync(staff, i, persona, persona);
        }

        var (status, body, text) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions", new { prompt = "Fourth", sortOrder = 9, options = new[] { option, option } });
        Assert.Equal(HttpStatusCode.Created, status);
        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(staff, HttpMethod.Post, $"{Admin}/onboarding-questions/{body.GetProperty("id").GetInt32()}/publish")).Status);
        Assert.DoesNotContain("Fourth", (await CallAsync(factory.NewSignedInClientAs(41, "u41", CustomerPermissions), HttpMethod.Get, $"{Me}/onboarding")).Text, StringComparison.Ordinal);
        Assert.NotEmpty(text);
    }

    [Fact]
    public async Task A_published_body_flag_is_ignored_on_create()
    {
        CookieClient staff = Staff();
        int templateId = await TemplateAsync(staff, 1, publish: false);
        var (status, body, _) = await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas", """{"name":"Flagged","viewTemplateId":%T,"sortOrder":1,"isPublished":true}""".Replace("%T", templateId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

        Assert.Equal(HttpStatusCode.Created, status);
        Assert.False(body.GetProperty("isPublished").GetBoolean());
    }

    [Fact]
    public async Task Nothing_published_fails_open_to_the_default_home_page()
    {
        CookieClient customer = Customer(41);

        Assert.False(await OnboardingRequiredAsync(customer));
        var (status, body, _) = await CallAsync(customer, HttpMethod.Get, $"{Me}/onboarding");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.GetProperty("questions").GetArrayLength());
        Assert.Equal(HttpStatusCode.Conflict, (await AnswerAsync(customer, (1, 1))).Status);
        Assert.Equal("default", (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task One_published_question_is_not_a_questionnaire()
    {
        CookieClient staff = Staff();
        int persona = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        await QuestionAsync(staff, 1, persona, persona);

        Assert.False(await OnboardingRequiredAsync(Customer(41)));
    }

    [Fact]
    public async Task A_new_user_answers_and_the_questions_never_show_a_weight()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);

        Assert.True(await OnboardingRequiredAsync(customer));
        var (status, body, text) = await CallAsync(customer, HttpMethod.Get, $"{Me}/onboarding");
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.GetProperty("questions").GetArrayLength());
        Assert.DoesNotContain("weight", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("persona", text, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(HttpStatusCode.NoContent, (await AnswerAsync(customer, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[0]))).Status);
        Assert.False(await OnboardingRequiredAsync(customer));
        Assert.True(await OnboardingRequiredAsync(Customer(42)));
    }

    [Fact]
    public async Task The_highest_total_picks_the_home_page_and_a_tie_goes_to_the_lower_sort_order()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);

        CookieClient leansBig = Customer(41);
        await AnswerAsync(leansBig, (config.Q1.Question, config.Q1.Options[1]), (config.Q2.Question, config.Q2.Options[1]));
        Assert.Equal(3, (await CallAsync(leansBig, HttpMethod.Get, Home)).Body.GetProperty("layout").GetProperty("widgets").GetArrayLength());

        CookieClient tied = Customer(42);
        await AnswerAsync(tied, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[1]));
        Assert.Equal(1, (await CallAsync(tied, HttpMethod.Get, Home)).Body.GetProperty("layout").GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_choose_all_that_apply_question_is_served_as_multiple_and_its_chosen_weights_are_added_up()
    {
        CookieClient staff = Staff();
        int small = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        int big = await PersonaAsync(staff, await TemplateAsync(staff, 3), 2);
        // Lines of service: each option leans to a Persona. One option for the small Persona, three for the big one.
        var lines = await QuestionAsync(staff, 1, allowsMultiple: true, small, big, big, big);
        var role = await QuestionAsync(staff, 2, small, big);
        CookieClient customer = Customer(41);

        var (_, served, text) = await CallAsync(customer, HttpMethod.Get, $"{Me}/onboarding");
        JsonElement[] questions = [.. served.GetProperty("questions").EnumerateArray()];
        Assert.True(questions.Single(q => q.GetProperty("id").GetInt32() == lines.QuestionId).GetProperty("allowsMultiple").GetBoolean());
        Assert.False(questions.Single(q => q.GetProperty("id").GetInt32() == role.QuestionId).GetProperty("allowsMultiple").GetBoolean());
        Assert.DoesNotContain("weight", text, StringComparison.OrdinalIgnoreCase);

        // Small leads on the first option (10 + 10 for the role) but three options for big add up to 30 + 0.
        (HttpStatusCode status, _, _) = await AnswerAsync(customer, (lines.QuestionId, lines.OptionIds[1]), (lines.QuestionId, lines.OptionIds[2]), (lines.QuestionId, lines.OptionIds[3]), (role.QuestionId, role.OptionIds[0]));

        Assert.Equal(HttpStatusCode.NoContent, status);
        Assert.Equal(3, (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("layout").GetProperty("widgets").GetArrayLength());
    }

    [Fact]
    public async Task A_multiple_choice_question_needs_at_least_one_answer_and_refuses_the_same_option_twice_or_one_from_another_question()
    {
        CookieClient staff = Staff();
        int small = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        int big = await PersonaAsync(staff, await TemplateAsync(staff, 3), 2);
        var lines = await QuestionAsync(staff, 1, allowsMultiple: true, small, big, big);
        var role = await QuestionAsync(staff, 2, small, big);
        CookieClient customer = Customer(41);

        (HttpStatusCode Status, JsonElement Body, string Text)[] results =
        [
            await AnswerAsync(customer, (role.QuestionId, role.OptionIds[0])),
            await AnswerAsync(customer, (lines.QuestionId, lines.OptionIds[0]), (lines.QuestionId, lines.OptionIds[0]), (role.QuestionId, role.OptionIds[0])),
            await AnswerAsync(customer, (lines.QuestionId, role.OptionIds[0]), (role.QuestionId, role.OptionIds[0])),
            await AnswerAsync(customer, (lines.QuestionId, lines.OptionIds[0]), (role.QuestionId, role.OptionIds[0]), (role.QuestionId, role.OptionIds[1])),
        ];

        Assert.All(results, r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.True(await OnboardingRequiredAsync(customer));
    }

    [Fact]
    public async Task The_multiple_choice_flag_can_be_changed_when_a_question_is_updated()
    {
        CookieClient staff = Staff();
        int persona = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        var question = await QuestionAsync(staff, 1, persona, persona);

        var (status, body, _) = await CallAsync(staff, HttpMethod.Put, $"{Admin}/onboarding-questions/{question.QuestionId}", new
        {
            prompt = "Lines of service",
            sortOrder = 1,
            allowsMultiple = true,
            options = new[] { new { label = "Air", sortOrder = 1, weights = new[] { new { personaId = persona, weight = 5 } } }, new { label = "Ground", sortOrder = 2, weights = new[] { new { personaId = persona, weight = 5 } } } },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.GetProperty("allowsMultiple").GetBoolean());
    }

    [Fact]
    public async Task Bad_submissions_are_refused_and_store_nothing()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);
        int q1 = config.Q1.Question;
        int q2 = config.Q2.Question;

        (HttpStatusCode Status, JsonElement Body, string Text)[] results =
        [
            await AnswerAsync(customer, (q1, config.Q1.Options[0])),
            await AnswerAsync(customer, (q1, config.Q1.Options[0]), (q1, config.Q1.Options[1])),
            await AnswerAsync(customer, (q1, config.Q1.Options[0]), (q2, config.Q1.Options[0])),
            await AnswerAsync(customer, (q1, 999_999), (q2, config.Q2.Options[0])),
            await AnswerAsync(customer, (q1, config.Q1.Options[0]), (q2, config.Q2.Options[0]), (q2, config.Q2.Options[1])),
            await CallAsync(customer, HttpMethod.Post, $"{Me}/onboarding", """{"answers":[]}"""),
            await CallAsync(customer, HttpMethod.Post, $"{Me}/onboarding", "{}"),
        ];

        Assert.All(results, r => Assert.Equal(HttpStatusCode.BadRequest, r.Status));
        Assert.True(await OnboardingRequiredAsync(customer));
    }

    [Fact]
    public async Task An_oversize_body_is_refused()
    {
        CookieClient staff = Staff();
        await ConfigureAsync(staff);

        var (status, _, _) = await CallAsync(Customer(41), HttpMethod.Post, $"{Me}/onboarding", $$"""{"answers":[],"pad":"{{new string('x', 40_000)}}"}""");

        Assert.True(status is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, status.ToString());
    }

    [Fact]
    public async Task A_persona_or_completed_value_sent_by_the_client_is_ignored_and_nothing_raw_is_stored_or_logged()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);

        var (status, _, _) = await CallAsync(customer, HttpMethod.Post, $"{Me}/onboarding", $$"""
            {"answers":[{"questionId":{{config.Q1.Question}},"optionId":{{config.Q1.Options[0]}},"note":"{{Marker}}"},{"questionId":{{config.Q2.Question}},"optionId":{{config.Q2.Options[0]}}}],
             "personaId":{{config.Big}},"onboardingRequired":false,"onboardingCompleted":true,"portalUserId":42,"comment":"{{Marker}}"}
            """);

        Assert.Equal(HttpStatusCode.NoContent, status);
        // The answers chose the first Persona, whatever the body claimed.
        Assert.Equal(1, (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("layout").GetProperty("widgets").GetArrayLength());
        // The named user (42) is untouched.
        Assert.True(await OnboardingRequiredAsync(Customer(42)));

        using IServiceScope scope = factory.Services.CreateScope();
        PrismDbContext db = scope.ServiceProvider.GetRequiredService<PrismDbContext>();
        Assert.Equal(1, await db.UserProfiles.CountAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(factory.Logs.Entries, e => e.Text.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unpublished_persona_or_template_sends_the_user_back_to_the_default_home_page()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);
        await AnswerAsync(customer, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[0]));
        Assert.Equal("persona", (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("source").GetString());

        await CallAsync(staff, HttpMethod.Post, $"{Admin}/personas/{config.Small}/unpublish");

        Assert.Equal("default", (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Customizing_copies_the_home_page_and_reset_returns_to_the_live_one()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);
        await AnswerAsync(customer, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[0]));

        var (status, body, text) = await CallAsync(customer, HttpMethod.Post, $"{Home}/customize");
        Assert.True(status == HttpStatusCode.OK, text);
        int viewId = body.GetProperty("savedViewId").GetInt32();
        var (_, home, _) = await CallAsync(customer, HttpMethod.Get, Home);
        Assert.Equal("custom", home.GetProperty("source").GetString());
        Assert.Equal(viewId, home.GetProperty("savedViewId").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await CallAsync(customer, HttpMethod.Get, $"/api/v1/providers/100/saved-views/{viewId}")).Status);

        // Asking again returns the same copy.
        Assert.Equal(viewId, (await CallAsync(customer, HttpMethod.Post, $"{Home}/customize")).Body.GetProperty("savedViewId").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await CallAsync(customer, HttpMethod.Delete, $"{Home}/customize")).Status);
        Assert.Equal("persona", (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(customer, HttpMethod.Get, $"/api/v1/providers/100/saved-views/{viewId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(customer, HttpMethod.Delete, $"{Home}/customize")).Status);
    }

    [Fact]
    public async Task Deleting_the_copy_as_a_saved_view_is_a_reset()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);
        await AnswerAsync(customer, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[0]));
        int viewId = (await CallAsync(customer, HttpMethod.Post, $"{Home}/customize")).Body.GetProperty("savedViewId").GetInt32();

        Assert.Equal(HttpStatusCode.NoContent, (await CallAsync(customer, HttpMethod.Delete, $"/api/v1/providers/100/saved-views/{viewId}")).Status);

        Assert.Equal("persona", (await CallAsync(customer, HttpMethod.Get, Home)).Body.GetProperty("source").GetString());
    }

    [Fact]
    public async Task Customizing_without_a_persona_is_refused()
    {
        Assert.Equal(HttpStatusCode.Conflict, (await CallAsync(Customer(41), HttpMethod.Post, $"{Home}/customize")).Status);
    }

    [Fact]
    public async Task The_home_page_is_scoped_to_the_users_own_provider_and_copies_are_private()
    {
        CookieClient staff = Staff();
        var config = await ConfigureAsync(staff);
        CookieClient customer = Customer(41);
        await AnswerAsync(customer, (config.Q1.Question, config.Q1.Options[0]), (config.Q2.Question, config.Q2.Options[0]));
        int viewId = (await CallAsync(customer, HttpMethod.Post, $"{Home}/customize")).Body.GetProperty("savedViewId").GetInt32();

        // A user in Provider 300 only cannot ask for Provider 100's home page, nor open the first user's copy.
        CookieClient elsewhere = Customer(43);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(elsewhere, HttpMethod.Get, Home)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(elsewhere, HttpMethod.Post, $"{Home}/customize")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(elsewhere, HttpMethod.Delete, $"{Home}/customize")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(elsewhere, HttpMethod.Get, $"/api/v1/providers/100/saved-views/{viewId}")).Status);

        // A user in the same Provider keeps their own state: user 42 has no copy and no persona.
        Assert.Equal("default", (await CallAsync(Customer(42), HttpMethod.Get, Home)).Body.GetProperty("source").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(Customer(42), HttpMethod.Delete, $"{Home}/customize")).Status);
    }

    [Fact]
    public async Task The_user_routes_need_the_dashboard_permission()
    {
        CookieClient noPermission = WithXsrf(factory.NewSignedInClientAs(44, "user44", Permissions.App));

        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(noPermission, HttpMethod.Get, $"{Me}/onboarding")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(noPermission, HttpMethod.Post, $"{Me}/onboarding", new { answers = new[] { new { questionId = 1, optionId = 1 } } })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await CallAsync(noPermission, HttpMethod.Get, Home)).Status);
    }

    [Fact]
    public async Task A_question_update_replaces_its_options_and_a_delete_removes_them()
    {
        CookieClient staff = Staff();
        int persona = await PersonaAsync(staff, await TemplateAsync(staff, 1), 1);
        var (questionId, options) = await QuestionAsync(staff, 1, persona, persona);

        string json = $$"""
            {"prompt":"Changed","sortOrder":5,"options":[
              {"label":"X","sortOrder":0,"weights":[{"personaId":{{persona}},"weight":1}]},
              {"label":"Y","sortOrder":1,"weights":[{"personaId":{{persona}},"weight":2}]},
              {"label":"Z","sortOrder":2,"weights":[]}]}
            """;
        var (status, body, text) = await CallAsync(staff, HttpMethod.Put, $"{Admin}/onboarding-questions/{questionId}", json);
        Assert.True(status == HttpStatusCode.OK, text);
        Assert.Equal(3, body.GetProperty("options").GetArrayLength());
        Assert.DoesNotContain(options[0], body.GetProperty("options").EnumerateArray().Select(o => o.GetProperty("id").GetInt32()));

        Assert.Equal(HttpStatusCode.NoContent, (await CallAsync(staff, HttpMethod.Delete, $"{Admin}/onboarding-questions/{questionId}")).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await CallAsync(staff, HttpMethod.Put, $"{Admin}/onboarding-questions/{questionId}", json)).Status);
    }
}
