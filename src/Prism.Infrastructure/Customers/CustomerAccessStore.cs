using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Prism.Application.Admin;
using Prism.Application.Customers;
using Prism.Infrastructure.Admin;
using Prism.Infrastructure.Persistence;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Customers;

/// <summary>
/// Customer Roles in Prism's own database: who holds which role in which Provider, and the per-Provider settings. Every change and its audit record are
/// saved in one transaction, so a change that cannot be audited does not happen. A user's Provider always comes from server code, never a request body.
/// </summary>
public sealed class CustomerAccessStore(PrismDbContext db, CustomerRoleCache cache, TimeProvider clock) : ICustomerAccess, ICustomerUserAdmin
{
    private static readonly TimeSpan SignInNoteInterval = TimeSpan.FromMinutes(15);

    public async Task<IReadOnlySet<string>> PermissionsAsync(int providerId, long portalUserId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(providerId, portalUserId, out IReadOnlySet<string> held))
        {
            return held;
        }

        string? roleKey = await db.ProviderUsers.AsNoTracking()
            .Where(u => u.ProviderId == providerId && u.PortalUserId == portalUserId)
            .Select(u => u.RoleKey)
            .FirstOrDefaultAsync(cancellationToken);
        IReadOnlySet<string> permissions = CustomerRoles.PermissionsOf(roleKey);
        cache.Set(providerId, portalUserId, permissions);
        return permissions;
    }

    public async Task<IReadOnlySet<string>> PermissionsInAnyProviderAsync(long portalUserId, CancellationToken cancellationToken)
    {
        if (cache.TryGet(CustomerRoleCache.AnyProvider, portalUserId, out IReadOnlySet<string> held))
        {
            return held;
        }

        List<string> roleKeys = await db.ProviderUsers.AsNoTracking()
            .Where(u => u.PortalUserId == portalUserId)
            .Select(u => u.RoleKey)
            .Take(CustomerAccessLimits.MaxProvidersRecorded)
            .ToListAsync(cancellationToken);
        HashSet<string> permissions = [.. roleKeys.SelectMany(key => CustomerRoles.PermissionsOf(key))];
        cache.Set(CustomerRoleCache.AnyProvider, portalUserId, permissions);
        return permissions;
    }

    public async Task<IReadOnlyDictionary<int, IReadOnlySet<string>>> RecordAsync(
        long portalUserId,
        string displayName,
        IReadOnlyList<int> providerIds,
        CancellationToken cancellationToken)
    {
        int[] wanted = [.. providerIds.Distinct().Take(CustomerAccessLimits.MaxProvidersRecorded)];
        string name = displayName.Length > CustomerAccessLimits.MaxDisplayNameLength ? displayName[..CustomerAccessLimits.MaxDisplayNameLength] : displayName;
        DateTimeOffset now = clock.GetUtcNow();

        List<ProviderUser> rows = await db.ProviderUsers.Where(u => u.PortalUserId == portalUserId && wanted.Contains(u.ProviderId)).ToListAsync(cancellationToken);
        bool changed = false;
        foreach (ProviderUser row in rows.Where(r => r.DisplayName != name || now - r.LastSignInTimestamp > SignInNoteInterval))
        {
            row.DisplayName = name;
            row.LastSignInTimestamp = now;
            changed = true;
        }

        int[] missing = [.. wanted.Except(rows.Select(r => r.ProviderId))];
        if (missing.Length > 0)
        {
            Dictionary<int, string> defaults = await db.ProviderSettings.AsNoTracking()
                .Where(s => missing.Contains(s.ProviderId))
                .ToDictionaryAsync(s => s.ProviderId, s => s.DefaultRoleKey, cancellationToken);
            foreach (int providerId in missing)
            {
                string fallback = defaults.GetValueOrDefault(providerId, CustomerRoles.Viewer);
                var row = new ProviderUser
                {
                    ProviderId = providerId,
                    PortalUserId = portalUserId,
                    DisplayName = name,
                    RoleKey = fallback,
                    LastSignInTimestamp = now,
                };
                db.ProviderUsers.Add(row);
                rows.Add(row);
            }

            changed = true;
        }

        if (changed)
        {
            await SaveRecordedAsync(portalUserId, wanted, cancellationToken);
            rows = await db.ProviderUsers.AsNoTracking().Where(u => u.PortalUserId == portalUserId && wanted.Contains(u.ProviderId)).ToListAsync(cancellationToken);
        }

        var result = new Dictionary<int, IReadOnlySet<string>>();
        foreach (ProviderUser row in rows)
        {
            IReadOnlySet<string> permissions = CustomerRoles.PermissionsOf(row.RoleKey);
            result[row.ProviderId] = permissions;
            cache.Set(row.ProviderId, portalUserId, permissions);
        }

        return result;
    }

    private async Task SaveRecordedAsync(long portalUserId, int[] providerIds, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            // Another request recorded the same user first. Their rows are the answer, so drop ours.
            db.ChangeTracker.Clear();
        }

        foreach (int providerId in providerIds)
        {
            cache.Forget(providerId, portalUserId);
        }
    }

    public async Task<ProviderUserPage> ListAsync(int providerId, int page, CancellationToken cancellationToken)
    {
        int skip = Math.Clamp(page, 0, CustomerAccessLimits.MaxPage) * CustomerAccessLimits.PageSize;
        List<ProviderUser> rows = await db.ProviderUsers.AsNoTracking()
            .Where(u => u.ProviderId == providerId)
            .OrderBy(u => u.DisplayName).ThenBy(u => u.PortalUserId)
            .Skip(skip)
            .Take(CustomerAccessLimits.PageSize + 1)
            .ToListAsync(cancellationToken);
        return new ProviderUserPage([.. rows.Take(CustomerAccessLimits.PageSize).Select(Describe)], rows.Count > CustomerAccessLimits.PageSize);
    }

    public async Task<RoleChangeResult> SetRoleAsync(RoleActor actor, int providerId, long targetPortalUserId, string roleKey, CancellationToken cancellationToken)
    {
        if (CustomerRoles.Find(roleKey) is not { } role)
        {
            return new RoleChangeResult(RoleChangeStatus.UnknownRole);
        }

        ProviderUser? target = await db.ProviderUsers.FirstOrDefaultAsync(u => u.ProviderId == providerId && u.PortalUserId == targetPortalUserId, cancellationToken);
        if (target is null)
        {
            return new RoleChangeResult(RoleChangeStatus.NotFound);
        }

        if (!actor.IsStaff && await RefusalForProviderAdminAsync(actor, providerId, target, role, cancellationToken) is { } refusal)
        {
            return new RoleChangeResult(refusal);
        }

        if (target.RoleKey == role.Key)
        {
            return new RoleChangeResult(RoleChangeStatus.Changed, Describe(target));
        }

        string detail = $"{target.RoleKey}>{role.Key}";
        target.RoleKey = role.Key;
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        AdminStoreSupport.AddAudit(db, new AdminActor(actor.PortalUserId, actor.UserName), new AdminAuditWrite(AdminAuditActions.CustomerRoleChanged, AdminAuditObjects.ProviderUser, null, providerId, null, targetPortalUserId, detail));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        cache.Forget(providerId, targetPortalUserId);
        return new RoleChangeResult(RoleChangeStatus.Changed, Describe(target));
    }

    private async Task<RoleChangeStatus?> RefusalForProviderAdminAsync(RoleActor actor, int providerId, ProviderUser target, CustomerRole role, CancellationToken cancellationToken)
    {
        if (target.PortalUserId == actor.PortalUserId)
        {
            return RoleChangeStatus.OwnRole;
        }

        if (role.Restricted && !await AllowsAdminRestrictedGrantsAsync(providerId, cancellationToken))
        {
            return RoleChangeStatus.RestrictedRole;
        }

        CustomerRole? current = CustomerRoles.Find(target.RoleKey);
        if (current is { CanManageUsers: true } && !role.CanManageUsers)
        {
            string[] adminKeys = [.. CustomerRoles.All.Where(r => r.CanManageUsers).Select(r => r.Key)];
            int admins = await db.ProviderUsers.CountAsync(u => u.ProviderId == providerId && adminKeys.Contains(u.RoleKey), cancellationToken);
            if (admins <= 1)
            {
                return RoleChangeStatus.LastAdmin;
            }
        }

        return null;
    }

    public async Task<ProviderRoleSettings> GetSettingsAsync(int providerId, CancellationToken cancellationToken)
    {
        ProviderSetting? setting = await db.ProviderSettings.AsNoTracking().FirstOrDefaultAsync(s => s.ProviderId == providerId, cancellationToken);
        return setting is null ? new ProviderRoleSettings(CustomerRoles.Viewer, false) : new ProviderRoleSettings(setting.DefaultRoleKey, setting.AllowAdminRestrictedGrants);
    }

    public Task<bool> AllowsAdminRestrictedGrantsAsync(int providerId, CancellationToken cancellationToken) =>
        db.ProviderSettings.AsNoTracking().Where(s => s.ProviderId == providerId).Select(s => s.AllowAdminRestrictedGrants).FirstOrDefaultAsync(cancellationToken);

    public async Task<RoleChangeResult> SetSettingsAsync(RoleActor actor, int providerId, ProviderRoleSettings settings, CancellationToken cancellationToken)
    {
        if (!actor.IsStaff || CustomerRoles.Find(settings.DefaultRoleKey) is not { Restricted: false, CanManageUsers: false } role)
        {
            return new RoleChangeResult(RoleChangeStatus.NotAllowed);
        }

        ProviderSetting? setting = await db.ProviderSettings.FirstOrDefaultAsync(s => s.ProviderId == providerId, cancellationToken);
        if (setting is null)
        {
            setting = new ProviderSetting { ProviderId = providerId };
            db.ProviderSettings.Add(setting);
        }

        string detail = $"{setting.DefaultRoleKey}>{role.Key};grants={(settings.AllowAdminRestrictedGrants ? 1 : 0)}";
        setting.DefaultRoleKey = role.Key;
        setting.AllowAdminRestrictedGrants = settings.AllowAdminRestrictedGrants;
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            AdminStoreSupport.AddAudit(db, new AdminActor(actor.PortalUserId, actor.UserName), new AdminAuditWrite(AdminAuditActions.CustomerSettingsChanged, AdminAuditObjects.ProviderSettings, null, providerId, null, null, detail));
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (AdminStoreSupport.IsUniqueViolation(ex))
        {
            // Two staff members created the first setting at once. The other one is stored; ask again.
            db.ChangeTracker.Clear();
            return await SetSettingsAsync(actor, providerId, settings, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new RoleChangeResult(RoleChangeStatus.Changed);
    }

    private static ProviderUserView Describe(ProviderUser u) => new(u.PortalUserId, u.DisplayName, u.RoleKey, u.LastSignInTimestamp);
}
