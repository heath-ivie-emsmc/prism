using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence;
using Prism.Testing.Common;
using Testcontainers.PostgreSql;

namespace Prism.Tests.Integration.Persistence;

/// <summary>A throwaway PostgreSQL with the real migrations applied. Never EnsureCreated: the migrations are what ships.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    /// <summary>The throwaway container database. Every read of it asserts that it is one, so no helper can be handed anything else.</summary>
    public string ConnectionString => TestDatabaseGuard.Assert(container.GetConnectionString());

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
        TestDatabaseGuard.Register(container.GetConnectionString());
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
