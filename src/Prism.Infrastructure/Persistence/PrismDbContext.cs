using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Persistence;

/// <summary>Prism's own writable database. The data warehouse is a separate read-only connection and is never mapped here.</summary>
public sealed class PrismDbContext(DbContextOptions<PrismDbContext> options) : DbContext(options)
{
    public DbSet<RevokedSession> RevokedSessions => Set<RevokedSession>();

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public DbSet<SavedView> SavedViews => Set<SavedView>();

    public DbSet<SavedViewShare> SavedViewShares => Set<SavedViewShare>();

    public DbSet<SavedViewInvitation> SavedViewInvitations => Set<SavedViewInvitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<RevokedSession>(entity =>
        {
            entity.ToTable("revoked_session");
            entity.HasKey(e => e.Id).HasName("pk_revoked_session");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.SessionId).HasColumnName("session_id").HasMaxLength(64).IsRequired();
            entity.Property(e => e.UserName).HasColumnName("user_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.SessionExpiresAtTimestamp).HasColumnName("session_expires_at_timestamp");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Every request looks a session up by id; the purge looks rows up by expiry.
            entity.HasIndex(e => e.SessionId).IsUnique().HasDatabaseName("uk_revoked_session__session_id");
            entity.HasIndex(e => e.SessionExpiresAtTimestamp).HasDatabaseName("ix_revoked_session__session_expires_at_timestamp");
        });

        modelBuilder.Entity<AuditEntry>(entity =>
        {
            entity.ToTable("audit_entry");
            entity.HasKey(e => e.Id).HasName("pk_audit_entry");

            // An audit trail can outgrow int4, so the key is bigint.
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.PortalUserId).HasColumnName("portal_user_id");
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
            entity.Property(e => e.CheckKey).HasColumnName("check_key").HasMaxLength(50).IsRequired();
            entity.Property(e => e.Period).HasColumnName("period").HasMaxLength(6).IsRequired();
            entity.Property(e => e.RowCount).HasColumnName("row_count");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Reviews ask who read a Provider's data over a time range; the Provider is the high-cardinality tenant filter.
            entity.HasIndex(e => new { e.ProviderId, e.CreatedAtTimestamp }).HasDatabaseName("ix_audit_entry__provider_id__created_at_timestamp");
            // And what one user has read.
            entity.HasIndex(e => new { e.PortalUserId, e.CreatedAtTimestamp }).HasDatabaseName("ix_audit_entry__portal_user_id__created_at_timestamp");
        });

        ConfigureSavedViews(modelBuilder);

        EmsmcModelConventions.Apply(modelBuilder);
    }

    private static void ConfigureSavedViews(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SavedView>(entity =>
        {
            entity.ToTable("saved_view");
            entity.HasKey(e => e.Id).HasName("pk_saved_view");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.OwnerPortalUserId).HasColumnName("owner_portal_user_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);
            entity.Property(e => e.Layout).HasColumnName("layout").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            entity.Property(e => e.LayoutSchemaVersion).HasColumnName("layout_schema_version").HasDefaultValue(1);
            // Npgsql maps a uint row version to the xmin system column, which has no column in the migration.
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.Property(e => e.CreatedByUserName).HasColumnName("created_by_user_name").HasMaxLength(200);
            entity.Property(e => e.UpdatedByUserName).HasColumnName("updated_by_user_name").HasMaxLength(200);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // "My views" and the duplicate-name check both look up by Provider and owner. Its prefix also serves the Provider-scoped list.
            entity.HasIndex(e => new { e.ProviderId, e.OwnerPortalUserId, e.Name }).IsUnique()
                .HasDatabaseName("uk_saved_view__provider_id__owner_portal_user_id__name");
        });

        modelBuilder.Entity<SavedViewShare>(entity =>
        {
            entity.ToTable("saved_view_share");
            entity.HasKey(e => e.Id).HasName("pk_saved_view_share");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.SavedViewId).HasColumnName("saved_view_id");
            entity.Property(e => e.SharedWithPortalUserId).HasColumnName("shared_with_portal_user_id");
            entity.Property(e => e.SavedViewInvitationId).HasColumnName("saved_view_invitation_id");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<SavedView>().WithMany().HasForeignKey(e => e.SavedViewId)
                .HasConstraintName("fk_saved_view_share__saved_view_id__saved_view").OnDelete(DeleteBehavior.Restrict);
            // Over 63 characters with the referenced table, so it is shortened per the DBA truncation rules.
            entity.HasOne<SavedViewInvitation>().WithMany().HasForeignKey(e => e.SavedViewInvitationId)
                .HasConstraintName("fk_saved_view_share__saved_view_invitation_id").OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.SavedViewId, e.SharedWithPortalUserId }).IsUnique()
                .HasDatabaseName("uk_saved_view_share__saved_view_id__shared_with_portal_user_id");
            // Supports the foreign key to the invitation.
            entity.HasIndex(e => e.SavedViewInvitationId).HasDatabaseName("ix_saved_view_share__saved_view_invitation_id");
            // "Shared with me" looks views up by the recipient.
            entity.HasIndex(e => e.SharedWithPortalUserId).HasDatabaseName("ix_saved_view_share__shared_with_portal_user_id");
        });

        modelBuilder.Entity<SavedViewInvitation>(entity =>
        {
            entity.ToTable("saved_view_invitation");
            entity.HasKey(e => e.Id).HasName("pk_saved_view_invitation");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.SavedViewId).HasColumnName("saved_view_id");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.InvitedEmail).HasColumnName("invited_email").HasMaxLength(320).IsRequired();
            entity.Property(e => e.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
            entity.Property(e => e.ExpiresAtTimestamp).HasColumnName("expires_at_timestamp");
            entity.Property(e => e.RedeemedAtTimestamp).HasColumnName("redeemed_at_timestamp");
            entity.Property(e => e.RedeemedByPortalUserId).HasColumnName("redeemed_by_portal_user_id");
            entity.Property(e => e.RevokedAtTimestamp).HasColumnName("revoked_at_timestamp");
            entity.Property(e => e.CreatedByPortalUserId).HasColumnName("created_by_portal_user_id");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<SavedView>().WithMany().HasForeignKey(e => e.SavedViewId)
                .HasConstraintName("fk_saved_view_invitation__saved_view_id__saved_view").OnDelete(DeleteBehavior.Restrict);

            // Redemption looks the invitation up by the hash of the token.
            entity.HasIndex(e => e.TokenHash).IsUnique().HasDatabaseName("uk_saved_view_invitation__token_hash");
            // The share dialog lists a view's invitations.
            entity.HasIndex(e => e.SavedViewId).HasDatabaseName("ix_saved_view_invitation__saved_view_id");
            // The cleanup job finds expired rows by expiry.
            entity.HasIndex(e => e.ExpiresAtTimestamp).HasDatabaseName("ix_saved_view_invitation__expires_at_timestamp");
        });
    }
}
