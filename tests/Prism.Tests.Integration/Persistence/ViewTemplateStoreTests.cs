using Microsoft.EntityFrameworkCore;
using Npgsql;
using Prism.Application.Admin;
using Prism.Application.SavedViews;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;
using Prism.Infrastructure.SavedViews;
using Prism.Testing.Common;

namespace Prism.Tests.Integration.Persistence;

/// <summary>View Templates, Assignments and delivery against real PostgreSQL.</summary>
public sealed class ViewTemplateStoreTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly AdminActor Admin = new(900, "admin");
    private static int counter;

    private static string Unique(string prefix) => $"{prefix}-{Interlocked.Increment(ref counter)}-{Guid.NewGuid():N}"[..30];

    private static SavedViewActor User(int provider, long user) => new(provider, user, $"user{user}");

    private static ViewTemplateContent Content(string name, int widgets = 2) =>
        new(name, "A starting layout", new SavedViewLayout(1, "comfortable", [.. Enumerable.Range(0, widgets).Select(i =>
            new LayoutWidget($"w{i}", "net_collections", "line", new Dictionary<string, string> { ["months"] = "6" }, 0, i * 3, 6, 3))]));

    private async Task<ViewTemplateDetail> CreateAsync(string? name = null, bool publish = false, int widgets = 2)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using PrismDbContext db = postgres.NewContext();
        ViewTemplateWriteResult created = await new ViewTemplateStore(db).CreateAsync(Admin, Content(name ?? Unique("t"), widgets), ct);
        Assert.Equal(ViewTemplateWriteStatus.Saved, created.Status);
        if (!publish)
        {
            return created.Template!;
        }

        await using PrismDbContext db2 = postgres.NewContext();
        return (await new ViewTemplateStore(db2).SetPublishedAsync(Admin, created.Template!.Id, true, ct)).Template!;
    }

    private async Task<AssignmentSummary> PushAsync(ViewTemplateDetail template, AssignmentScope scope)
    {
        await using PrismDbContext db = postgres.NewContext();
        AssignmentWriteResult result = await new TemplateAssignmentStore(db, TimeProvider.System)
            .CreateAsync(Admin, template.Id, scope, template.Layout.Widgets!.Count, TestContext.Current.CancellationToken);
        Assert.Equal(AssignmentWriteStatus.Saved, result.Status);
        return result.Assignment!;
    }

    /// <summary>An everyone push reaches every user in every Provider, and the tests share one database, so a test that makes one revokes it when it is done.</summary>
    private async Task RevokeAsync(AssignmentSummary assignment)
    {
        await using PrismDbContext db = postgres.NewContext();
        await new TemplateAssignmentStore(db, TimeProvider.System).RevokeAsync(Admin, assignment.Id, TestContext.Current.CancellationToken);
    }

    private async Task<int> DeliverAsync(SavedViewActor actor)
    {
        await using PrismDbContext db = postgres.NewContext();
        return await new TemplateDeliveryService(db).DeliverPendingAsync(actor, TestContext.Current.CancellationToken);
    }

    private async Task<List<SavedView>> ViewsOfAsync(SavedViewActor actor)
    {
        await using PrismDbContext db = postgres.NewContext();
        return await db.SavedViews.AsNoTracking().Where(v => v.ProviderId == actor.ProviderId && v.OwnerPortalUserId == actor.PortalUserId).ToListAsync(TestContext.Current.CancellationToken);
    }

    private static AssignmentScope ProviderScope(int id) => new(AssignmentScopeKinds.Provider, id);

    private static AssignmentScope Everyone() => new(AssignmentScopeKinds.All, null);

    [Fact]
    public async Task Creating_a_template_writes_an_audit_row_with_codes_and_counts_only()
    {
        ViewTemplateDetail template = await CreateAsync(widgets: 3);

        await using PrismDbContext db = postgres.NewContext();
        AdminAuditEntry entry = await db.AdminAuditEntries.AsNoTracking().SingleAsync(e => e.ObjectId == template.Id && e.Action == AdminAuditActions.TemplateCreated, TestContext.Current.CancellationToken);
        Assert.Equal(Admin.PortalUserId, entry.ActorPortalUserId);
        Assert.Equal(AdminAuditObjects.ViewTemplate, entry.ObjectType);
        Assert.Equal(3, entry.AffectedCount);
        Assert.Null(entry.ProviderId);
    }

    [Fact]
    public async Task A_duplicate_name_is_refused_and_leaves_no_audit_row()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        string name = Unique("dup");
        await CreateAsync(name);

        await using PrismDbContext db = postgres.NewContext();
        ViewTemplateWriteResult second = await new ViewTemplateStore(db).CreateAsync(Admin, Content(name), ct);

        Assert.Equal(ViewTemplateWriteStatus.NameTaken, second.Status);
        await using PrismDbContext check = postgres.NewContext();
        // One template, and one creation audit row for it: the refused attempt added neither.
        ViewTemplate stored = await check.ViewTemplates.AsNoTracking().SingleAsync(t => t.Name == name, ct);
        Assert.Equal(1, await check.AdminAuditEntries.CountAsync(e => e.Action == AdminAuditActions.TemplateCreated && e.ObjectId == stored.Id, ct));
        Assert.Equal(1, await check.ViewTemplates.CountAsync(t => t.Name == name, ct));
    }

    [Fact]
    public async Task A_save_from_a_stale_version_changes_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync();

        await using PrismDbContext first = postgres.NewContext();
        ViewTemplateWriteResult saved = await new ViewTemplateStore(first).UpdateAsync(Admin, template.Id, template.Version, Content(template.Name + "-v2"), ct);
        await using PrismDbContext second = postgres.NewContext();
        ViewTemplateWriteResult stale = await new ViewTemplateStore(second).UpdateAsync(Admin, template.Id, template.Version, Content("stale"), ct);

        Assert.Equal(ViewTemplateWriteStatus.Saved, saved.Status);
        Assert.Equal(ViewTemplateWriteStatus.Conflict, stale.Status);
        await using PrismDbContext check = postgres.NewContext();
        Assert.Equal(template.Name + "-v2", (await check.ViewTemplates.AsNoTracking().SingleAsync(t => t.Id == template.Id, ct)).Name);
    }

    [Fact]
    public async Task Only_a_published_template_can_be_pushed_and_the_confirmation_must_match()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail draft = await CreateAsync(widgets: 2);
        ViewTemplateDetail published = await CreateAsync(publish: true, widgets: 2);

        await using PrismDbContext db = postgres.NewContext();
        var store = new TemplateAssignmentStore(db, TimeProvider.System);
        Assert.Equal(AssignmentWriteStatus.NotPublished, (await store.CreateAsync(Admin, draft.Id, Everyone(), 2, ct)).Status);
        Assert.Equal(AssignmentWriteStatus.StaleConfirmation, (await store.CreateAsync(Admin, published.Id, Everyone(), 5, ct)).Status);
        Assert.Equal(AssignmentWriteStatus.TemplateNotFound, (await store.CreateAsync(Admin, int.MaxValue, Everyone(), 2, ct)).Status);
    }

    [Fact]
    public async Task A_push_audit_row_names_the_provider_and_the_widget_count()
    {
        ViewTemplateDetail template = await CreateAsync(publish: true, widgets: 4);

        AssignmentSummary assignment = await PushAsync(template, ProviderScope(3101));

        await using PrismDbContext db = postgres.NewContext();
        AdminAuditEntry entry = await db.AdminAuditEntries.AsNoTracking().SingleAsync(e => e.ObjectId == assignment.Id && e.Action == AdminAuditActions.AssignmentCreated, TestContext.Current.CancellationToken);
        Assert.Equal(3101, entry.ProviderId);
        Assert.Equal(4, entry.AffectedCount);
    }

    [Fact]
    public async Task A_provider_push_is_delivered_once_to_each_user_in_that_provider_and_to_nobody_elsewhere()
    {
        ViewTemplateDetail template = await CreateAsync(publish: true);
        await PushAsync(template, ProviderScope(3201));

        Assert.Equal(1, await DeliverAsync(User(3201, 41)));
        Assert.Equal(0, await DeliverAsync(User(3201, 41)));
        Assert.Equal(1, await DeliverAsync(User(3201, 42)));
        Assert.Equal(0, await DeliverAsync(User(3202, 41)));

        List<SavedView> mine = await ViewsOfAsync(User(3201, 41));
        SavedView copy = Assert.Single(mine);
        Assert.Equal(template.Name, copy.Name);
        Assert.NotNull(copy.SourceTemplateAssignmentId);
        Assert.Equal(TemplateDeliveryService.DeliveredBy, copy.CreatedByUserName);
        Assert.Empty(await ViewsOfAsync(User(3202, 41)));
    }

    [Fact]
    public async Task A_push_to_everyone_reaches_a_user_in_any_provider()
    {
        ViewTemplateDetail template = await CreateAsync(publish: true);
        AssignmentSummary assignment = await PushAsync(template, Everyone());
        try
        {
            Assert.Equal(1, await DeliverAsync(User(3301, 51)));
            Assert.Equal(1, await DeliverAsync(User(3302, 51)));
        }
        finally
        {
            await RevokeAsync(assignment);
        }
    }

    [Fact]
    public async Task A_revoked_push_is_not_delivered_but_copies_already_delivered_stay()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync(publish: true);
        AssignmentSummary assignment = await PushAsync(template, ProviderScope(3401));
        await DeliverAsync(User(3401, 61));

        await using PrismDbContext db = postgres.NewContext();
        AssignmentWriteResult revoked = await new TemplateAssignmentStore(db, TimeProvider.System).RevokeAsync(Admin, assignment.Id, ct);
        AssignmentWriteResult again = await new TemplateAssignmentStore(db, TimeProvider.System).RevokeAsync(Admin, assignment.Id, ct);

        Assert.NotNull(revoked.Assignment!.RevokedAt);
        Assert.Equal(1, revoked.Assignment.DeliveredCount);
        Assert.Equal(AssignmentWriteStatus.Saved, again.Status);
        Assert.Equal(0, await DeliverAsync(User(3401, 62)));
        Assert.Single(await ViewsOfAsync(User(3401, 61)));
        await using PrismDbContext check = postgres.NewContext();
        Assert.Equal(1, await check.AdminAuditEntries.CountAsync(e => e.ObjectId == assignment.Id && e.Action == AdminAuditActions.AssignmentRevoked, ct));
    }

    [Fact]
    public async Task A_copy_the_user_deleted_is_not_delivered_again()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync(publish: true);
        await PushAsync(template, ProviderScope(3501));
        SavedViewActor user = User(3501, 71);
        await DeliverAsync(user);
        SavedView copy = Assert.Single(await ViewsOfAsync(user));

        await using (PrismDbContext db = postgres.NewContext())
        {
            Assert.True(await new SavedViewStore(db).DeleteAsync(user, copy.Id, ct));
        }

        Assert.Equal(0, await DeliverAsync(user));
        Assert.Empty(await ViewsOfAsync(user));
        await using PrismDbContext check = postgres.NewContext();
        Assert.Null((await check.TemplateDeliveries.AsNoTracking().SingleAsync(d => d.PortalUserId == 71 && d.ProviderId == 3501, ct)).SavedViewId);
    }

    [Fact]
    public async Task Editing_or_unpublishing_the_template_after_a_push_does_not_change_what_is_delivered()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync(publish: true, widgets: 2);
        await PushAsync(template, ProviderScope(3601));

        await using (PrismDbContext db = postgres.NewContext())
        {
            ViewTemplateDetail current = (await new ViewTemplateStore(db).GetAsync(template.Id, ct))!;
            await new ViewTemplateStore(db).UpdateAsync(Admin, template.Id, current.Version, Content(template.Name + "-edited", 5), ct);
            await using PrismDbContext db2 = postgres.NewContext();
            await new ViewTemplateStore(db2).SetPublishedAsync(Admin, template.Id, false, ct);
        }

        await DeliverAsync(User(3601, 81));

        SavedView copy = Assert.Single(await ViewsOfAsync(User(3601, 81)));
        Assert.Equal(template.Name, copy.Name);
        Assert.Contains("\"w1\"", copy.Layout, StringComparison.Ordinal);
        Assert.DoesNotContain("\"w4\"", copy.Layout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_name_the_user_already_has_gets_a_number_and_their_own_view_is_untouched()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync(publish: true);
        await PushAsync(template, ProviderScope(3701));
        SavedViewActor user = User(3701, 91);
        await using (PrismDbContext db = postgres.NewContext())
        {
            await new SavedViewStore(db).CreateAsync(user, new SavedViewContent(template.Name, "mine", Content("x").Layout), ct);
        }

        await DeliverAsync(user);

        List<SavedView> views = await ViewsOfAsync(user);
        Assert.Equal(2, views.Count);
        Assert.Contains(views, v => v.Name == template.Name && v.Description == "mine");
        Assert.Contains(views, v => v.Name == $"{template.Name} (2)");
    }

    [Fact]
    public async Task Two_opens_at_once_deliver_one_copy()
    {
        ViewTemplateDetail template = await CreateAsync(publish: true);
        await PushAsync(template, ProviderScope(3801));
        SavedViewActor user = User(3801, 101);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DeliverAsync(user)));

        Assert.Single(await ViewsOfAsync(user));
    }

    [Fact]
    public async Task A_template_that_has_been_pushed_cannot_be_deleted_but_an_unpushed_one_can()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail pushed = await CreateAsync(publish: true);
        AssignmentSummary assignment = await PushAsync(pushed, Everyone());
        await RevokeAsync(assignment);
        ViewTemplateDetail unpushed = await CreateAsync();

        await using PrismDbContext db = postgres.NewContext();
        var store = new ViewTemplateStore(db);
        Assert.Equal(ViewTemplateWriteStatus.HasAssignments, (await store.DeleteAsync(Admin, pushed.Id, ct)).Status);
        Assert.Equal(ViewTemplateWriteStatus.Saved, (await store.DeleteAsync(Admin, unpushed.Id, ct)).Status);
        Assert.Equal(ViewTemplateWriteStatus.NotFound, (await store.DeleteAsync(Admin, unpushed.Id, ct)).Status);
    }

    [Fact]
    public async Task The_admin_audit_trail_cannot_be_changed_or_removed()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await CreateAsync();

        await using PrismDbContext db = postgres.NewContext();
        NpgsqlException update = await Assert.ThrowsAnyAsync<NpgsqlException>(() => db.Database.ExecuteSqlRawAsync("UPDATE admin_audit_entry SET action = 'x'", ct));
        NpgsqlException delete = await Assert.ThrowsAnyAsync<NpgsqlException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM admin_audit_entry", ct));
        NpgsqlException truncate = await Assert.ThrowsAnyAsync<NpgsqlException>(() => db.Database.ExecuteSqlRawAsync("TRUNCATE admin_audit_entry", ct));

        Assert.All([update, delete, truncate], ex => Assert.Equal("23001", ((PostgresException)ex).SqlState));
    }

    [Fact]
    public async Task A_provider_assignment_needs_a_provider_and_an_everyone_assignment_must_not_have_one()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        ViewTemplateDetail template = await CreateAsync(publish: true);

        await using PrismDbContext db = postgres.NewContext();
        db.TemplateAssignments.Add(new TemplateAssignment { ViewTemplateId = template.Id, ScopeKind = "Provider", ProviderId = null, Name = "x", CreatedByPortalUserId = 1 });

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
    }
}
