using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Prism.Infrastructure.Warehouse;

/// <summary>What the startup check found about the warehouse login. It holds the role name and counts, never a connection string or a password.</summary>
public sealed record WarehouseWriteProtectionReport(string RoleName, bool SessionReadOnly, bool TemporaryTablesAllowed, IReadOnlyList<string> Problems)
{
    public bool IsReadOnly => Problems.Count == 0;
}

/// <summary>The warehouse login can write, create or own something, or could not be checked. The host refuses to start.</summary>
public sealed class WarehouseWriteProtectionException : InvalidOperationException
{
    public WarehouseWriteProtectionException(string message)
        : base(message)
    {
    }

    public WarehouseWriteProtectionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Proves at startup that the warehouse login cannot write. Replaceable in tests that run against a host with no real warehouse.</summary>
public interface IWarehouseWriteProtectionCheck
{
    /// <summary>Completes when the login is read-only. Throws <see cref="WarehouseWriteProtectionException"/> when it is not or when that cannot be established.</summary>
    Task VerifyAsync(CancellationToken cancellationToken);
}

/// <summary>Used when no warehouse connection is configured: there is no login to check, and the start-up notice says so.</summary>
public sealed class NotConfiguredWarehouseWriteProtectionCheck : IWarehouseWriteProtectionCheck
{
    public Task VerifyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// The first layer's proof: with read-only catalog queries only, over the warehouse connection, it checks what the login is allowed to do and refuses
/// a login that is a superuser, can create roles, databases or objects, is a member of a privileged role, or can write to or owns anything in any non-system schema.
/// It needs no privilege of its own beyond reading the catalog, runs in a read-only transaction, and returns at most one row per schema.
/// </summary>
public sealed partial class WarehouseWriteProtectionCheck(NpgsqlDataSource dataSource, IOptions<PrismWarehouseOptions> options, ILogger<WarehouseWriteProtectionCheck> logger) : IWarehouseWriteProtectionCheck
{
    /// <summary>The most schemas the check reads. More than this cannot be verified here and refuses the start.</summary>
    internal const int MaxSchemas = 1000; // SchemaSql reads one more than this, to know there are too many

    // The role's own attributes, what it can do to the database, and the session default. Every statement here only reads the catalog.
    internal const string RoleSql =
        """
        SELECT r.rolname, r.rolsuper, r.rolcreaterole, r.rolcreatedb, r.rolreplication, r.rolbypassrls,
               has_database_privilege(current_database(), 'CREATE') AS can_create_in_database,
               has_database_privilege(current_database(), 'TEMP') AS can_create_temporary,
               pg_has_role((SELECT d.datdba FROM pg_database d WHERE d.datname = current_database()), 'USAGE') AS owns_database,
               current_setting('default_transaction_read_only') AS session_read_only
        FROM pg_roles r
        WHERE r.rolname = current_user
        """;

    // Roles the login is a member of (directly or through other roles) that grant more than reading.
    internal const string MembershipSql =
        """
        SELECT count(*) AS privileged_roles
        FROM pg_roles m
        WHERE m.rolname <> current_user
          AND pg_has_role(m.oid, 'MEMBER')
          AND (m.rolsuper OR m.rolcreaterole OR m.rolcreatedb OR m.rolreplication OR m.rolbypassrls
               OR m.rolname = ANY (ARRAY['pg_write_all_data', 'pg_execute_server_program', 'pg_write_server_files', 'pg_signal_backend', 'pg_checkpoint', 'pg_maintain']))
        """;

