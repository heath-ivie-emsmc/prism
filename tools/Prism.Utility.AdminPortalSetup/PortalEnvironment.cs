namespace Prism.Utility.AdminPortalSetup;

/// <summary>Non-secret portal settings per environment. Application ids for deployed environments are not guessed: pass --application-id.</summary>
internal sealed record PortalEnvironment(string Name, string PortalApiBaseUrl, string Audience, string? DefaultApplicationId)
{
    private static readonly PortalEnvironment[] Known =
    [
        new("dev", "https://api-admin-portal.dev.emsbilling.com", "admin-portal-api-dev", "prism-localhost"),
        new("stage", "https://api-admin-portal.stage.emsbilling.com", "admin-portal-api-stage", null),
        new("prod", "https://api-admin-portal.emsbilling.com", "admin-portal-api-prod", null),
    ];

    public static IReadOnlyList<string> GetNames() => Known.Select(environment => environment.Name).ToList();

    public static PortalEnvironment? Find(string name) =>
        Known.FirstOrDefault(environment => string.Equals(environment.Name, name, StringComparison.OrdinalIgnoreCase));
}
