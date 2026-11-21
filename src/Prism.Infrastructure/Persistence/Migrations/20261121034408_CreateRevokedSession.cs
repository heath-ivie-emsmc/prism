using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Prism.Infrastructure.Persistence;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class CreateRevokedSession : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "revoked_session",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                session_expires_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                updated_at_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_revoked_session", x => x.id);
            })
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "ix_revoked_session__session_expires_at_timestamp",
            table: "revoked_session",
            column: "session_expires_at_timestamp")
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        migrationBuilder.CreateIndex(
            name: "uk_revoked_session__session_id",
            table: "revoked_session",
            column: "session_id",
            unique: true)
            .Annotation("Npgsql:StorageParameter:fillfactor", 90);

        // Timestamp trigger on the new table and fill factor 100 on its primary key (EF cannot express either).
        migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "revoked_session");
    }
}
