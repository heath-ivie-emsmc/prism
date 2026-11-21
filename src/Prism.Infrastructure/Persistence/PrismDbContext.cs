using Microsoft.EntityFrameworkCore;
using Prism.Infrastructure.Persistence.Entities;

namespace Prism.Infrastructure.Persistence;

/// <summary>Prism's own writable database. The data warehouse is a separate read-only connection and is never mapped here.</summary>
public sealed class PrismDbContext(DbContextOptions<PrismDbContext> options) : DbContext(options)
{
    public DbSet<RevokedSession> RevokedSessions => Set<RevokedSession>();

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

        EmsmcModelConventions.Apply(modelBuilder);
    }
}
