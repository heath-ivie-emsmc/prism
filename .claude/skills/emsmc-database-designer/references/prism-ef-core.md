# Applying the EMSMC conventions with EF Core (Prism)

Prism persists with EF Core and PostgreSQL. The EMSMC conventions in `SKILL.md` and the other reference files are the standard. This file maps them onto EF Core so the generated schema matches what the DBA team expects. It adds no new conventions.

## Mapping the mandatory structure

- **Primary key:** a single `int` or `bigint` column named `id`, configured as `GENERATED ALWAYS AS IDENTITY` (`UseIdentityAlwaysColumn()`), PK constraint named `pk_<table>`. Choose `bigint` for high-volume tables (logs, events, audit).
- **Timestamps:** every table has `created_at_timestamp` and `updated_at_timestamp` (`timestamp`, UTC), set by the shared `fn_set_timestamps()` trigger, not by application code. Map the properties with `ValueGeneratedOnAdd` / `ValueGeneratedOnAddOrUpdate`. The trigger `trg_<table>__timestamps` is created by `EmsmcPostgreSqlScripts.AfterTablesCreated` (`src/Prism.Infrastructure/Persistence/`), which a migration runs through `migrationBuilder.Sql(...)` after it creates tables. It attaches the trigger to every table in the listed schemas that has both timestamp columns, so no table can miss it, and it is safe to run again.
- **User-facing tables** also have `created_by_user_name` and `updated_by_user_name` (`varchar(200)`), set by the application.
- **Foreign keys:** database behavior must be `RESTRICT`. EF defaults required relationships to cascade. `EmsmcModelConventions.Apply(modelBuilder)` (end of `OnModelCreating`) turns any `Cascade` or `SetNull` into `Restrict`. `ClientCascade` is left alone for children the application deletes itself.
- **Booleans:** `is_` or verb prefix, `NOT NULL`, with a database default (`HasDefaultValue`).
- **jsonb:** use `jsonb`, never `json`, with a default (`'{}'::jsonb` or `'[]'::jsonb`).
- **Naming:** lowercase snake_case for tables, columns and every constraint and index. Use `HasDatabaseName(...)` with the doc format, for example `ix_session_revocation__user_id__revoked_at_timestamp`. Never accept an EF-generated name. Check every name against the 63-character PostgreSQL limit and apply the truncation rules in `naming-conventions.md`.
- **Fill factor:** table 90, primary key 100, unique key 90, index 90. `EmsmcModelConventions.Apply` sets table and index fill factor 90 on every entity. EF has no API for keys, so `EmsmcPostgreSqlScripts.AfterTablesCreated` sets every primary key to 100 in the same migration step.
- **Index limits:** at most 5 per table, high-cardinality columns first, all `WHERE` columns in one index.
- **Enums:** PostgreSQL enum types named `enum_<name>`, values in PascalCase with underscores matching the C# enum exactly.
- **Historical tables** (audit log and similar) are partitioned by day or month.

## Workflow for a schema change

1. Describe the change with an ER diagram and the DDL this skill would produce.
2. Write or change the entity and its configuration so the EF model produces that DDL.
3. Generate the migration, then read the generated SQL and compare it with the DDL from step 1. Fix naming, FK behavior, fill factor and triggers before committing.
4. Migrations run in the CI migration step, never at application startup.
5. Flag anything that deviates from the DBA standard in the PR description so reviewers expect it.

## Query shape

The query rules in `sql-best-practices.md` apply to raw SQL in migrations and to Metric Catalog warehouse queries: name the exact columns, always filter, no functions on the left of a comparison, `EXISTS` over `IN` for large inner queries, always parameterize, and never use `DISTINCT` to hide a bad join.

## Shared helpers (in `src/Prism.Infrastructure/Persistence/`)

- `PrismNpgsqlConfiguration.UsePrismNpgsql(...)`: the only way to point a DbContext at PostgreSQL. Never call `UseNpgsql` directly.
- `EmsmcModelConventions.Apply(modelBuilder)`: fill factor 90 on tables and indexes, no database cascade or set-null.
- `EmsmcPostgreSqlScripts`: `fn_set_timestamps`, a timestamp trigger on every table, and primary key fill factor 100. Add `migrationBuilder.Sql(EmsmcPostgreSqlScripts.AfterTablesCreated);` to every migration that creates tables.

Known divergence for DBA review: the timestamp columns are `timestamp with time zone` and the function assigns `now()`, as in the M6 project, instead of the `timestamp` and `now() AT TIME ZONE 'UTC'` shown in `SKILL.md`. Assigning the `AT TIME ZONE` form to a `timestamptz` column shifts instants when a client session is not on UTC.
