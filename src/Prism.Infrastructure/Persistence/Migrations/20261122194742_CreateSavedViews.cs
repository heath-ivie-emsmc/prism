using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateSavedViews : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "saved_view",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                owner_portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                layout = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                layout_schema_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                created_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                updated_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_saved_view", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "saved_view_invitation",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                saved_view_id = table.Column<int>(type: "integer", nullable: false),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                invited_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                redeemed_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                redeemed_by_portal_user_id = table.Column<long>(type: "bigint", nullable: true),
                revoked_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_by_portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_saved_view_invitation", x => x.id);
                table.ForeignKey(
                    name: "fk_saved_view_invitation__saved_view_id__saved_view",
                    column: x => x.saved_view_id,
                    principalTable: "saved_view",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateTable(
            name: "saved_view_share",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                saved_view_id = table.Column<int>(type: "integer", nullable: false),
                shared_with_portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                saved_view_invitation_id = table.Column<int>(type: "integer", nullable: true),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_saved_view_share", x => x.id);
                table.ForeignKey(
                    name: "fk_saved_view_share__saved_view_id__saved_view",
                    column: x => x.saved_view_id,
                    principalTable: "saved_view",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_saved_view_share__saved_view_invitation_id",
                    column: x => x.saved_view_invitation_id,
                    principalTable: "saved_view_invitation",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_saved_view__provider_id__owner_portal_user_id__name",
            table: "saved_view",
            columns: new[] { "provider_id", "owner_portal_user_id", "name" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_saved_view_invitation__expires_at_timestamp",
            table: "saved_view_invitation",
            column: "expires_at_timestamp")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_saved_view_invitation__saved_view_id",
            table: "saved_view_invitation",
            column: "saved_view_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_saved_view_invitation__token_hash",
            table: "saved_view_invitation",
            column: "token_hash",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_saved_view_share__saved_view_invitation_id",
            table: "saved_view_share",
            column: "saved_view_invitation_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_saved_view_share__shared_with_portal_user_id",
            table: "saved_view_share",
            column: "shared_with_portal_user_id")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_saved_view_share__saved_view_id__shared_with_portal_user_id",
            table: "saved_view_share",
            columns: new[] { "saved_view_id", "shared_with_portal_user_id" },
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "saved_view_share");

        migrationBuilder.DropTable(
            name: "saved_view_invitation");

        migrationBuilder.DropTable(
            name: "saved_view");
    }
}
