using System.Text.Json;
using Prism.Application.Auth;
using Prism.Infrastructure.Auth;

namespace Prism.Tests.Auth;

public sealed class PortalDirectoryParseTests
{
    private static PortalUser Parse(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return PortalDirectory.Parse(document.RootElement);
    }

    [Fact]
    public void Reads_the_documented_shape()
    {
        PortalUser user = Parse("""
            { "username": "jdoe", "id": 42, "displayName": "Jane Doe",
              "roleNames": ["Analyst", "Admin"],
              "assignments": { "100": [1, 2], "200": [] } }
            """);

        Assert.Equal("jdoe", user.UserName);
        Assert.Equal("Jane Doe", user.DisplayName);
        Assert.Equal(42, user.PortalUserId);
        Assert.Equal(["Analyst", "Admin"], user.Roles);
        Assert.Equal(["100", "200"], user.Providers.Select(p => p.Id));
        Assert.Equal(["1", "2"], user.Providers[0].FacilityIds);
        Assert.Empty(user.Providers[1].FacilityIds);
    }

    [Fact]
    public void Unwraps_a_data_envelope_and_ignores_property_case()
    {
        PortalUser user = Parse("""{ "data": { "UserName": "jdoe", "RoleNames": ["Analyst"] } }""");

        Assert.Equal("jdoe", user.UserName);
        Assert.Equal(["Analyst"], user.Roles);
    }

    [Fact]
    public void Falls_back_to_email_and_to_a_string_id()
    {
        PortalUser user = Parse("""{ "email": "jane@example.org", "admin_portal_id": "77", "roles": ["Analyst"] }""");

        Assert.Equal("jane@example.org", user.UserName);
        Assert.Equal("jane@example.org", user.DisplayName);
        Assert.Equal(77, user.PortalUserId);
    }

    [Fact]
    public void Builds_a_display_name_from_first_and_last_name()
    {
        PortalUser user = Parse("""{ "username": "jdoe", "first_name": "Jane", "last_name": "Doe", "roles": ["x"] }""");

        Assert.Equal("Jane Doe", user.DisplayName);
    }

    [Fact]
    public void Returns_empty_collections_when_fields_are_missing_or_the_wrong_type()
    {
        PortalUser user = Parse("""{ "username": "jdoe", "roles": "Analyst", "assignments": [1, 2] }""");

        Assert.Empty(user.Roles);
        Assert.Empty(user.Providers);
        Assert.Null(user.PortalUserId);
    }

    [Fact]
    public void Removes_duplicate_roles_ignoring_case()
    {
        PortalUser user = Parse("""{ "username": "jdoe", "roles": ["Analyst", "analyst", " ", "Admin"] }""");

        Assert.Equal(["Analyst", "Admin"], user.Roles);
    }

    [Fact]
    public void Caps_the_number_of_roles_providers_and_facilities()
    {
        string roles = string.Join(",", Enumerable.Range(0, PortalDirectory.MaxRoles + 50).Select(i => $"\"r{i}\""));
        string providers = string.Join(",", Enumerable.Range(0, PortalDirectory.MaxProviders + 50).Select(i => $"\"{i}\": []"));
        string facilities = string.Join(",", Enumerable.Range(0, PortalDirectory.MaxFacilitiesPerProvider + 50));

        PortalUser user = Parse($$"""{ "username": "u", "roles": [{{roles}}], "assignments": { {{providers}}, "big": [{{facilities}}] } }""");

        Assert.Equal(PortalDirectory.MaxRoles, user.Roles.Count);
        Assert.Equal(PortalDirectory.MaxProviders, user.Providers.Count);
    }

    [Fact]
    public void Caps_facilities_within_a_provider()
    {
        string facilities = string.Join(",", Enumerable.Range(0, PortalDirectory.MaxFacilitiesPerProvider + 50));

        PortalUser user = Parse($$"""{ "username": "u", "roles": ["r"], "assignments": { "big": [{{facilities}}] } }""");

        Assert.Equal(PortalDirectory.MaxFacilitiesPerProvider, user.Providers[0].FacilityIds.Count);
    }

    [Fact]
    public void Reports_an_empty_user_name_when_the_portal_sends_none()
    {
        PortalUser user = Parse("""{ "roles": ["Analyst"] }""");

        Assert.Equal("", user.UserName);
    }

