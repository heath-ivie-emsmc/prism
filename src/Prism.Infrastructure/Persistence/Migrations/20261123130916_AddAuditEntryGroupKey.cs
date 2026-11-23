using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuditEntryGroupKey : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "group_key",
            table: "audit_entry",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "group_key",
            table: "audit_entry");
    }
}
