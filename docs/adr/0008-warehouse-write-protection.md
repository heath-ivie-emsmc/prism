# 8. The warehouse is read-only, and Prism proves it

Status: accepted

## Context

Prism reads the production data warehouse. A change to it, from a bug, a bad query, a test or a developer's machine, would be catastrophic and could not be undone by a deploy. A developer will have the production connection string (in user secrets, a shell or a tool), so the protection has to hold in Development and in tests, not only in deployed environments.

## Decision

The warehouse is read-only for Prism: no schema change, no migration, no write, no DDL, from code, tests or tools. A change to the warehouse is a request to the database team, never a code change. Five layers, each tested, none relying on another:

1. **The database login is the real guarantee.** The login has SELECT only, no CREATE anywhere, owns nothing, and is not a superuser (the example for the database team is in `docs/warehouse.md`). Prism never creates or alters it.
2. **Prism verifies the login at startup and refuses to start.** `WarehouseWriteProtectionStartup`, a hosted service, runs `WarehouseWriteProtectionCheck` in every environment (Development and Testing included) whenever a warehouse connection is configured. Using catalog queries only, in a read-only transaction, it refuses a login that is a superuser or has CREATEROLE, CREATEDB, REPLICATION or BYPASSRLS, is a member of a privileged role, can create in the database or a schema, owns the database, a schema or any table, view or sequence, or can write to any table or view (including by a single column) or use a sequence, in any non-system schema. If it cannot reach the warehouse or read the catalog it also refuses: a warehouse that cannot be verified is not used. The message names the kind of privilege and counts, and the role name; never a connection string, host or password. The exception is `WarehouseWriteProtectionException`, thrown from `StartAsync`, so a dev or deployed host fails to start. Temporary tables are reported but do not refuse: PostgreSQL grants TEMP to everyone by default, a temporary table is private to the session and touches no warehouse object, and refusing it would make Prism unable to start on a standard database.
3. **Every query is read-only in the server's eyes.** `NpgsqlWarehouse` runs each query in an explicit transaction whose first statement is `SET TRANSACTION READ ONLY`, always rolled back. The server refuses a write (SQLSTATE 25006) even if a session default were changed. The connection also keeps `default_transaction_read_only=on` and a statement timeout as belt and braces.
4. **Every query passes `WarehouseSqlPolicy`.** On top of the existing rules (one statement, SELECT or WITH, filtered on `@provider_id`), a linear scanner reads the text as PostgreSQL does and ignores string literals (plain, `E''`, dollar-quoted), quoted identifiers and comments. It refuses write, DDL and administrative verbs, `INTO`, locking clauses, and calls to side-effect or outside-reaching functions (`set_config`, `pg_terminate_backend`, `nextval`, `dblink*`, `lo_*`, `pg_read_file`, `pg_sleep`, `query_to_xml` and the like). It also refuses what it cannot read with certainty (an unterminated literal or comment, non-ASCII or control characters in code, unicode-escaped identifiers). A guard test runs it over every SQL text in the code.
5. **Tests and tools cannot reach a real database.** Test helpers get a connection only from a Testcontainers fixture, `TestDatabaseGuard.Assert` throws for anything else, and architecture tests fail on a test that builds a connection without the guard, reads the warehouse connection from real configuration, user secrets or the environment, on production code that touches the warehouse connection outside its folder and its one registration point, on an EF context configured with it, on any migration, `migrationBuilder` or `.sql` that targets `reporting` or `ods`, and on production SQL with DDL or DML against those schemas. The test host blanks the warehouse setting, so an environment variable in a developer's shell never reaches it.

## Trade-offs

- The login, not Prism, is the guarantee. Layers 3 and 4 stop Prism's own mistakes and make a misconfigured login visible; they do not replace the grant. That is why layer 2 refuses to start rather than warn.
- Failing closed means a restart while the warehouse is down will not come up. The orchestrator retries, and the alternative is serving from a warehouse no one has verified. Metric routes already answer 503 when the warehouse is unavailable at query time; only startup is stricter.
- The policy is stricter than needed: an unquoted column named like a verb (`update`) is refused and must be quoted. No existing query needed that. A function deny-list can never be complete, which is why the transaction and the login sit behind it.
- A login that is itself read-only but granted `pg_read_all_data`-style access is accepted: reading is the point.
- TEMP is informational only, as above.

## Consequences

- A new query follows `docs/metric-design-guide.md` section 9: `IWarehouse` only, and it passes the policy (the guard test fails the build otherwise).
- A new integration test uses `PostgresFixture` and, for DDL, `TestDatabaseGuard.Assert`.
- The database team owns the login. Prism documents what it needs and checks it.
