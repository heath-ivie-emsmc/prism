namespace Prism.Application.Admin;

/// <summary>One Provider an Internal User can pick: its portal number (the one every route uses) and display name.</summary>
public sealed record ProviderChoice(string Id, string Name);

/// <summary>Where the list of Providers comes from: every Provider's portal number and display name. Read from the warehouse's own tables, not from the portal.</summary>
public interface IProviderListSource
{
    Task<IReadOnlyList<ProviderChoice>> ListAsync(CancellationToken cancellationToken);
}

public static class ProviderSearchLimits
{
    public const int MaxQueryLength = 100;
    public const int MaxResults = 20;

    /// <summary>Companies whose name starts with this are test accounts in the portal and are never offered.</summary>
    public const string TestAccountPrefix = "zzz";
}

/// <summary>
/// Finds Providers by name or number, so staff do not have to know ids. It lists the portal's whole directory, so it is for Internal Users only and
/// never narrows by a customer's assignments. The result is only a choice: whatever is picked is still checked by the route that uses it.
/// </summary>
public interface IProviderCatalog
{
    /// <summary>Up to <see cref="ProviderSearchLimits.MaxResults"/> matches. An empty search gives none.</summary>
    Task<IReadOnlyList<ProviderChoice>> SearchAsync(string query, CancellationToken cancellationToken);
}
