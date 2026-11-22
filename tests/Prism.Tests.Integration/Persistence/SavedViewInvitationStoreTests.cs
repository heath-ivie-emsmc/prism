using Microsoft.EntityFrameworkCore;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;
using Prism.Infrastructure.SavedViews;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Persistence;

/// <summary>Invitations against real PostgreSQL: single use, expiry, revocation, replacement, the caps, and the race between two people using one link.</summary>
public sealed class SavedViewInvitationStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const long Owner = 21;
    private const long Guest = 22;
    private const long Other = 23;
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private static SavedViewActor As(int provider, long user) => new(provider, user, $"user{user}");

    private async Task<int> NewViewAsync(int provider)
    {
        await using PrismDbContext db = postgres.NewContext();
        var view = new SavedView { ProviderId = provider, OwnerPortalUserId = Owner, Name = $"view-{provider}" };
        db.SavedViews.Add(view);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return view.Id;
    }

    private static SavedViewInvitationStore Store(PrismDbContext db, TestTimeProvider? clock = null) => new(db, clock ?? new TestTimeProvider(Start));

    private async Task<string> InviteAsync(int provider, int viewId, string email = "guest@example.org", TestTimeProvider? clock = null)
    {
        await using PrismDbContext db = postgres.NewContext();
        InvitationCreateResult result = await Store(db, clock).CreateAsync(As(provider, Owner), viewId, [email], Week, TestContext.Current.CancellationToken);
        Assert.Equal(InvitationCreateStatus.Created, result.Status);
        return result.Invitations.Single().Token;
    }

    [Fact]
    public async Task Only_a_hash_of_the_token_is_stored()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3001);

        string token = await InviteAsync(3001, view);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitation row = await db.SavedViewInvitations.SingleAsync(i => i.SavedViewId == view, ct);
        Assert.Equal(InvitationTokens.Hash(token), row.TokenHash);
        Assert.DoesNotContain(token, row.TokenHash + row.InvitedEmail, StringComparison.Ordinal);
        Assert.Equal(Start.Add(Week), row.ExpiresAtTimestamp);
    }

    [Fact]
    public async Task A_token_works_once_gives_access_and_a_second_use_gets_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3002);
        string token = await InviteAsync(3002, view);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);
        InvitationTarget? first = await store.RedeemAsync(token, Guest, ct);
        InvitationTarget? replay = await store.RedeemAsync(token, Guest, ct);
        InvitationTarget? replayByOther = await store.RedeemAsync(token, Other, ct);

        Assert.Equal(new InvitationTarget(3002, view), first);
        Assert.Null(replay);
        Assert.Null(replayByOther);
        Assert.Null(await store.FindTargetAsync(token, ct));
        await using PrismDbContext check = postgres.NewContext();
        Assert.Equal([Guest], await check.SavedViewShares.Where(s => s.SavedViewId == view).Select(s => s.SharedWithPortalUserId).ToListAsync(ct));
        Assert.NotNull(await new SavedViewStore(check).GetAsync(As(3002, Guest), view, ct));
        Assert.Null(await new SavedViewStore(check).GetAsync(As(3002, Other), view, ct));
    }

    [Fact]
    public async Task Two_people_using_one_link_at_the_same_moment_only_one_gets_in()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3003);
        string token = await InviteAsync(3003, view);

        InvitationTarget?[] results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async i =>
        {
            await using PrismDbContext db = postgres.NewContext();
            return await Store(db).RedeemAsync(token, 100 + i, ct);
        }));

        Assert.Single(results, r => r is not null);
        await using PrismDbContext check = postgres.NewContext();
        Assert.Equal(1, await check.SavedViewShares.CountAsync(s => s.SavedViewId == view, ct));
    }

    [Fact]
    public async Task An_expired_token_is_dead_and_so_is_one_a_second_before_it_expires_not()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3004);
        var clock = new TestTimeProvider(Start);
        string token = await InviteAsync(3004, view, clock: clock);

        clock.Advance(Week - TimeSpan.FromSeconds(1));
        await using (PrismDbContext db = postgres.NewContext())
        {
            Assert.NotNull(await Store(db, clock).FindTargetAsync(token, ct));
        }

        clock.Advance(TimeSpan.FromSeconds(1));
        await using (PrismDbContext db = postgres.NewContext())
        {
            Assert.Null(await Store(db, clock).FindTargetAsync(token, ct));
            Assert.Null(await Store(db, clock).RedeemAsync(token, Guest, ct));
        }
    }

    [Fact]
    public async Task A_revoked_invitation_cannot_be_used_and_only_the_owner_can_revoke()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3005);
        string token = await InviteAsync(3005, view);
        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);
        int invitationId = await db.SavedViewInvitations.Where(i => i.SavedViewId == view).Select(i => i.Id).SingleAsync(ct);

        Assert.False(await store.RevokeInvitationAsync(As(3005, Other), view, invitationId, ct));
        Assert.False(await store.RevokeInvitationAsync(As(3990, Owner), view, invitationId, ct));
        Assert.NotNull(await store.FindTargetAsync(token, ct));
        Assert.True(await store.RevokeInvitationAsync(As(3005, Owner), view, invitationId, ct));

        Assert.Null(await store.RedeemAsync(token, Guest, ct));
    }

    [Fact]
    public async Task Inviting_the_same_address_again_kills_the_old_link_and_the_new_one_works()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3006);
        string oldToken = await InviteAsync(3006, view);
        string newToken = await InviteAsync(3006, view);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);

        Assert.Null(await store.RedeemAsync(oldToken, Guest, ct));
        Assert.NotNull(await store.RedeemAsync(newToken, Guest, ct));
    }

    [Fact]
    public async Task The_owner_opening_their_own_link_gets_no_share_and_leaves_it_for_the_guest()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3007);
        string token = await InviteAsync(3007, view);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);

        Assert.NotNull(await store.RedeemAsync(token, Owner, ct));
        Assert.Equal(0, await db.SavedViewShares.CountAsync(s => s.SavedViewId == view, ct));
        Assert.NotNull(await store.RedeemAsync(token, Guest, ct));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789012")]
    [InlineData("0123456789012345678901234567890123456789012345")]
    [InlineData("0123456789012345678901234567890123456789 12")]
    public async Task A_made_up_or_malformed_token_gets_nothing_and_never_reaches_the_database(string token)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);

        Assert.Null(await store.FindTargetAsync(token, ct));
        Assert.Null(await store.RedeemAsync(token, Guest, ct));
    }

    [Fact]
    public async Task A_token_for_a_view_that_was_deleted_is_dead()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3008);
        string token = await InviteAsync(3008, view);

        await using PrismDbContext db = postgres.NewContext();
        Assert.True(await new SavedViewStore(db).DeleteAsync(As(3008, Owner), view, ct));

        Assert.Null(await Store(db).RedeemAsync(token, Guest, ct));
    }

    [Fact]
    public async Task Old_unused_invitations_are_purged_when_new_ones_are_made_and_redeemed_ones_are_kept()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3013);
        var clock = new TestTimeProvider(Start);
        string redeemed = await InviteAsync(3013, view, "kept@example.org", clock);
        await InviteAsync(3013, view, "gone@example.org", clock);
        await using (PrismDbContext db = postgres.NewContext())
        {
            await Store(db, clock).RedeemAsync(redeemed, Guest, ct);
        }

        clock.Advance(Week + SavedViewInvitationStore.PurgeAfter + TimeSpan.FromDays(1));
        await InviteAsync(3013, view, "fresh@example.org", clock);

        await using PrismDbContext check = postgres.NewContext();
        string[] remaining = [.. (await check.SavedViewInvitations.Where(i => i.SavedViewId == view).Select(i => i.InvitedEmail).ToListAsync(ct)).Order(StringComparer.Ordinal)];
        Assert.Equal(["fresh@example.org", "kept@example.org"], remaining);
    }

    [Fact]
    public async Task Only_the_owner_of_a_view_in_this_provider_can_invite_or_see_who_has_access()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3009);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);

        Assert.Equal(InvitationCreateStatus.NotFound, (await store.CreateAsync(As(3009, Other), view, ["a@example.org"], Week, ct)).Status);
        Assert.Equal(InvitationCreateStatus.NotFound, (await store.CreateAsync(As(3991, Owner), view, ["a@example.org"], Week, ct)).Status);
        Assert.Null(await store.ListSharingAsync(As(3009, Other), view, ct));
        Assert.Null(await store.ListSharingAsync(As(3991, Owner), view, ct));
    }

    [Fact]
    public async Task The_owner_sees_pending_invitations_and_recipients_and_can_remove_a_recipient()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3010);
        string used = await InviteAsync(3010, view, "used@example.org");
        await InviteAsync(3010, view, "waiting@example.org");
        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);
        await store.RedeemAsync(used, Guest, ct);

        SavedViewSharing sharing = (await store.ListSharingAsync(As(3010, Owner), view, ct))!;

        Assert.Equal("used@example.org", sharing.Recipients.Single().Email);
        Assert.Equal("waiting@example.org", sharing.Pending.Single().Email);
        Assert.False(sharing.Pending.Single().IsExpired);
        Assert.False(await store.RevokeShareAsync(As(3010, Other), view, sharing.Recipients.Single().ShareId, ct));
        Assert.True(await store.RevokeShareAsync(As(3010, Owner), view, sharing.Recipients.Single().ShareId, ct));
        Assert.Null(await new SavedViewStore(db).GetAsync(As(3010, Guest), view, ct));
    }

    [Fact]
    public async Task A_view_cannot_have_more_recipients_and_pending_invitations_than_the_cap()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        int view = await NewViewAsync(3011);
        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db);
        db.SavedViewShares.AddRange(Enumerable.Range(0, SavedViewLimits.MaxRecipientsPerView).Select(i => new SavedViewShare { SavedViewId = view, SharedWithPortalUserId = 1000 + i }));
        await db.SaveChangesAsync(ct);

        InvitationCreateResult result = await store.CreateAsync(As(3011, Owner), view, ["one-more@example.org"], Week, ct);

        Assert.Equal(InvitationCreateStatus.TooManyRecipients, result.Status);
        Assert.Empty(result.Invitations);
    }

    [Fact]
    public async Task An_owner_cannot_send_more_than_the_hourly_cap_and_the_window_moves_with_the_clock()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        // The cap counts per owner across all views, so this owner is unique to the test.
        const long busyOwner = 9001;
        await using PrismDbContext seed = postgres.NewContext();
        var view = new SavedView { ProviderId = 3012, OwnerPortalUserId = busyOwner, Name = "busy" };
        seed.SavedViews.Add(view);
        await seed.SaveChangesAsync(ct);
        var clock = new TestTimeProvider(DateTimeOffset.UtcNow);
        await using PrismDbContext db = postgres.NewContext();
        SavedViewInvitationStore store = Store(db, clock);
        SavedViewActor owner = As(3012, busyOwner);
        string[] emails = [.. Enumerable.Range(0, SavedViewLimits.MaxInvitationsPerOwnerPerHour).Select(i => $"p{i}@example.org")];

        // Ten at a time, the most one request may carry.
        foreach (string[] batch in emails.Chunk(10))
        {
            Assert.Equal(InvitationCreateStatus.Created, (await store.CreateAsync(owner, view.Id, batch, Week, ct)).Status);
        }

        Assert.Equal(InvitationCreateStatus.TooManyInvitations, (await store.CreateAsync(owner, view.Id, ["late@example.org"], Week, ct)).Status);

        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(InvitationCreateStatus.Created, (await store.CreateAsync(owner, view.Id, ["late@example.org"], Week, ct)).Status);
    }
}
