using Microsoft.EntityFrameworkCore;
using Prism.Application.Audit;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Tests.Integration.Persistence;

public sealed class AuditLogTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task A_read_is_recorded_with_who_what_and_how_many_and_the_database_sets_the_time()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();

        await new AuditLog(db).WriteAsync(4101, 9001, AuditActions.ClaimsList, "denied", "202610", 2, ct);

        AuditEntry row = await db.AuditEntries.AsNoTracking().SingleAsync(e => e.ProviderId == 4101 && e.PortalUserId == 9001, ct);
        Assert.Equal(AuditActions.ClaimsList, row.Action);
        Assert.Equal("denied", row.CheckKey);
        Assert.Equal("202610", row.Period);
        Assert.Equal(2, row.RowCount);
        Assert.True(row.CreatedAtTimestamp > DateTimeOffset.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task The_audit_table_has_no_column_that_could_hold_claim_detail()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();

        List<string> columns = await db.Database
            .SqlQuery<string>($"SELECT column_name AS \"Value\" FROM information_schema.columns WHERE table_name = 'audit_entry' ORDER BY ordinal_position")
            .ToListAsync(ct);

        Assert.Equal(
            ["id", "provider_id", "portal_user_id", "action", "check_key", "period", "row_count", "created_at_timestamp", "updated_at_timestamp"],
            columns);
    }

    [Fact]
    public async Task A_blank_action_is_refused()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();

        await Assert.ThrowsAsync<ArgumentException>(() => new AuditLog(db).WriteAsync(1, 1, " ", "denied", "202610", 0, ct));
    }
}
