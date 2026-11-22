using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateAuditEntry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_entry",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                provider_id = table.Column<int>(type: "integer", nullable: false),
                portal_user_id = table.Column<long>(type: "bigint", nullable: false),
                action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                check_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                period = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                row_count = table.Column<int>(type: "integer", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_audit_entry", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_audit_entry__portal_user_id__created_at_timestamp",
            table: "audit_entry",
            columns: new[] { "portal_user_id", "created_at_timestamp" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_audit_entry__provider_id__created_at_timestamp",
            table: "audit_entry",
            columns: new[] { "provider_id", "created_at_timestamp" })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_entry");
    }
}
