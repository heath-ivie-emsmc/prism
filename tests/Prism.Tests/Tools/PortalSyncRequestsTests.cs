using System.Text.Json.Nodes;
using Prism.Application.Auth;
using Prism.Utility.AdminPortalSetup;

namespace Prism.Tests.Tools;

public sealed class PortalSyncRequestsTests
{
    [Fact]
    public void The_permissions_body_lists_every_permission_in_the_shape_the_portal_expects()
    {
        JsonArray permissions = PortalSyncRequests.PermissionsBody()["permissions"]!.AsArray();

        Assert.Equal(Permissions.All.Select(p => p.Id), permissions.Select(p => (string)p!["id"]!));
        JsonObject first = permissions[0]!.AsObject();
        Assert.Equal(["description", "group", "id", "name", "parent"], first.Select(p => p.Key).Order().ToArray());
        Assert.Null(first["parent"]);
        Assert.Equal("Prism", (string)first["group"]!);
    }

    [Fact]
    public void The_roles_body_carries_each_role_with_its_permission_ids_and_a_version()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        JsonArray roles = PortalSyncRequests.CoreRolesBody(now)["roles"]!.AsArray();

        Assert.Equal(Permissions.Roles.Select(r => r.Name), roles.Select(r => (string)r!["id"]!));
        foreach (JsonNode? role in roles)
        {
            Assert.Equal((string)role!["id"]!, (string)role["name"]!);
            Assert.Equal(now.ToUnixTimeSeconds(), (long)role["version"]!);
        }

        string[] superUser = roles.Single(r => (string)r!["id"]! == "SuperUser")!["assignments"]!.AsArray().Select(a => (string)a!).ToArray();
        Assert.Equal(Permissions.All.Select(p => p.Id), superUser);
    }

    [Fact]
    public void Roles_only_reference_permissions_that_the_permissions_body_defines()
    {
        HashSet<string> published = PortalSyncRequests.PermissionsBody()["permissions"]!.AsArray().Select(p => (string)p!["id"]!).ToHashSet();

        IEnumerable<string> referenced = PortalSyncRequests.CoreRolesBody(DateTimeOffset.UtcNow)["roles"]!.AsArray()
            .SelectMany(r => r!["assignments"]!.AsArray().Select(a => (string)a!));

        Assert.All(referenced, id => Assert.Contains(id, published));
    }

    [Fact]
    public void A_later_run_publishes_a_newer_role_version()
    {
        var earlier = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        long first = (long)PortalSyncRequests.CoreRolesBody(earlier)["roles"]![0]!["version"]!;
        long second = (long)PortalSyncRequests.CoreRolesBody(earlier.AddMinutes(1))["roles"]![0]!["version"]!;

        Assert.True(second > first);
    }
}
