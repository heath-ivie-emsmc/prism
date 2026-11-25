using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateViewTemplates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "source_template_assignment_id",
            table: "saved_view",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "admin_audit_entry",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                actor_portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                object_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                object_id = table.Column<int>(type: "integer", nullable: true),
                provider_id = table.Column<int>(type: "integer", nullable: true),
                affected_count = table.Column<int>(type: "integer", nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_admin_audit_entry", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "view_template",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                layout = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                layout_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                is_published = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_view_template", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "template_assignment",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                view_template_id = table.Column<int>(type: "integer", nullable: false),
                scope_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                provider_id = table.Column<int>(type: "integer", nullable: true),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                layout = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                layout_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                revoked_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by_portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_template_assignment", x => x.id);
                table.CheckConstraint("ck_template_assignment__provider_id", "(scope_kind = 'Provider') = (provider_id IS NOT NULL)");
                table.CheckConstraint("ck_template_assignment__scope_kind", "scope_kind IN ('All', 'Provider')");
                table.ForeignKey(
                    name: "fk_template_assignment__view_template_id__view_template",
                    column: x => x.view_template_id,
                    principalTable: "view_template",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "template_delivery",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                template_assignment_id = table.Column<int>(type: "integer", nullable: false),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                saved_view_id = table.Column<int>(type: "integer", nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_template_delivery", x => x.id);
                table.ForeignKey(
                    name: "fk_template_delivery__saved_view_id__saved_view",
                    column: x => x.saved_view_id,
                    principalTable: "saved_view",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_template_delivery__template_assignment_id",
                    column: x => x.template_assignment_id,
                    principalTable: "template_assignment",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_saved_view__source_template_assignment_id",
            table: "saved_view",
            column: "source_template_assignment_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_admin_audit_entry__plus",
            table: "admin_audit_entry",
            columns: new[] { "actor_portal_user_id", "created_at_timestamp" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_admin_audit_entry__provider_id__created_at_timestamp",
            table: "admin_audit_entry",
            columns: new[] { "provider_id", "created_at_timestamp" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_template_assignment__provider_id__scope_kind",
            table: "template_assignment",
            columns: new[] { "provider_id", "scope_kind" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_template_assignment__view_template_id",
            table: "template_assignment",
            column: "view_template_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_template_delivery__saved_view_id",
            table: "template_delivery",
            column: "saved_view_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_template_delivery__template_assignment_id",
            table: "template_delivery",
            column: "template_assignment_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_template_delivery__plus",
            table: "template_delivery",
            columns: new[] { "portal_user_id", "provider_id", "template_assignment_id" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_view_template__name",
            table: "view_template",
            column: "name",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.AddForeignKey(
            name: "fk_saved_view__source_template_assignment_id",
            table: "saved_view",
            column: "source_template_assignment_id",
            principalTable: "template_assignment",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);

        // The admin audit trail is append-only, like audit_entry: UPDATE, DELETE and TRUNCATE raise, whoever the caller is.
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.fn_reject_admin_audit_change()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'admin_audit_entry is append-only: % is not allowed', TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_admin_audit_entry__append_only
                BEFORE UPDATE OR DELETE ON public.admin_audit_entry
                FOR EACH ROW EXECUTE FUNCTION public.fn_reject_admin_audit_change();

            CREATE TRIGGER trg_admin_audit_entry__no_truncate
                BEFORE TRUNCATE ON public.admin_audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION public.fn_reject_admin_audit_change();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_admin_audit_entry__no_truncate ON public.admin_audit_entry;
            DROP TRIGGER IF EXISTS trg_admin_audit_entry__append_only ON public.admin_audit_entry;
            DROP FUNCTION IF EXISTS public.fn_reject_admin_audit_change();
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_saved_view__source_template_assignment_id",
            table: "saved_view");

        migrationBuilder.DropTable(
            name: "admin_audit_entry");

        migrationBuilder.DropTable(
            name: "template_delivery");

        migrationBuilder.DropTable(
            name: "template_assignment");

        migrationBuilder.DropTable(
            name: "view_template");

        migrationBuilder.DropIndex(
            name: "ix_saved_view__source_template_assignment_id",
            table: "saved_view");

        migrationBuilder.DropColumn(
            name: "source_template_assignment_id",
            table: "saved_view");
    }
}
