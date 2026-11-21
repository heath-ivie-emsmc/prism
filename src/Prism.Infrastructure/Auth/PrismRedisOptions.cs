namespace Prism.Infrastructure.Auth;

/// <summary>
/// Redis holds short-lived security state shared across instances (login replay protection). The connection string is a secret.
/// Empty is allowed only outside deployed environments, where one in-memory store is enough.
/// </summary>
public sealed class PrismRedisOptions
{
    public const string ConnectionStringName = "Redis";

    public string ConnectionString { get; set; } = "";
}
