namespace Prism.Application.Auth;

/// <summary>What the Admin Portal tells us about a user after a login token is exchanged.</summary>
public sealed record PortalUser(
    string UserName,
    string DisplayName,
    long? PortalUserId,
    IReadOnlyList<string> Roles,
    IReadOnlyList<ProviderScope> Providers);

/// <summary>A Provider the user may see. An empty <see cref="FacilityIds"/> means all Facilities.</summary>
public sealed record ProviderScope(string Id, string Name, IReadOnlyList<string> FacilityIds);
