using System.Net;
using System.Text.Json;
using Prism.Application.Admin;
using Prism.Application.Auth;
using Prism.Tests.Integration.Auth;

namespace Prism.Tests.Integration.Admin;

/// <summary>Staff find a Provider by name or number instead of knowing ids. The search lists the whole directory, so it is for Internal Users only.</summary>
public sealed class ProviderSearchTests : IDisposable
{
    private readonly AuthTestFactory factory = new();

    public void Dispose() => factory.Dispose();

    private CookieClient Staff() => factory.NewSignedInClientInGroups([AuthTestFactory.InternalGroupName], Permissions.App, Permissions.AdminTemplatesAuthor);

    private static async Task<(HttpStatusCode Status, string[] Names, string[] Ids)> SearchAsync(CookieClient client, string query)
    {
        HttpResponseMessage response = await client.GetAsync($"/api/v1/admin/providers?q={Uri.EscapeDataString(query)}");
        string text = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return (response.StatusCode, [], []);
        }

        JsonElement[] found = [.. JsonElement.Parse(text).GetProperty("providers").EnumerateArray()];
        return (response.StatusCode, [.. found.Select(p => p.GetProperty("name").GetString()!)], [.. found.Select(p => p.GetProperty("id").GetString()!)]);
    }

    [Fact]
    public async Task A_name_fragment_in_any_case_finds_the_provider_and_its_number()
    {
        var (status, names, ids) = await SearchAsync(Staff(), "ABBEV");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(["Abbeville County Emergency Services"], names);
        Assert.Equal(["9001"], ids);
    }

    [Fact]
    public async Task Digits_match_the_start_of_a_number()
    {
        var (_, _, ids) = await SearchAsync(Staff(), "52");

        Assert.Equal(["526"], ids);
    }

    [Fact]
    public async Task An_empty_search_and_a_search_with_no_match_return_nothing()
    {
        CookieClient staff = Staff();

        Assert.Empty((await SearchAsync(staff, "   ")).Names);
        Assert.Empty((await SearchAsync(staff, "zzzz-no-such-provider")).Names);
    }

    [Fact]
    public async Task A_very_long_search_is_cut_down_and_answered_normally()
    {
        var (status, names, _) = await SearchAsync(Staff(), new string('a', 5000));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(names);
    }

    [Fact]
    public async Task Results_are_limited_and_names_that_start_with_the_search_come_first()
    {
        factory.ProviderList.Companies.Clear();
        factory.ProviderList.Companies.AddRange(Enumerable.Range(1, 50).Select(i => new ProviderChoice(i.ToString(System.Globalization.CultureInfo.InvariantCulture), $"County {i:D2} Fire")));
        factory.ProviderList.Companies.Add(new ProviderChoice("900", "County"));

        var (_, names, _) = await SearchAsync(Staff(), "County");

        Assert.Equal(20, names.Length);
        Assert.Equal("County", names[0]);
    }

    [Fact]
    public async Task A_customer_user_and_an_anonymous_caller_are_refused()
    {
        CookieClient customer = factory.NewSignedInClientAs(41, "customer", Permissions.App, Permissions.DashboardView);

        Assert.Equal(HttpStatusCode.Forbidden, (await SearchAsync(customer, "mercy")).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SearchAsync(factory.NewClient(), "mercy")).Status);
    }

    [Fact]
    public async Task A_tier_without_the_internal_marker_is_refused()
    {
        CookieClient tierOnly = factory.NewSignedInClient(Permissions.App, Permissions.AdminTemplatesAuthor);

        Assert.Equal(HttpStatusCode.Forbidden, (await SearchAsync(tierOnly, "mercy")).Status);
    }
}
