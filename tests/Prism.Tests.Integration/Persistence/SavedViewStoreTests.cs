using Microsoft.EntityFrameworkCore;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;
using Prism.Infrastructure.SavedViews;

namespace Prism.Tests.Integration.Persistence;

/// <summary>The Saved View store against real PostgreSQL: who can see, change and delete a view, and what a stale save does.</summary>
public sealed class SavedViewStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const long Owner = 11;
    private const long Recipient = 12;
    private const long Stranger = 13;

    private static SavedViewActor As(int provider, long user) => new(provider, user, $"user{user}");

    private static SavedViewContent Content(string name = "Cash", string? description = null) =>
        new(name, description, new SavedViewLayout(1, "comfortable", [new LayoutWidget("w1", "net_collections", "line", new Dictionary<string, string> { ["months"] = "6" }, 0, 0, 6, 3)]));

    private static SavedViewStore Store(PrismDbContext db) => new(db);

    private async Task<SavedViewDetail> CreateAsync(int provider, string name = "Cash", long owner = Owner)
    {
        await using PrismDbContext db = postgres.NewContext();
        SavedViewWriteResult result = await Store(db).CreateAsync(As(provider, owner), Content(name), TestContext.Current.CancellationToken);
        Assert.Equal(SavedViewWriteStatus.Saved, result.Status);
        return result.View!;
    }

    private async Task ShareAsync(SavedViewDetail view, long user)
    {
        await using PrismDbContext db = postgres.NewContext();
        db.SavedViewShares.Add(new SavedViewShare { SavedViewId = view.Id, SharedWithPortalUserId = user });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task The_owner_gets_back_what_they_saved_with_the_owner_name_and_a_version()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2001);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewDetail? read = await Store(db).GetAsync(As(2001, Owner), created.Id, ct);

        Assert.NotNull(read);
        Assert.True(read.IsOwner);
        Assert.Equal("Cash", read.Name);
        Assert.Equal($"user{Owner}", read.OwnerName);
        Assert.Equal("net_collections", read.Layout.Widgets![0].MetricKey);
        Assert.Equal("6", read.Layout.Widgets[0].Options!["months"]);
        Assert.Equal(created.Version, read.Version);
    }

    [Fact]
    public async Task A_stranger_and_a_view_in_another_provider_both_look_like_nothing_is_there()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2002);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewStore store = Store(db);

        Assert.Null(await store.GetAsync(As(2002, Stranger), created.Id, ct));
        Assert.Null(await store.GetAsync(As(2999, Owner), created.Id, ct));
        Assert.Null(await store.GetAsync(As(2002, Owner), created.Id + 1000, ct));
    }

    [Fact]
    public async Task A_recipient_can_read_a_shared_view_but_cannot_change_or_delete_it()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2003);
        await ShareAsync(created, Recipient);

        await using PrismDbContext db = postgres.NewContext();
        SavedViewStore store = Store(db);
        SavedViewDetail? read = await store.GetAsync(As(2003, Recipient), created.Id, ct);
        SavedViewWriteResult update = await store.UpdateAsync(As(2003, Recipient), created.Id, created.Version, Content("Hijacked"), ct);
        bool deleted = await store.DeleteAsync(As(2003, Recipient), created.Id, ct);

        Assert.NotNull(read);
        Assert.False(read.IsOwner);
        Assert.Equal(SavedViewWriteStatus.NotFound, update.Status);
        Assert.False(deleted);
        Assert.Equal("Cash", (await store.GetAsync(As(2003, Owner), created.Id, ct))!.Name);
    }

    [Fact]
    public async Task A_share_does_not_reach_across_providers()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2004);
        await ShareAsync(created, Recipient);

        await using PrismDbContext db = postgres.NewContext();

        Assert.Null(await Store(db).GetAsync(As(2998, Recipient), created.Id, ct));
    }

    [Fact]
    public async Task Saving_from_a_stale_version_is_a_conflict_and_the_current_version_saves()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2005);

        await using PrismDbContext first = postgres.NewContext();
        SavedViewWriteResult saved = await Store(first).UpdateAsync(As(2005, Owner), created.Id, created.Version, Content("First edit"), ct);
        await using PrismDbContext second = postgres.NewContext();
        SavedViewWriteResult stale = await Store(second).UpdateAsync(As(2005, Owner), created.Id, created.Version, Content("Second edit"), ct);

        Assert.Equal(SavedViewWriteStatus.Saved, saved.Status);
        Assert.NotEqual(created.Version, saved.View!.Version);
        Assert.Equal(SavedViewWriteStatus.Conflict, stale.Status);
        await using PrismDbContext check = postgres.NewContext();
        Assert.Equal("First edit", (await Store(check).GetAsync(As(2005, Owner), created.Id, ct))!.Name);
    }

    [Fact]
    public async Task A_name_is_unique_per_owner_and_provider_on_create_and_on_rename()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail first = await CreateAsync(2006, "Same");
        SavedViewDetail other = await CreateAsync(2006, "Other");

        await using PrismDbContext db = postgres.NewContext();
        SavedViewStore store = Store(db);
        SavedViewWriteResult duplicate = await store.CreateAsync(As(2006, Owner), Content("Same"), ct);
        SavedViewWriteResult rename = await store.UpdateAsync(As(2006, Owner), other.Id, other.Version, Content("Same"), ct);
        SavedViewWriteResult differentOwner = await store.CreateAsync(As(2006, Stranger), Content("Same"), ct);
        SavedViewWriteResult differentProvider = await store.CreateAsync(As(2996, Owner), Content("Same"), ct);

        Assert.Equal(SavedViewWriteStatus.NameTaken, duplicate.Status);
        Assert.Equal(SavedViewWriteStatus.NameTaken, rename.Status);
        Assert.Equal(SavedViewWriteStatus.Saved, differentOwner.Status);
        Assert.Equal(SavedViewWriteStatus.Saved, differentProvider.Status);
        Assert.NotEqual(0, first.Id);
    }

    [Fact]
    public async Task An_owner_cannot_keep_more_views_than_the_limit()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        db.SavedViews.AddRange(Enumerable.Range(0, SavedViewLimits.MaxViewsPerOwner).Select(i => new SavedView { ProviderId = 2007, OwnerPortalUserId = Owner, Name = $"v{i}" }));
        await db.SaveChangesAsync(ct);

        SavedViewWriteResult result = await Store(db).CreateAsync(As(2007, Owner), Content("One too many"), ct);

        Assert.Equal(SavedViewWriteStatus.LimitReached, result.Status);
    }

    [Fact]
    public async Task Deleting_removes_the_view_with_its_shares_and_invitations_for_the_owner_only()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2008);
        await ShareAsync(created, Recipient);
        await using (PrismDbContext seed = postgres.NewContext())
        {
            seed.SavedViewInvitations.Add(new SavedViewInvitation
            {
                SavedViewId = created.Id,
                ProviderId = 2008,
                InvitedEmail = "x@example.org",
                TokenHash = InvitationTokens.Hash(InvitationTokens.NewToken()),
                ExpiresAtTimestamp = DateTimeOffset.UtcNow.AddDays(1),
                CreatedByPortalUserId = Owner,
            });
            await seed.SaveChangesAsync(ct);
        }

        await using PrismDbContext db = postgres.NewContext();
        SavedViewStore store = Store(db);

        Assert.False(await store.DeleteAsync(As(2008, Stranger), created.Id, ct));
        Assert.False(await store.DeleteAsync(As(2993, Owner), created.Id, ct));
        Assert.True(await store.DeleteAsync(As(2008, Owner), created.Id, ct));
        Assert.Null(await store.GetAsync(As(2008, Recipient), created.Id, ct));
        Assert.Equal(0, await db.SavedViewShares.CountAsync(s => s.SavedViewId == created.Id, ct));
        Assert.Equal(0, await db.SavedViewInvitations.CountAsync(i => i.SavedViewId == created.Id, ct));
    }

    [Fact]
    public async Task The_lists_separate_mine_from_shared_and_stay_inside_the_provider()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await CreateAsync(2009, "Mine");
        SavedViewDetail theirs = await CreateAsync(2009, "Theirs", Stranger);
        await ShareAsync(theirs, Owner);
        await CreateAsync(2992, "Elsewhere");

        await using PrismDbContext db = postgres.NewContext();
        SavedViewLists lists = await Store(db).ListAsync(As(2009, Owner), ct);

        Assert.Equal(["Mine"], lists.Mine.Select(v => v.Name));
        Assert.Equal(["Theirs"], lists.Shared.Select(v => v.Name));
        Assert.All(lists.Mine, v => Assert.True(v.IsOwner));
        Assert.All(lists.Shared, v => Assert.False(v.IsOwner));
        Assert.Equal(1, lists.Mine[0].WidgetCount);
    }

    [Fact]
    public async Task Only_the_known_layout_properties_are_stored()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        SavedViewDetail created = await CreateAsync(2010);

        await using PrismDbContext db = postgres.NewContext();
        string stored = await db.SavedViews.Where(v => v.Id == created.Id).Select(v => v.Layout).SingleAsync(ct);

        using var json = System.Text.Json.JsonDocument.Parse(stored);
        // jsonb does not keep key order, so compare as a set.
        Assert.Equal(["density", "version", "widgets"], json.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }
}