    // One row per non-system schema: can the login create in it or own it, and how many tables, views, materialized views, foreign tables and sequences can it write to or own.
    internal const string SchemaSql =
        """
        SELECT has_schema_privilege(n.oid, 'CREATE') AS can_create,
               pg_has_role(n.nspowner, 'USAGE') AS owns_schema,
               count(c.oid) FILTER (WHERE c.relkind <> 'S' AND (has_table_privilege(c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,REFERENCES,TRIGGER')
                                                                OR has_any_column_privilege(c.oid, 'INSERT,UPDATE,REFERENCES'))) AS writable_relations,
               count(c.oid) FILTER (WHERE c.relkind = 'S' AND has_sequence_privilege(c.oid, 'USAGE,UPDATE')) AS writable_sequences,
               count(c.oid) FILTER (WHERE pg_has_role(c.relowner, 'USAGE')) AS owned_objects
        FROM pg_namespace n
        LEFT JOIN pg_class c ON c.relnamespace = n.oid AND c.relkind IN ('r', 'p', 'v', 'm', 'f', 'S')
        WHERE n.nspname !~ '^pg_' AND n.nspname <> 'information_schema'
        GROUP BY n.oid, n.nspname, n.nspowner
        ORDER BY n.nspname
        LIMIT 1001
        """;

    public async Task VerifyAsync(CancellationToken cancellationToken)
    {
        WarehouseWriteProtectionReport report = await InspectAsync(cancellationToken);
        if (!report.IsReadOnly)
        {
            string message = $"the warehouse login '{report.RoleName}' is not read-only ({report.Problems.Count} problem(s)): {string.Join("; ", report.Problems)}";
            if (!options.Value.SkipLoginCheck)
            {
                throw new WarehouseWriteProtectionException(
                    $"Refusing to start: {message}. "
                    + "Prism never writes to the warehouse. Ask the database team for a login with SELECT only (see docs/warehouse.md, Write protection).");
            }

            LogWritableLoginAllowed(logger, message);
        }

        LogVerified(logger, report.RoleName, report.SessionReadOnly, report.TemporaryTablesAllowed);
        if (!report.SessionReadOnly)
        {
            LogSessionNotReadOnly(logger, report.RoleName);
        }
    }

    /// <summary>Reads what the login can do. Throws <see cref="WarehouseWriteProtectionException"/> only when the catalog cannot be read.</summary>
    public async Task<WarehouseWriteProtectionReport> InspectAsync(CancellationToken cancellationToken)
    {
        int timeout = options.Value.CommandTimeoutSeconds;
        try
        {
            await using ReadOnlyWarehouseSession session = await ReadOnlyWarehouseSession.BeginAsync(dataSource, timeout, cancellationToken);
            List<string> problems = [];
            (string role, bool sessionReadOnly, bool temp) = await ReadRoleAsync(session, timeout, problems, cancellationToken);
            await ReadMembershipAsync(session, timeout, problems, cancellationToken);
            await ReadSchemasAsync(session, timeout, problems, cancellationToken);
            return new WarehouseWriteProtectionReport(role, sessionReadOnly, temp, problems);
        }
        catch (NpgsqlException ex)
        {
            // Npgsql messages can carry the host. Only the SQL state is kept, and the connection string never is.
            throw new WarehouseWriteProtectionException(
                $"Refusing to start: whether the warehouse login can write could not be checked (the warehouse could not be reached or its catalog could not be read, SQL state {(ex as PostgresException)?.SqlState ?? "none"}). "
                + "Prism will not use a warehouse it cannot verify as read-only.");
        }
    }

    private static async Task<(string Role, bool SessionReadOnly, bool Temporary)> ReadRoleAsync(ReadOnlyWarehouseSession session, int timeout, List<string> problems, CancellationToken ct)
    {
        await using NpgsqlCommand command = session.CreateCommand(RoleSql, timeout);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            problems.Add("the role of the connection is not in the catalog, so what it may do cannot be checked");
            return ("unknown", false, false);
        }

        string role = SafeName(reader.GetString(0));
        AddIf(problems, reader.GetBoolean(1), "it is a superuser");
        AddIf(problems, reader.GetBoolean(2), "it can create roles");
        AddIf(problems, reader.GetBoolean(3), "it can create databases");
        AddIf(problems, reader.GetBoolean(4), "it has the replication privilege");
        AddIf(problems, reader.GetBoolean(5), "it bypasses row level security");
        AddIf(problems, reader.GetBoolean(6), "it can create objects in the database");
        AddIf(problems, reader.GetBoolean(8), "it owns the database");
        return (role, string.Equals(reader.GetString(9), "on", StringComparison.OrdinalIgnoreCase), reader.GetBoolean(7));
    }

