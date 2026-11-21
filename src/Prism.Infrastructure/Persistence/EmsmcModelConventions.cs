using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Prism.Infrastructure.Persistence;

/// <summary>
/// Applies the EMSMC DBA standard to the whole EF model, so no table can miss it: table and index fill factor 90,
/// and foreign keys that never cascade or set null in the database.
/// </summary>
/// <remarks>
/// Call <see cref="Apply"/> at the end of <c>OnModelCreating</c>, after every entity configuration.
/// Primary key fill factor (100) has no EF API, and the timestamp triggers are database objects, so both are applied
/// by <see cref="EmsmcPostgreSqlScripts"/> from each migration that creates tables.
/// Standard: <c>.claude/skills/emsmc-database-designer</c>.
/// </remarks>
public static class EmsmcModelConventions
{
    public const int TableFillFactor = 90;

    public const int IndexFillFactor = 90;

    private const string FillFactor = "fillfactor";

    public static void Apply(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            ApplyForeignKeyBehavior(entityType);

            // Views and keyless query types have no heap or indexes to tune.
            if (entityType.GetTableName() is null || entityType.GetViewName() is not null)
            {
                continue;
            }

            entityType.SetStorageParameter(FillFactor, TableFillFactor);

            foreach (IMutableIndex index in entityType.GetIndexes())
            {
                index.SetStorageParameter(FillFactor, IndexFillFactor);
            }
        }
    }

    // ClientCascade and the default ClientSetNull are allowed: they act in EF, and the database keeps RESTRICT semantics.
    private static void ApplyForeignKeyBehavior(IMutableEntityType entityType)
    {
        IEnumerable<IMutableForeignKey> offenders = entityType.GetForeignKeys()
            .Where(foreignKey => foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.SetNull);

        foreach (IMutableForeignKey foreignKey in offenders)
        {
            foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }
}
