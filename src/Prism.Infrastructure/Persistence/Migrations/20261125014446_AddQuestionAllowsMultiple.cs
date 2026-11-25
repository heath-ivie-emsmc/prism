using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddQuestionAllowsMultiple : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "allows_multiple",
            table: "onboarding_question",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "allows_multiple",
            table: "onboarding_question");
    }
}
