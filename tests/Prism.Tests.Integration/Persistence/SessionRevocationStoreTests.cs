using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence;

namespace Prism.Tests.Integration.Persistence;

public sealed class SessionRevocationStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private static SessionRevocationStore NewStore(PrismDbContext db) => new(db, new FixedTime(Now));

    [Fact]
    public async Task A_revoked_session_is_reported_revoked_and_others_are_not()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SessionRevocationStore store = NewStore(db);

        await store.RevokeAsync("session-a", "jdoe", Now.AddHours(8), ct);

        Assert.True(await store.IsRevokedAsync("session-a", ct));
        Assert.False(await store.IsRevokedAsync("session-b", ct));
    }

    [Fact]
    public async Task Revoking_the_same_session_twice_is_not_an_error()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SessionRevocationStore store = NewStore(db);

        await store.RevokeAsync("session-twice", "jdoe", Now.AddHours(8), ct);
        await store.RevokeAsync("session-twice", "jdoe", Now.AddHours(8), ct);

        Assert.Equal(1, await db.RevokedSessions.CountAsync(r => r.SessionId == "session-twice", ct));
    }

    [Fact]
    public async Task Revoking_purges_rows_for_sessions_that_have_already_expired()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SessionRevocationStore store = NewStore(db);
        await store.RevokeAsync("session-old", "jdoe", Now.AddHours(-1), ct);

        await store.RevokeAsync("session-new", "jdoe", Now.AddHours(8), ct);

        Assert.False(await store.IsRevokedAsync("session-old", ct));
        Assert.True(await store.IsRevokedAsync("session-new", ct));
    }

    [Fact]
    public async Task A_just_expired_session_stays_revoked_through_the_clock_skew_window()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SessionRevocationStore store = NewStore(db);
        // Expired 90 seconds ago: the session token is still accepted for up to 2 minutes after expiry.
        await store.RevokeAsync("session-skew", "jdoe", Now.AddSeconds(-90), ct);

        await store.RevokeAsync("session-other", "jdoe", Now.AddHours(8), ct);

        Assert.True(await store.IsRevokedAsync("session-skew", ct));
    }

    [Fact]
    public async Task Hostile_ids_are_stored_as_data_not_run_as_sql()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SessionRevocationStore store = NewStore(db);
        const string hostile = "x'); DROP TABLE revoked_session;--";

        await store.RevokeAsync(hostile, "o'brien", Now.AddHours(8), ct);

        Assert.True(await store.IsRevokedAsync(hostile, ct));
        Assert.True(await db.RevokedSessions.AnyAsync(ct));
    }

    [Fact]
    public async Task The_database_assigns_the_timestamps()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        await NewStore(db).RevokeAsync("session-stamps", "jdoe", Now.AddHours(8), ct);

        var row = await db.RevokedSessions.AsNoTracking().SingleAsync(r => r.SessionId == "session-stamps", ct);

        Assert.True(row.CreatedAtTimestamp > DateTimeOffset.UtcNow.AddMinutes(-5));
        Assert.Equal(row.CreatedAtTimestamp, row.UpdatedAtTimestamp);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
