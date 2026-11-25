using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Prism.Application.Auth;

namespace Prism.Infrastructure.Auth;

/// <summary>
/// Exchanges a login token's nonce for the user's profile, roles and Provider assignments through the Admin Portal API.
/// The portal consumes the nonce, so call this exactly once per sign-in.
/// </summary>
public sealed class PortalDirectory(
    HttpClient http,
    PortalAppJwtFactory appJwt,
    IOptions<PrismAuthOptions> options,
    PortalCompanyNames companyNames,
    ILogger<PortalDirectory> logger) : IPortalDirectory
{
    internal const int MaxRoles = 100;

    internal const int MaxProviders = 2000;

    internal const int CompanyBatchSize = 100;

    internal const int MaxNameLookups = 25;

    internal const int MaxNameLength = 200;

    internal const int MaxFacilitiesPerProvider = 5000;

    internal const int MaxGroups = 100;

    private static readonly string[] UserNameKeys = ["username", "user_name", "email", "mail"];
    private static readonly string[] IdKeys = ["id", "admin_portal_id", "userId", "user_id"];
    private static readonly string[] RoleKeys = ["roleNames", "role_names", "roles"];
    private static readonly string[] GroupKeys = ["groups", "userGroups", "user_groups", "groupNames", "group_names"];

    public async Task<PortalUser> ResolveUserAsync(string nonce, CancellationToken cancellationToken)
    {
        PrismAuthOptions settings = options.Value;
        Uri baseUri = PortalUrls.AsDirectory(
            PortalUrls.RequireHttpsOrLoopback(settings.AdminPortalApiBaseUrl, nameof(settings.AdminPortalApiBaseUrl)));

        using JsonDocument json = await GetJsonAsync(baseUri, $"user/user-data?nonce_token={Uri.EscapeDataString(nonce)}", cancellationToken);
        PortalUser user = Parse(json.RootElement);

        // The real user-data response carries no roles. They are read by portal user id.
        if (user.PortalUserId is { } portalUserId && user.Roles.Count == 0)
        {
            using JsonDocument rbac = await GetJsonAsync(baseUri, $"user/rbac-data?user_id={portalUserId}", cancellationToken);
            user = user with { Roles = ParseRbacRoles(rbac.RootElement, portalUserId) };
        }

        if (user.Roles.Count == 0 || string.IsNullOrWhiteSpace(user.UserName))
        {
            // Only property names, never values: this is how a mismatch with the real response shape gets diagnosed.
            PortalDirectoryLog.UnexpectedShape(logger, KeyNames(json.RootElement), KeyNames(Unwrap(json.RootElement)));
        }

        return user;
    }

    public async Task<IReadOnlyList<ProviderScope>> ResolveProvidersAsync(long portalUserId, CancellationToken cancellationToken)
    {
        PrismAuthOptions settings = options.Value;
        Uri baseUri = PortalUrls.AsDirectory(
            PortalUrls.RequireHttpsOrLoopback(settings.AdminPortalApiBaseUrl, nameof(settings.AdminPortalApiBaseUrl)));
        return await ReadProvidersAsync(baseUri, portalUserId, cancellationToken);
    }

    // The portal calls a Provider a company. The assigned companies come with their Facilities. Names are looked up one company
    // at a time: the directory is thousands of rows, and a user is assigned only a few companies.
    private async Task<List<ProviderScope>> ReadProvidersAsync(Uri baseUri, long portalUserId, CancellationToken cancellationToken)
    {
        using JsonDocument assigned = await GetJsonAsync(baseUri, $"user/assigned-companies?user_id={portalUserId}", cancellationToken);
        List<ProviderScope> providers = ParseAssignedCompanies(assigned.RootElement);
        // Counts and company ids only (ids are not personal data). This is how an unexpectedly large or empty scope gets explained.
        PortalDirectoryLog.AssignedCompanies(
            logger,
            providers.Count,
            providers.Sum(provider => provider.FacilityIds.Count),
            string.Join(", ", providers.Take(10).Select(provider => $"{provider.Id}({provider.FacilityIds.Count}: {string.Join(' ', provider.FacilityIds.Where(id => id.All(char.IsAsciiDigit)).Take(20))})")));

        if (providers.Count >= MaxProviders)
        {
            PortalDirectoryLog.AssignmentsTruncated(logger, MaxProviders);
        }

        // Beyond the cap a company keeps its id as its name, instead of a sign-in making hundreds of calls.
        ProviderScope[] named = await Task.WhenAll(providers.Select((provider, index) =>
            index < MaxNameLookups ? WithNameAsync(baseUri, provider, cancellationToken) : Task.FromResult(provider)));
        return [.. named];
    }

    private async Task<ProviderScope> WithNameAsync(Uri baseUri, ProviderScope provider, CancellationToken cancellationToken)
    {
        string? name = await companyNames.NameAsync(provider.Id, token => LookUpCompanyNameAsync(baseUri, provider.Id, token), cancellationToken);
        return string.IsNullOrWhiteSpace(name) ? provider : provider with { Name = name };
    }

    // company/batch returns the companies with an id greater than from_id, in id order. Asking from id - 1 puts this company first.
    private async Task<string?> LookUpCompanyNameAsync(Uri baseUri, string companyId, CancellationToken cancellationToken)
    {
        if (!long.TryParse(companyId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long id) || id < 1)
        {
            return null;
        }

        using JsonDocument json = await GetJsonAsync(baseUri, $"company/batch?from_id={id - 1}", cancellationToken);
        (IReadOnlyList<KeyValuePair<long, string>> companies, _) = ParseCompanyBatch(json.RootElement);
        return companies.FirstOrDefault(company => company.Key == id).Value;
    }

    private async Task<JsonDocument> GetJsonAsync(Uri baseUri, string relativeUrl, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, relativeUrl));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", appJwt.Create());
        request.Headers.Add("X-Ems-Application-Id", options.Value.ApplicationId);

        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        string body = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonDocument.Parse(body);
    }

    private static string KeyNames(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object
            ? string.Join(", ", element.EnumerateObject().Take(30).Select(p => SafeKeyName(p.Name)))
            : element.ValueKind.ToString();

    // Only plain identifier-looking keys are logged. A key that is not (a value used as a key, say) is replaced, never quoted.
    private static string SafeKeyName(string name) =>
        name.Length is > 0 and <= 50 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') ? name : "(other)";

    /// <summary>
    /// <c>user/assigned-companies</c>: an object keyed by company id with the Facility ids, such as <c>{"1":[1,2],"2":[]}</c>.
    /// An empty list means all Facilities. PHP serializes an empty map as <c>[]</c>, so an array means nobody is assigned.
    /// </summary>
    internal static List<ProviderScope> ParseAssignedCompanies(JsonElement root)
    {
        JsonElement data = Unwrap(root);
        if (data.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return data.EnumerateObject()
            .Where(property => property.Name.Length is > 0 and <= 20 && property.Name.All(char.IsAsciiDigit))
            .Take(MaxProviders)
            .Select(property => new ProviderScope(property.Name, property.Name, ReadFacilityIds(property.Value)))
            .ToList();
    }

    /// <summary><c>company/batch</c>: up to 100 companies as <c>{id, name, ...}</c>. Returns the usable ones and how many rows the portal sent.</summary>
    internal static (IReadOnlyList<KeyValuePair<long, string>> Companies, int Rows) ParseCompanyBatch(JsonElement root)
    {
        JsonElement data = root.ValueKind == JsonValueKind.Object && TryGet(root, "result", out JsonElement result) ? result : root;
        if (data.ValueKind != JsonValueKind.Array)
        {
            return ([], 0);
        }

        var companies = new List<KeyValuePair<long, string>>();
        int rows = 0;
        foreach (JsonElement row in data.EnumerateArray().Take(CompanyBatchSize))
        {
            rows++;
            long? id = ReadLong(row, ["id"]);
            string? name = ReadString(row, ["name"])?.Trim();
            if (id is > 0 && !string.IsNullOrEmpty(name))
            {
                companies.Add(new KeyValuePair<long, string>(id.Value, name.Length <= MaxNameLength ? name : name[..MaxNameLength]));
            }
        }

        return (companies, rows);
    }

    /// <summary>Role names assigned to the user in an RBAC response: <c>assignments[{user_id, item_name}]</c>. Unknown names are filtered later by the permission catalog.</summary>
    internal static List<string> ParseRbacRoles(JsonElement root, long portalUserId)
    {
        JsonElement data = Unwrap(root);
        if (!TryGet(data, "assignments", out JsonElement assignments) || assignments.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        // A response without any user_id is scoped by the query (user_id was in the URL). A response where some rows name a user
        // is a multi-user list, and a row that does not name one is not ours.
        bool anyRowNamesAUser = assignments.EnumerateArray().Any(item => TryGet(item, "user_id", out _));
        return assignments.EnumerateArray()
            .Where(item => AssignedTo(item, portalUserId, anyRowNamesAUser))
            .Select(item => ReadString(item, ["item_name"])?.Trim())
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxRoles)
            .ToList();
    }

    // An assignment for a different user is never ours. A row with no user id is ours only when no row in the response names one.
    private static bool AssignedTo(JsonElement assignment, long portalUserId, bool anyRowNamesAUser)
    {
        if (!TryGet(assignment, "user_id", out JsonElement id))
        {
            return !anyRowNamesAUser;
        }

        return id.ValueKind switch
        {
            JsonValueKind.Number => id.TryGetInt64(out long number) && number == portalUserId,
            JsonValueKind.String => long.TryParse(id.GetString(), out long parsed) && parsed == portalUserId,
            _ => false,
        };
    }

    internal static PortalUser Parse(JsonElement root)
    {
        JsonElement data = Unwrap(root);

        string userName = ReadString(data, UserNameKeys)?.Trim() ?? "";
        long? portalId = ReadLong(data, IdKeys);
        IReadOnlyList<string> roles = ReadRoles(data);
        IReadOnlyList<ProviderScope> providers = ReadProviders(data);

        return new PortalUser(userName, ReadDisplayName(data) ?? userName, portalId, roles, providers) { Groups = ReadGroups(data) };
    }

    /// <summary>
    /// The user groups the user belongs to, as plain names or as objects with a name. The portal's shape for this is not confirmed yet,
    /// so anything unrecognised gives no groups, which means the user is not an Internal User (fails closed).
    /// </summary>
    internal static List<string> ReadGroups(JsonElement element)
    {
        foreach (string key in GroupKeys)
        {
            if (!TryGet(element, key, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            return value.EnumerateArray()
                .Select(ReadGroupName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .Where(name => name.Length <= MaxNameLength)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxGroups)
                .ToList();
        }

        return [];
    }

    private static string? ReadGroupName(JsonElement item) => item.ValueKind switch
    {
        JsonValueKind.Object => ReadString(item, ["name", "group_name", "title"]),
        JsonValueKind.String => item.GetString(),
        _ => null,
    };

    private static JsonElement Unwrap(JsonElement root)
    {
        foreach (string wrapper in new[] { "data", "result" })
        {
            if (TryGet(root, wrapper, out JsonElement inner) && inner.ValueKind == JsonValueKind.Object)
            {
                return inner;
            }
        }

        return root;
    }

    private static bool TryGet(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        JsonProperty? match = element.EnumerateObject()
            .Cast<JsonProperty?>()
            .FirstOrDefault(property => string.Equals(property!.Value.Name, name, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return false;
        }

        value = match.Value.Value;
        return true;
    }

    private static string? ReadString(JsonElement element, string[] names)
    {
        foreach (string name in names)
        {
            if (TryGet(element, name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    private static long? ReadLong(JsonElement element, string[] names)
    {
        foreach (string name in names)
        {
            if (!TryGet(element, name, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out number))
            {
                return number;
            }
        }

        return null;
    }

    private static string? ReadDisplayName(JsonElement element)
    {
        string? name = ReadString(element, ["displayName", "display_name", "name"]);
        if (name is not null)
        {
            return name.Trim();
        }

        string combined = $"{ReadString(element, ["firstName", "first_name"])} {ReadString(element, ["lastName", "last_name"])}".Trim();
        return combined.Length > 0 ? combined : null;
    }

    private static List<string> ReadRoles(JsonElement element)
    {
        foreach (string key in RoleKeys)
        {
            if (TryGet(element, key, out JsonElement value) && value.ValueKind == JsonValueKind.Array)
            {
                return value.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                    .Select(item => item.GetString()!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(MaxRoles)
                    .ToList();
            }
        }

        return [];
    }

    // The portal returns { "<providerId>": [facilityId, ...] }. Names are not part of this response yet.
    private static List<ProviderScope> ReadProviders(JsonElement element)
    {
        if (!TryGet(element, "assignments", out JsonElement assignments) || assignments.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        return assignments.EnumerateObject()
            .Take(MaxProviders)
            .Select(property => new ProviderScope(property.Name, property.Name, ReadFacilityIds(property.Value)))
            .ToList();
    }

    private static List<string> ReadFacilityIds(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(item => item.ValueKind switch
            {
                JsonValueKind.Number => item.GetRawText(),
                JsonValueKind.String => item.GetString(),
                _ => null,
            })
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .Take(MaxFacilitiesPerProvider)
            .ToList();
    }
}

internal static partial class PortalDirectoryLog
{
    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Portal returned {CompanyCount} assigned companies with {FacilityCount} facilities in total. First companies as id(facility count: facility ids): [{FirstCompanies}].")]
    public static partial void AssignedCompanies(ILogger logger, int companyCount, int facilityCount, string firstCompanies);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "The portal returned at least {Limit} assigned companies. Only the first {Limit} are used.")]
    public static partial void AssignmentsTruncated(ILogger logger, int limit);

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Portal user data had no roles or no user name. Top-level keys: [{TopKeys}]. Data keys: [{DataKeys}].")]
    public static partial void UnexpectedShape(ILogger logger, string topKeys, string dataKeys);
}
