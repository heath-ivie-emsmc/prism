using Microsoft.EntityFrameworkCore;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Prism.Infrastructure.Persistence;

/// <summary>
/// The only place a Prism DbContext is pointed at PostgreSQL. Hosts, design-time factories and tests call
/// <see cref="UsePrismNpgsql"/> and never <c>UseNpgsql</c>, so provider settings stay in one spot.
/// </summary>
public static class PrismNpgsqlConfiguration
{
    public static DbContextOptionsBuilder UsePrismNpgsql(
        this DbContextOptionsBuilder builder,
        string connectionString,
        Action<NpgsqlDbContextOptionsBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.UseNpgsql(connectionString, options => configure?.Invoke(options));
    }
}
