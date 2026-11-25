using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSavedViewIsHidden : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_hidden",
            table: "saved_view",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "is_hidden",
            table: "saved_view");
    }
}
