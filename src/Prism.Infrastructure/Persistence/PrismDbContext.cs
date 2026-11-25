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

    public DbSet<AdminAuditEntry> AdminAuditEntries => Set<AdminAuditEntry>();

    public DbSet<ViewTemplate> ViewTemplates => Set<ViewTemplate>();

    public DbSet<TemplateAssignment> TemplateAssignments => Set<TemplateAssignment>();

    public DbSet<TemplateDelivery> TemplateDeliveries => Set<TemplateDelivery>();

    public DbSet<Persona> Personas => Set<Persona>();

    public DbSet<OnboardingQuestion> OnboardingQuestions => Set<OnboardingQuestion>();

    public DbSet<OnboardingOption> OnboardingOptions => Set<OnboardingOption>();

    public DbSet<OnboardingOptionWeight> OnboardingOptionWeights => Set<OnboardingOptionWeight>();

    public DbSet<ProviderUser> ProviderUsers => Set<ProviderUser>();

    public DbSet<ProviderSetting> ProviderSettings => Set<ProviderSetting>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserHomeView> UserHomeViews => Set<UserHomeView>();

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
            entity.Property(e => e.GroupKey).HasColumnName("group_key").HasMaxLength(40);
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
        ConfigureTemplates(modelBuilder);
        ConfigurePersonas(modelBuilder);
        ConfigureCustomerAccess(modelBuilder);

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

            entity.Property(e => e.SourceTemplateAssignmentId).HasColumnName("source_template_assignment_id");
            entity.Property(e => e.IsHidden).HasColumnName("is_hidden").HasDefaultValue(false);
            // Over 63 characters with the referenced table, so it is shortened per the DBA truncation rules.
            entity.HasOne<TemplateAssignment>().WithMany().HasForeignKey(e => e.SourceTemplateAssignmentId)
                .HasConstraintName("fk_saved_view__source_template_assignment_id").OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(e => e.SourceTemplateAssignmentId).HasDatabaseName("ix_saved_view__source_template_assignment_id");

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

    private static void ConfigureTemplates(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AdminAuditEntry>(entity =>
        {
            entity.ToTable("admin_audit_entry");
            entity.HasKey(e => e.Id).HasName("pk_admin_audit_entry");

            // An audit trail can outgrow int4, so the key is bigint.
            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ActorPortalUserId).HasColumnName("actor_portal_user_id");
            entity.Property(e => e.Action).HasColumnName("action").HasMaxLength(50).IsRequired();
            entity.Property(e => e.ObjectType).HasColumnName("object_type").HasMaxLength(40).IsRequired();
            entity.Property(e => e.ObjectId).HasColumnName("object_id");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.AffectedCount).HasColumnName("affected_count");
            entity.Property(e => e.TargetPortalUserId).HasColumnName("target_portal_user_id");
            entity.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(80);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Reviews ask what one admin did over a time range, and what was done to one Provider.
            entity.HasIndex(e => new { e.ActorPortalUserId, e.CreatedAtTimestamp }).HasDatabaseName("ix_admin_audit_entry__plus");
            entity.HasIndex(e => new { e.ProviderId, e.CreatedAtTimestamp }).HasDatabaseName("ix_admin_audit_entry__provider_id__created_at_timestamp");
        });

        modelBuilder.Entity<ViewTemplate>(entity =>
        {
            entity.ToTable("view_template");
            entity.HasKey(e => e.Id).HasName("pk_view_template");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);
            entity.Property(e => e.Layout).HasColumnName("layout").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            entity.Property(e => e.LayoutSchemaVersion).HasColumnName("layout_schema_version").HasDefaultValue(1);
            entity.Property(e => e.IsPublished).HasColumnName("is_published").HasDefaultValue(false);
            entity.Property(e => e.RowVersion).IsRowVersion();
            entity.Property(e => e.CreatedByUserName).HasColumnName("created_by_user_name").HasMaxLength(200);
            entity.Property(e => e.UpdatedByUserName).HasColumnName("updated_by_user_name").HasMaxLength(200);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("uk_view_template__name");
        });

        modelBuilder.Entity<TemplateAssignment>(entity =>
        {
            entity.ToTable("template_assignment", table =>
            {
                table.HasCheckConstraint("ck_template_assignment__scope_kind", "scope_kind IN ('All', 'Provider')");
                table.HasCheckConstraint("ck_template_assignment__provider_id", "(scope_kind = 'Provider') = (provider_id IS NOT NULL)");
            });
            entity.HasKey(e => e.Id).HasName("pk_template_assignment");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ViewTemplateId).HasColumnName("view_template_id");
            entity.Property(e => e.ScopeKind).HasColumnName("scope_kind").HasMaxLength(20).IsRequired();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(300);
            entity.Property(e => e.Layout).HasColumnName("layout").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb").IsRequired();
            entity.Property(e => e.LayoutSchemaVersion).HasColumnName("layout_schema_version").HasDefaultValue(1);
            entity.Property(e => e.RevokedAtTimestamp).HasColumnName("revoked_at_timestamp");
            entity.Property(e => e.CreatedByPortalUserId).HasColumnName("created_by_portal_user_id");
            entity.Property(e => e.CreatedByUserName).HasColumnName("created_by_user_name").HasMaxLength(200);
            entity.Property(e => e.UpdatedByUserName).HasColumnName("updated_by_user_name").HasMaxLength(200);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<ViewTemplate>().WithMany().HasForeignKey(e => e.ViewTemplateId)
                .HasConstraintName("fk_template_assignment__view_template_id__view_template").OnDelete(DeleteBehavior.Restrict);

            // Delivery asks for the open assignments for a Provider, or for everyone.
            entity.HasIndex(e => new { e.ProviderId, e.ScopeKind }).HasDatabaseName("ix_template_assignment__provider_id__scope_kind");
            // The admin screen lists a template's assignments, and the foreign key needs it.
            entity.HasIndex(e => e.ViewTemplateId).HasDatabaseName("ix_template_assignment__view_template_id");
        });

        modelBuilder.Entity<TemplateDelivery>(entity =>
        {
            entity.ToTable("template_delivery");
            entity.HasKey(e => e.Id).HasName("pk_template_delivery");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.TemplateAssignmentId).HasColumnName("template_assignment_id");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.PortalUserId).HasColumnName("portal_user_id");
            entity.Property(e => e.SavedViewId).HasColumnName("saved_view_id");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Over 63 characters with the referenced table, so it is shortened per the DBA truncation rules.
            entity.HasOne<TemplateAssignment>().WithMany().HasForeignKey(e => e.TemplateAssignmentId)
                .HasConstraintName("fk_template_delivery__template_assignment_id").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<SavedView>().WithMany().HasForeignKey(e => e.SavedViewId)
                .HasConstraintName("fk_template_delivery__saved_view_id__saved_view").OnDelete(DeleteBehavior.Restrict);

            // One delivery per assignment, Provider and user. The unique key's columns would make the name too long, so it is shortened.
            entity.HasIndex(e => new { e.PortalUserId, e.ProviderId, e.TemplateAssignmentId }).IsUnique().HasDatabaseName("uk_template_delivery__plus");
            // The foreign key to the clone, and the clear on delete.
            entity.HasIndex(e => e.SavedViewId).HasDatabaseName("ix_template_delivery__saved_view_id");
            // The admin screen counts deliveries per assignment.
            entity.HasIndex(e => e.TemplateAssignmentId).HasDatabaseName("ix_template_delivery__template_assignment_id");
        });
    }

    private static void ConfigurePersonas(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Persona>(entity =>
        {
            entity.ToTable("persona");
            entity.HasKey(e => e.Id).HasName("pk_persona");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
            entity.Property(e => e.ViewTemplateId).HasColumnName("view_template_id");
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsPublished).HasColumnName("is_published").HasDefaultValue(false);
            entity.Property(e => e.CreatedByUserName).HasColumnName("created_by_user_name").HasMaxLength(200);
            entity.Property(e => e.UpdatedByUserName).HasColumnName("updated_by_user_name").HasMaxLength(200);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<ViewTemplate>().WithMany().HasForeignKey(e => e.ViewTemplateId)
                .HasConstraintName("fk_persona__view_template_id__view_template").OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.Name).IsUnique().HasDatabaseName("uk_persona__name");
            // The foreign key to the template, and "is this template a home page" before it is unpublished or deleted.
            entity.HasIndex(e => e.ViewTemplateId).HasDatabaseName("ix_persona__view_template_id");
        });

        modelBuilder.Entity<OnboardingQuestion>(entity =>
        {
            entity.ToTable("onboarding_question");
            entity.HasKey(e => e.Id).HasName("pk_onboarding_question");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.Prompt).HasColumnName("prompt").HasMaxLength(200).IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.IsPublished).HasColumnName("is_published").HasDefaultValue(false);
            entity.Property(e => e.AllowsMultiple).HasColumnName("allows_multiple").HasDefaultValue(false);
            entity.Property(e => e.CreatedByUserName).HasColumnName("created_by_user_name").HasMaxLength(200);
            entity.Property(e => e.UpdatedByUserName).HasColumnName("updated_by_user_name").HasMaxLength(200);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Every first login reads the published questions in order.
            entity.HasIndex(e => new { e.IsPublished, e.SortOrder }).HasDatabaseName("ix_onboarding_question__is_published__sort_order");
        });

        modelBuilder.Entity<OnboardingOption>(entity =>
        {
            entity.ToTable("onboarding_option");
            entity.HasKey(e => e.Id).HasName("pk_onboarding_option");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.OnboardingQuestionId).HasColumnName("onboarding_question_id");
            entity.Property(e => e.Label).HasColumnName("label").HasMaxLength(120).IsRequired();
            entity.Property(e => e.SortOrder).HasColumnName("sort_order");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Over 63 characters with the referenced table, so it is shortened per the DBA truncation rules.
            entity.HasOne<OnboardingQuestion>().WithMany().HasForeignKey(e => e.OnboardingQuestionId)
                .HasConstraintName("fk_onboarding_option__onboarding_question_id").OnDelete(DeleteBehavior.Restrict);

            // A question's options are read together, in order.
            entity.HasIndex(e => new { e.OnboardingQuestionId, e.SortOrder }).HasDatabaseName("ix_onboarding_option__onboarding_question_id__sort_order");
        });

        modelBuilder.Entity<OnboardingOptionWeight>(entity =>
        {
            entity.ToTable("onboarding_option_weight", table =>
                table.HasCheckConstraint("ck_onboarding_option_weight__weight", "weight >= 0 AND weight <= 100"));
            entity.HasKey(e => e.Id).HasName("pk_onboarding_option_weight");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.OnboardingOptionId).HasColumnName("onboarding_option_id");
            entity.Property(e => e.PersonaId).HasColumnName("persona_id");
            entity.Property(e => e.Weight).HasColumnName("weight");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Over 63 characters with the referenced table, so it is shortened per the DBA truncation rules.
            entity.HasOne<OnboardingOption>().WithMany().HasForeignKey(e => e.OnboardingOptionId)
                .HasConstraintName("fk_onboarding_option_weight__onboarding_option_id").OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Persona>().WithMany().HasForeignKey(e => e.PersonaId)
                .HasConstraintName("fk_onboarding_option_weight__persona_id__persona").OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => new { e.OnboardingOptionId, e.PersonaId }).IsUnique()
                .HasDatabaseName("uk_onboarding_option_weight__onboarding_option_id__persona_id");
            // The foreign key to the Persona, and "is this Persona used" before it is deleted.
            entity.HasIndex(e => e.PersonaId).HasDatabaseName("ix_onboarding_option_weight__persona_id");
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.ToTable("user_profile");
            entity.HasKey(e => e.Id).HasName("pk_user_profile");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.PortalUserId).HasColumnName("portal_user_id");
            entity.Property(e => e.PersonaId).HasColumnName("persona_id");
            entity.Property(e => e.OnboardingCompletedAtTimestamp).HasColumnName("onboarding_completed_at_timestamp");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<Persona>().WithMany().HasForeignKey(e => e.PersonaId)
                .HasConstraintName("fk_user_profile__persona_id__persona").OnDelete(DeleteBehavior.Restrict);

            // One profile per user, and every sign-in looks it up by user.
            entity.HasIndex(e => e.PortalUserId).IsUnique().HasDatabaseName("uk_user_profile__portal_user_id");
            // The foreign key to the Persona, and "is this Persona in use" before it is deleted.
            entity.HasIndex(e => e.PersonaId).HasDatabaseName("ix_user_profile__persona_id");
        });

        modelBuilder.Entity<UserHomeView>(entity =>
        {
            entity.ToTable("user_home_view");
            entity.HasKey(e => e.Id).HasName("pk_user_home_view");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.PortalUserId).HasColumnName("portal_user_id");
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.SavedViewId).HasColumnName("saved_view_id");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasOne<SavedView>().WithMany().HasForeignKey(e => e.SavedViewId)
                .HasConstraintName("fk_user_home_view__saved_view_id__saved_view").OnDelete(DeleteBehavior.Restrict);

            // One home page per user and Provider, looked up on every dashboard load.
            entity.HasIndex(e => new { e.PortalUserId, e.ProviderId }).IsUnique().HasDatabaseName("uk_user_home_view__portal_user_id__provider_id");
            // The foreign key to the Saved View, and the clear when the user deletes it.
            entity.HasIndex(e => e.SavedViewId).HasDatabaseName("ix_user_home_view__saved_view_id");
        });
    }

    private static void ConfigureCustomerAccess(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProviderUser>(entity =>
        {
            entity.ToTable("provider_user");
            entity.HasKey(e => e.Id).HasName("pk_provider_user");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.PortalUserId).HasColumnName("portal_user_id");
            entity.Property(e => e.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
            entity.Property(e => e.RoleKey).HasColumnName("role_key").HasMaxLength(30).IsRequired();
            entity.Property(e => e.LastSignInTimestamp).HasColumnName("last_sign_in_timestamp");
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            // Every customer request looks a role up by Provider and user, and the admin list reads one Provider's users.
            entity.HasIndex(e => new { e.ProviderId, e.PortalUserId }).IsUnique().HasDatabaseName("uk_provider_user__provider_id__portal_user_id");
            // A route that is not under a Provider looks a user up across Providers.
            entity.HasIndex(e => e.PortalUserId).HasDatabaseName("ix_provider_user__portal_user_id");
        });

        modelBuilder.Entity<ProviderSetting>(entity =>
        {
            entity.ToTable("provider_setting");
            entity.HasKey(e => e.Id).HasName("pk_provider_setting");

            entity.Property(e => e.Id).HasColumnName("id").UseIdentityAlwaysColumn();
            entity.Property(e => e.ProviderId).HasColumnName("provider_id");
            entity.Property(e => e.DefaultRoleKey).HasColumnName("default_role_key").HasMaxLength(30).IsRequired().HasDefaultValue("viewer");
            entity.Property(e => e.AllowAdminRestrictedGrants).HasColumnName("allow_admin_restricted_grants").HasDefaultValue(false);
            entity.Property(e => e.CreatedAtTimestamp).HasColumnName("created_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAdd();
            entity.Property(e => e.UpdatedAtTimestamp).HasColumnName("updated_at_timestamp")
                .HasDefaultValueSql("now()").ValueGeneratedOnAddOrUpdate();

            entity.HasIndex(e => e.ProviderId).IsUnique().HasDatabaseName("uk_provider_setting__provider_id");
        });
    }
}
