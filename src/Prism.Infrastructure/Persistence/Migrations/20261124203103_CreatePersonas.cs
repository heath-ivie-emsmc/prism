using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreatePersonas : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "onboarding_question",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                prompt = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_onboarding_question", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "persona",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                view_template_id = table.Column<int>(type: "integer", nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_persona", x => x.id);
                table.ForeignKey(
                    name: "fk_persona__view_template_id__view_template",
                    column: x => x.view_template_id,
                    principalTable: "view_template",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "user_home_view",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                saved_view_id = table.Column<int>(type: "integer", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_home_view", x => x.id);
                table.ForeignKey(
                    name: "fk_user_home_view__saved_view_id__saved_view",
                    column: x => x.saved_view_id,
                    principalTable: "saved_view",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "onboarding_option",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                onboarding_question_id = table.Column<int>(type: "integer", nullable: false),
                label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                sort_order = table.Column<int>(type: "integer", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_onboarding_option", x => x.id);
                table.ForeignKey(
                    name: "fk_onboarding_option__onboarding_question_id",
                    column: x => x.onboarding_question_id,
                    principalTable: "onboarding_question",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "user_profile",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                persona_id = table.Column<int>(type: "integer", nullable: true),
                onboarding_completed_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_user_profile", x => x.id);
                table.ForeignKey(
                    name: "fk_user_profile__persona_id__persona",
                    column: x => x.persona_id,
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "onboarding_option_weight",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                onboarding_option_id = table.Column<int>(type: "integer", nullable: false),
                persona_id = table.Column<int>(type: "integer", nullable: false),
                weight = table.Column<int>(type: "integer", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_onboarding_option_weight", x => x.id);
                table.CheckConstraint("ck_onboarding_option_weight__weight", "weight >= 0 AND weight <= 100");
                table.ForeignKey(
                    name: "fk_onboarding_option_weight__onboarding_option_id",
                    column: x => x.onboarding_option_id,
                    principalTable: "onboarding_option",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_onboarding_option_weight__persona_id__persona",
                    column: x => x.persona_id,
                    principalTable: "persona",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_onboarding_option__onboarding_question_id__sort_order",
            table: "onboarding_option",
            columns: new[] { "onboarding_question_id", "sort_order" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_onboarding_option_weight__persona_id",
            table: "onboarding_option_weight",
            column: "persona_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_onboarding_option_weight__onboarding_option_id__persona_id",
            table: "onboarding_option_weight",
            columns: new[] { "onboarding_option_id", "persona_id" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_onboarding_question__is_published__sort_order",
            table: "onboarding_question",
            columns: new[] { "is_published", "sort_order" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_persona__view_template_id",
            table: "persona",
            column: "view_template_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_persona__name",
            table: "persona",
            column: "name",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_user_home_view__saved_view_id",
            table: "user_home_view",
            column: "saved_view_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_user_home_view__portal_user_id__provider_id",
            table: "user_home_view",
            columns: new[] { "portal_user_id", "provider_id" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_user_profile__persona_id",
            table: "user_profile",
            column: "persona_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_user_profile__portal_user_id",
            table: "user_profile",
            column: "portal_user_id",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "onboarding_option_weight");

        migrationBuilder.DropTable(
            name: "user_home_view");

        migrationBuilder.DropTable(
            name: "user_profile");

        migrationBuilder.DropTable(
            name: "onboarding_option");

        migrationBuilder.DropTable(
            name: "persona");

        migrationBuilder.DropTable(
            name: "onboarding_question");
    }
}
