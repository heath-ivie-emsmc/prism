using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateCustomerAccess : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "detail",
            table: "admin_audit_entry",
            type: "character varying(80)",
            maxLength: 80,
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "target_portal_user_id",
            table: "admin_audit_entry",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "provider_setting",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                default_role_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false, defaultValue: "viewer"),
                allow_admin_restricted_grants = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_provider_setting", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "provider_user",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                role_key = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                last_sign_in_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_provider_user", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_provider_setting__provider_id",
            table: "provider_setting",
            column: "provider_id",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_provider_user__portal_user_id",
            table: "provider_user",
            column: "portal_user_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_provider_user__provider_id__portal_user_id",
            table: "provider_user",
            columns: new[] { "provider_id", "portal_user_id" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "provider_setting");

        migrationBuilder.DropTable(
            name: "provider_user");

        migrationBuilder.DropColumn(
            name: "detail",
            table: "admin_audit_entry");

        migrationBuilder.DropColumn(
            name: "target_portal_user_id",
            table: "admin_audit_entry");
    }
}
