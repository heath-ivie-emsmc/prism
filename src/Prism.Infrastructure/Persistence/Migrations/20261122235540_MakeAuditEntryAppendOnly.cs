using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Prism.Infrastructure.Persistence.Migrations;

/// <summary>
/// Makes audit_entry append-only at the database: UPDATE, DELETE and TRUNCATE raise, whoever the caller is.
/// The application role needs INSERT and SELECT only. Retention, if ever needed, is a deliberate DBA step that drops the trigger first.
/// </summary>
public partial class MakeAuditEntryAppendOnly : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION public.fn_reject_audit_change()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'audit_entry is append-only: % is not allowed', TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            CREATE TRIGGER trg_audit_entry__append_only
                BEFORE UPDATE OR DELETE ON public.audit_entry
                FOR EACH ROW EXECUTE FUNCTION public.fn_reject_audit_change();

            CREATE TRIGGER trg_audit_entry__no_truncate
                BEFORE TRUNCATE ON public.audit_entry
                FOR EACH STATEMENT EXECUTE FUNCTION public.fn_reject_audit_change();
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_audit_entry__no_truncate ON public.audit_entry;
            DROP TRIGGER IF EXISTS trg_audit_entry__append_only ON public.audit_entry;
            DROP FUNCTION IF EXISTS public.fn_reject_audit_change();
            """);
    }
}
