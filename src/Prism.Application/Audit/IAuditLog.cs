namespace Prism.Application.Audit;

/// <summary>The kinds of read that leave an audit record.</summary>
public static class AuditActions
{
    public const string ClaimsList = "claims.list";
    public const string ClaimsExport = "claims.export";
}

/// <summary>
/// Writes the audit record for a read of claim-level data. A record says who read what kind of data for which Provider, period and check,
/// and how many rows. It never holds a claim reference, a name, an amount or a filter value.
/// </summary>
public interface IAuditLog
{
    /// <param name="providerId">The Provider (portal company id) after the access check, never a requested value.</param>
    /// <param name="portalUserId">The signed-in user's portal user id.</param>
    /// <param name="action">One of <see cref="AuditActions"/>.</param>
    /// <param name="checkKey">The Attention Check key, taken from the catalog.</param>
    /// <param name="period">The reporting period as yyyyMM.</param>
    /// <param name="rowCount">How many claim rows the response carried.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task WriteAsync(int providerId, long portalUserId, string action, string checkKey, string period, int rowCount, CancellationToken cancellationToken);
}