    private static List<string> Rbac(string json, long userId)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return PortalDirectory.ParseRbacRoles(document.RootElement, userId);
    }

    [Fact]
    public void Rbac_roles_are_the_names_assigned_to_this_user_only()
    {
        List<string> roles = Rbac(
            """
            {"type":"success","result":{"items":[],"assignments":[
              {"user_id":7,"item_name":"prism-localhost__ProviderAdmin"},
              {"user_id":"7","item_name":"SuperUser"},
              {"user_id":8,"item_name":"PlatformSupport"}]}}
            """,
            7);

        Assert.Equal(["prism-localhost__ProviderAdmin", "SuperUser"], roles);
    }

    [Fact]
    public void Rbac_roles_are_empty_when_the_response_has_no_assignments()
    {
        Assert.Empty(Rbac("""{"result":{"items":[]}}""", 7));
        Assert.Empty(Rbac("""{"result":{"assignments":"nope"}}""", 7));
    }

    [Fact]
    public void Rbac_roles_ignore_blank_duplicate_and_non_string_names_and_are_capped()
    {
        string many = string.Join(",", Enumerable.Range(0, 300).Select(i => $$"""{"user_id":7,"item_name":"role{{i}}"}"""));

        Assert.Equal(PortalDirectory.MaxRoles, Rbac("{\"result\":{\"assignments\":[" + many + "]}}", 7).Count);
        Assert.Equal(
            ["A"],
            Rbac("""{"result":{"assignments":[{"user_id":7,"item_name":"A"},{"user_id":7,"item_name":"a"},{"user_id":7,"item_name":" "},{"user_id":7,"item_name":5}]}}""", 7));
    }

    [Fact]
    public void Rbac_rows_without_a_user_id_are_not_ours_when_other_rows_name_a_user()
    {
        // A multi-user list: a row that names nobody must not be handed to whoever is signing in.
        List<string> roles = Rbac(
            """{"result":{"assignments":[{"user_id":7,"item_name":"A"},{"item_name":"SuperUser"},{"user_id":9,"item_name":"B"}]}}""",
            7);

        Assert.Equal(["A"], roles);
    }

    [Fact]
    public void Rbac_rows_without_a_user_id_are_ours_when_the_response_is_scoped_by_the_query()
    {
        Assert.Equal(["A", "B"], Rbac("""{"result":{"assignments":[{"item_name":"A"},{"item_name":"B"}]}}""", 7));
    }

    [Fact]
    public void Assigned_companies_are_keyed_by_company_id_with_their_facility_ids()
    {
        using JsonDocument document = JsonDocument.Parse("""{"type":"result","result":{"1":[1,2,3],"2":[]}}""");

        List<ProviderScope> providers = PortalDirectory.ParseAssignedCompanies(document.RootElement);

        Assert.Equal([("1", "1,2,3"), ("2", "")], providers.Select(p => (p.Id, string.Join(',', p.FacilityIds))));
    }

    [Theory]
    [InlineData("""{"result":[]}""")]
    [InlineData("""{"result":{}}""")]
    [InlineData("""{"result":"nope"}""")]
    [InlineData("""[]""")]
    public void No_assigned_companies_in_any_of_the_shapes_the_portal_uses_means_no_Providers(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);

        Assert.Empty(PortalDirectory.ParseAssignedCompanies(document.RootElement));
    }

    [Fact]
    public void Assigned_companies_ignore_keys_that_are_not_plain_numbers_and_are_capped()
    {
        string many = string.Join(",", Enumerable.Range(1, 2500).Select(i => $"\"{i}\":[]"));
        using JsonDocument document = JsonDocument.Parse("{\"result\":{\"abc\":[1],\"1 OR 1=1\":[2]," + many + "}}");

        List<ProviderScope> providers = PortalDirectory.ParseAssignedCompanies(document.RootElement);

        Assert.Equal(PortalDirectory.MaxProviders, providers.Count);
        Assert.All(providers, p => Assert.True(p.Id.All(char.IsAsciiDigit)));
    }

    [Fact]
    public void A_company_batch_gives_id_and_name_and_skips_rows_without_both()
    {
        using JsonDocument document = JsonDocument.Parse(
            """{"result":[{"id":5,"name":" Mercy Air "},{"id":6},{"name":"No id"},{"id":0,"name":"Zero"},{"id":7,"name":""}]}""");

        (IReadOnlyList<KeyValuePair<long, string>> companies, int rows) = PortalDirectory.ParseCompanyBatch(document.RootElement);

        Assert.Equal(5, rows);
        Assert.Equal([new KeyValuePair<long, string>(5, "Mercy Air")], companies);
    }

    [Fact]
    public void A_company_name_is_capped_in_length()
    {
        using JsonDocument document = JsonDocument.Parse("{\"result\":[{\"id\":1,\"name\":\"" + new string('x', 500) + "\"}]}");

        (IReadOnlyList<KeyValuePair<long, string>> companies, _) = PortalDirectory.ParseCompanyBatch(document.RootElement);

        Assert.Equal(PortalDirectory.MaxNameLength, companies[0].Value.Length);
    }
}
