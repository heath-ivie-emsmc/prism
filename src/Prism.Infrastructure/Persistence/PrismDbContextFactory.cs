using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Prism.Infrastructure.Persistence;

/// <summary>
/// Used only by <c>dotnet ef</c>. Generating a migration never connects, so the fallback string is a placeholder.
/// Applying one needs a real target: pass <c>--connection</c>, or set <c>PRISM_DB_CONNECTION</c>.
/// </summary>
public sealed class PrismDbContextFactory : IDesignTimeDbContextFactory<PrismDbContext>
{
    public const string ConnectionVariable = "PRISM_DB_CONNECTION";

    private const string DesignTimeOnly = "Host=localhost;Database=prism_design_time;Username=design";

    public PrismDbContext CreateDbContext(string[] args)
    {
        string connection = Environment.GetEnvironmentVariable(ConnectionVariable) is { Length: > 0 } fromEnvironment
            ? fromEnvironment
            : DesignTimeOnly;

        var builder = new DbContextOptionsBuilder<PrismDbContext>();
        builder.UsePrismNpgsql(connection);
        return new PrismDbContext(builder.Options);
    }
}
