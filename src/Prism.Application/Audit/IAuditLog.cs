using Prism.Application.Metrics;

namespace Prism.Application.Audit;

/// <summary>The kinds of read that leave an audit record.</summary>
public static class AuditActions
{
    public const string ClaimsList = "claims.list";
    public const string ClaimsExport = "claims.export";
}

/// <summary>What one audited read touched: the kind (one of <see cref="AuditActions"/>), the Attention Check key from the catalog, the period, how many rows, and the aircraft-type key the read was narrowed to (a catalog key from the breakdown route, never free text).</summary>
public sealed record AuditRead(string Action, string CheckKey, ApPeriod Period, int Rows, string? GroupKey = null);

/// <summary>
/// Writes the audit record for a read of claim-level data. A record says who read what kind of data for which Provider, period and check,
/// and how many rows. It never holds a claim reference, a name, an amount or a filter value.
/// </summary>
public interface IAuditLog
{
    /// <param name="providerId">The Provider (portal company id) after the access check, never a requested value.</param>
    /// <param name="portalUserId">The signed-in user's portal user id.</param>
    /// <param name="read">What the read touched.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task WriteAsync(int providerId, long portalUserId, AuditRead read, CancellationToken cancellationToken);
}