    private static async Task ReadMembershipAsync(ReadOnlyWarehouseSession session, int timeout, List<string> problems, CancellationToken ct)
    {
        await using NpgsqlCommand command = session.CreateCommand(MembershipSql, timeout);
        long privileged = Convert.ToInt64(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        AddIf(problems, privileged > 0, $"it is a member of {privileged} privileged role(s) (superuser, administrative or able to write)");
    }

    private static async Task ReadSchemasAsync(ReadOnlyWarehouseSession session, int timeout, List<string> problems, CancellationToken ct)
    {
        int schemas = 0;
        int create = 0;
        int ownedSchemas = 0;
        long writableRelations = 0;
        long writableSequences = 0;
        long ownedObjects = 0;
        await using NpgsqlCommand command = session.CreateCommand(SchemaSql, timeout);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            schemas++;
            create += reader.GetBoolean(0) ? 1 : 0;
            ownedSchemas += reader.GetBoolean(1) ? 1 : 0;
            writableRelations += reader.GetInt64(2);
            writableSequences += reader.GetInt64(3);
            ownedObjects += reader.GetInt64(4);
        }

        AddIf(problems, schemas > MaxSchemas, $"there are more than {MaxSchemas} schemas, which is too many to verify");
        AddIf(problems, create > 0, $"it can create objects in {create} schema(s)");
        AddIf(problems, ownedSchemas > 0, $"it owns {ownedSchemas} schema(s)");
        AddIf(problems, writableRelations > 0, $"it can write to (INSERT, UPDATE, DELETE, TRUNCATE, REFERENCES or TRIGGER) {writableRelations} table(s) or view(s)");
        AddIf(problems, writableSequences > 0, $"it can use or update {writableSequences} sequence(s)");
        AddIf(problems, ownedObjects > 0, $"it owns {ownedObjects} table(s), view(s) or sequence(s)");
    }

    private static void AddIf(List<string> problems, bool condition, string problem)
    {
        if (condition)
        {
            problems.Add(problem);
        }
    }

    /// <summary>A role name is not secret, but it comes from outside, so it is cut and limited to harmless characters before it reaches a message.</summary>
    private static string SafeName(string name)
    {
        string cut = name.Length > 63 ? name[..63] : name;
        return string.Concat(cut.Select(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '?'));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "TEMPORARY OVERRIDE Warehouse:SkipLoginCheck is on, so the login check was skipped although {Problem}. Prism never writes and still runs every query in a read-only transaction. Remove the override as soon as a SELECT-only login is available")]
    private static partial void LogWritableLoginAllowed(ILogger logger, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Warehouse login {Role} verified read-only (session default read-only: {SessionReadOnly}, may create temporary tables: {Temporary})")]
    private static partial void LogVerified(ILogger logger, string role, bool sessionReadOnly, bool temporary);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Warehouse login {Role} does not default to read-only transactions. Every Prism query still runs in a read-only transaction. Ask the database team for ALTER ROLE ... SET default_transaction_read_only = on")]
    private static partial void LogSessionNotReadOnly(ILogger logger, string role);
}

/// <summary>
/// Runs the write protection check before the host serves anything, in every environment, whenever a warehouse connection is configured.
/// A failure here stops the host from starting. With no connection configured there is nothing to check, and <c>WarehouseStartupNotice</c> already says so.
/// </summary>
public sealed class WarehouseWriteProtectionStartup(IOptions<PrismWarehouseOptions> options, IWarehouseWriteProtectionCheck check) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        options.Value.IsConfigured ? check.VerifyAsync(cancellationToken) : Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
