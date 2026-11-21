using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Prism.Tests.Integration.Persistence;

/// <summary>A throwaway PostgreSQL with the real migrations applied. Never EnsureCreated: the migrations are what ships.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString => container.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
        await using PrismDbContext db = NewContext();
        await db.Database.MigrateAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();

    public PrismDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<PrismDbContext>();
        builder.UsePrismNpgsql(ConnectionString);
        return new PrismDbContext(builder.Options);
    }
}
