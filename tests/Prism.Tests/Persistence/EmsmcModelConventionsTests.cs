using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Prism.Infrastructure.Persistence;

namespace Prism.Tests.Persistence;

public sealed class EmsmcModelConventionsTests
{
    [Fact]
    public void Tables_and_indexes_get_fill_factor_90()
    {
        using var context = new CascadeContext();

        // Relational annotations such as storage parameters only exist on the design-time model.
        IModel model = context.GetService<IDesignTimeModel>().Model;
        IEntityType parent = model.FindEntityType(typeof(Parent))!;

        Assert.Equal(90, parent.GetStorageParameters()["fillfactor"]);
        Assert.NotEmpty(parent.GetIndexes());
        Assert.All(parent.GetIndexes(), index => Assert.Equal(90, index.GetStorageParameters()["fillfactor"]));
    }

    [Fact]
    public void Cascade_foreign_keys_become_restrict()
    {
        using var context = new CascadeContext();

        IForeignKey foreignKey = context.Model.FindEntityType(typeof(Child))!.GetForeignKeys().Single();

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Set_null_foreign_keys_become_restrict()
    {
        using var context = new SetNullContext();

        IForeignKey foreignKey = context.Model.FindEntityType(typeof(Child))!.GetForeignKeys().Single();

        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Client_cascade_is_left_alone()
    {
        using var context = new ClientCascadeContext();

        IForeignKey foreignKey = context.Model.FindEntityType(typeof(Child))!.GetForeignKeys().Single();

        Assert.Equal(DeleteBehavior.ClientCascade, foreignKey.DeleteBehavior);
    }

    internal sealed class Parent
    {
        public int Id { get; set; }

        public string Name { get; set; } = "";
    }

    internal sealed class Child
    {
        public int Id { get; set; }

        public int? ParentId { get; set; }

        public Parent? Parent { get; set; }
    }

    // EF caches one model per DbContext type, so each delete behavior under test needs its own context type.
    private abstract class SampleContext(DeleteBehavior deleteBehavior) : DbContext(Options())
    {
        private static DbContextOptions Options() =>
            new DbContextOptionsBuilder().UsePrismNpgsql("Host=localhost;Database=unused").Options;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Parent>().HasIndex(x => x.Name).IsUnique();
            modelBuilder.Entity<Child>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(deleteBehavior);

            EmsmcModelConventions.Apply(modelBuilder);
        }
    }

    private sealed class CascadeContext() : SampleContext(DeleteBehavior.Cascade);

    private sealed class SetNullContext() : SampleContext(DeleteBehavior.SetNull);

    private sealed class ClientCascadeContext() : SampleContext(DeleteBehavior.ClientCascade);
}
