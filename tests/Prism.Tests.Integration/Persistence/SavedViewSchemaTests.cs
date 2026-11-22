using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Tests.Integration.Persistence;

/// <summary>The Saved View tables against real PostgreSQL: the constraints the application leans on, not the application itself.</summary>
public sealed class SavedViewSchemaTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static SavedView NewView(int providerId, long owner, string name) => new() { ProviderId = providerId, OwnerPortalUserId = owner, Name = name };

    [Fact]
    public async Task A_new_view_gets_an_empty_layout_version_one_and_trigger_timestamps()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SavedView view = NewView(1001, 1, "Defaults");

        db.SavedViews.Add(view);
        await db.SaveChangesAsync(ct);
        await db.Entry(view).ReloadAsync(ct);

        Assert.Equal("{}", view.Layout);
        Assert.Equal(1, view.LayoutSchemaVersion);
        Assert.NotEqual(default, view.CreatedAtTimestamp);
    }

    [Fact]
    public async Task Saving_from_a_stale_copy_is_refused_and_created_at_never_changes()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext first = postgres.NewContext();
        SavedView view = NewView(1002, 1, "Concurrent");
        first.SavedViews.Add(view);
        await first.SaveChangesAsync(ct);

        await using PrismDbContext second = postgres.NewContext();
        SavedView stale = await second.SavedViews.SingleAsync(v => v.Id == view.Id, ct);
        DateTimeOffset created = stale.CreatedAtTimestamp;

        view.Name = "Renamed by the first editor";
        await first.SaveChangesAsync(ct);

        stale.Name = "Renamed by the second editor";
        stale.CreatedAtTimestamp = created.AddYears(-5);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(ct));

        await using PrismDbContext check = postgres.NewContext();
        SavedView stored = await check.SavedViews.SingleAsync(v => v.Id == view.Id, ct);
        Assert.Equal("Renamed by the first editor", stored.Name);
        Assert.Equal(created, stored.CreatedAtTimestamp);
    }

    [Fact]
    public async Task An_owner_cannot_have_two_views_with_the_same_name_in_a_provider_but_can_in_another()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        db.SavedViews.Add(NewView(1003, 5, "Same name"));
        await db.SaveChangesAsync(ct);

        db.SavedViews.Add(NewView(1003, 5, "Same name"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        db.SavedViews.Add(NewView(1004, 5, "Same name"));
        db.SavedViews.Add(NewView(1003, 6, "Same name"));
        await db.SaveChangesAsync(ct);
    }

    [Fact]
    public async Task A_view_with_shares_or_invitations_cannot_be_deleted_underneath_them()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SavedView view = NewView(1005, 1, "Shared");
        db.SavedViews.Add(view);
        await db.SaveChangesAsync(ct);
        db.SavedViewShares.Add(new SavedViewShare { SavedViewId = view.Id, SharedWithPortalUserId = 9 });
        await db.SaveChangesAsync(ct);

        Npgsql.PostgresException error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM saved_view WHERE id = {0}", [view.Id], ct));
        Assert.Equal(Npgsql.PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task A_user_is_shared_a_view_once_and_an_invitation_token_hash_is_unique()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SavedView view = NewView(1006, 1, "Invites");
        db.SavedViews.Add(view);
        await db.SaveChangesAsync(ct);
        db.SavedViewShares.Add(new SavedViewShare { SavedViewId = view.Id, SharedWithPortalUserId = 9 });
        await db.SaveChangesAsync(ct);

        db.SavedViewShares.Add(new SavedViewShare { SavedViewId = view.Id, SharedWithPortalUserId = 9 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        db.ChangeTracker.Clear();

        string hash = new('a', 64);
        db.SavedViewInvitations.Add(NewInvitation(view, hash));
        await db.SaveChangesAsync(ct);
        db.SavedViewInvitations.Add(NewInvitation(view, hash));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }

    private static SavedViewInvitation NewInvitation(SavedView view, string tokenHash) => new()
    {
        SavedViewId = view.Id,
        ProviderId = view.ProviderId,
        InvitedEmail = "someone@example.org",
        TokenHash = tokenHash,
        ExpiresAtTimestamp = new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero),
        CreatedByPortalUserId = 1,
    };
}
