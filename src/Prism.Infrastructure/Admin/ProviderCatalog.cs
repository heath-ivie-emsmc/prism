using Prism.Application.Admin;
using Prism.Application.Warehouse;

namespace Prism.Infrastructure.Admin;

/// <summary>The Provider list, kept for an hour and shared by every request. One caller refreshes it while the others wait; a failed refresh keeps the old list.</summary>
public sealed class ProviderCatalogCache(TimeProvider timeProvider) : IDisposable
{
    internal static readonly TimeSpan LifeTime = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim refresh = new(1, 1);
    private IReadOnlyList<ProviderChoice> companies = [];
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    public void Dispose() => refresh.Dispose();

    /// <summary>Reads the list now, whether or not it has expired, so a background refresh keeps it warm. A failure keeps the old list.</summary>
    public async Task RefreshAsync(Func<CancellationToken, Task<IReadOnlyList<ProviderChoice>>> load, CancellationToken cancellationToken)
    {
        await refresh.WaitAsync(cancellationToken);
        try
        {
            companies = await load(cancellationToken);
            expiresAt = timeProvider.GetUtcNow() + LifeTime;
        }
        finally
        {
            refresh.Release();
        }
    }

    public async Task<IReadOnlyList<ProviderChoice>> GetAsync(Func<CancellationToken, Task<IReadOnlyList<ProviderChoice>>> load, CancellationToken cancellationToken)
    {
        if (timeProvider.GetUtcNow() < expiresAt)
        {
            return companies;
        }

        await refresh.WaitAsync(cancellationToken);
        try
        {
            if (timeProvider.GetUtcNow() < expiresAt)
            {
                return companies;
            }

            try
            {
                companies = await load(cancellationToken);
                expiresAt = timeProvider.GetUtcNow() + LifeTime;
            }
            catch (Exception ex) when (ex is WarehouseUnavailableException or WarehouseResultTooLargeException)
            {
                // Serve the old list rather than nothing. With no old list there is nothing to serve, so the caller sees the failure.
                if (companies.Count == 0)
                {
                    throw;
                }
            }

            return companies;
        }
        finally
        {
            refresh.Release();
        }
    }
}

public sealed class ProviderCatalog(IProviderListSource source, ProviderCatalogCache cache) : IProviderCatalog
{
    public async Task<IReadOnlyList<ProviderChoice>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        string text = query.Trim();
        if (text.Length == 0)
        {
            return [];
        }

        if (text.Length > ProviderSearchLimits.MaxQueryLength)
        {
            text = text[..ProviderSearchLimits.MaxQueryLength];
        }

        IReadOnlyList<ProviderChoice> all = await cache.GetAsync(source.ListAsync, cancellationToken);
        bool digits = text.All(char.IsAsciiDigit);
        return [.. all
            .Where(company => !company.Name.TrimStart().StartsWith(ProviderSearchLimits.TestAccountPrefix, StringComparison.OrdinalIgnoreCase))
            .Where(company => company.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || (digits && company.Id.StartsWith(text, StringComparison.Ordinal)))
            .OrderByDescending(company => company.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            .ThenBy(company => company.Name, StringComparer.OrdinalIgnoreCase)
            .Take(ProviderSearchLimits.MaxResults)];
    }
}
