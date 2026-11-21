using System.Text.Json;
using System.Text.Json.Nodes;
using Prism.Application.Auth;

namespace Prism.Utility.AdminPortalSetup;

/// <summary>Builds the bodies the Admin Portal API expects, from the permission and role catalog the API itself uses.</summary>
internal static class PortalSyncRequests
{
    public const string PermissionsPath = "app/permissions";
    public const string CoreRolesPath = "app/core-roles";

    /// <summary>Body for POST app/permissions. Every Prism permission is top level (a flat list in the portal UI).</summary>
    public static JsonObject PermissionsBody() => new()
    {
        ["permissions"] = new JsonArray(Permissions.All.Select(permission => (JsonNode)new JsonObject
        {
            ["id"] = permission.Id,
            ["name"] = permission.Name,
            ["description"] = permission.Name,
            ["group"] = permission.Group,
            ["parent"] = permission.Parent,
        }).ToArray()),
    };

    /// <summary>Body for POST app/core-roles. The version is the time of the run, so the portal sees each publish as newer.</summary>
    public static JsonObject CoreRolesBody(DateTimeOffset now) => new()
    {
        ["roles"] = new JsonArray(Permissions.Roles.Select(role => (JsonNode)new JsonObject
        {
            ["id"] = role.Name,
            ["name"] = role.Name,
            ["version"] = now.ToUnixTimeSeconds(),
            ["assignments"] = new JsonArray(role.Permissions.Select(id => (JsonNode)JsonValue.Create(id)).ToArray()),
        }).ToArray()),
    };

    public static string Serialize(JsonObject body) => body.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}
