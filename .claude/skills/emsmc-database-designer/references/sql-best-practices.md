# SQL Best Practices

By Cintia Mendiberry, adapted and extended.

## Table of Contents

1. [Queries](#queries)
2. [CTEs](#ctes)
3. [Joins](#joins)
4. [WHERE Clauses and Indexes](#where-clauses-and-indexes)
5. [EXISTS vs IN](#exists-vs-in)
6. [Views](#views)
7. [Stored Procedures](#stored-procedures)
8. [Functions](#functions)
9. [Indexes](#indexes)
10. [Tables](#tables)
11. [Columns](#columns)
12. [Partitioning](#partitioning)
13. [Linked Servers](#linked-servers)

---

## Queries

- **Use aliases** with different names than the objects they refer to. Aliases improve readability and prevent ambiguity.
- **SELECT exact columns** needed. Never use `SELECT *`.
- **SELECT exact rows** needed — always use a WHERE clause.
- **Be precise when joining** — `INNER JOIN` is always more efficient than `LEFT`/`RIGHT JOIN`. Only use outer joins when you genuinely need unmatched rows.
- **No implicit conversion.** Convert the parameter to the target column's type before using it in the query. Implicit conversions prevent index usage and cause CPU overhead.
- **Never use DISTINCT to fix wrong results.** If you need DISTINCT, the joins or WHERE clause are wrong. Fix the root cause.
- **ORDER BY / GROUP BY**: Use column names, not positional numbers or aliases.
- **Avoid functions in SELECT list.** If a function is necessary, wrap it in a subquery in the select list.
- **No LIKE on numeric columns** (int, bigint, etc.).

## CTEs

Use Common Table Expressions when:
- The intermediate result is **widely used** by applications (reducing deadlocks by isolating reads).
- The CTE result is **reused in the next query**, reducing the number of tables joined.
- Querying **recursive tables** (hierarchical data).

## Joins

- Prefer `INNER JOIN` over `LEFT JOIN` / `RIGHT JOIN` when you don't need unmatched rows.
- Always specify the join condition explicitly. Never rely on implicit joins (comma-separated FROM).
- When joining many tables, consider breaking the query into CTEs to reduce complexity.

## WHERE Clauses and Indexes

**Critical rule: NEVER use functions on the left side of a comparison.** This breaks index usage.

```sql
-- WRONG: function on the left side breaks the index
WHERE CONVERT(DATE, BH1.BATCHDATE) = CONVERT(DATE, GETDATE() - 9)

-- CORRECT: transform the right side instead
WHERE BH1.BATCHDATE >= CONVERT(DATE, GETDATE() - 9)
  AND BH1.BATCHDATE <  CONVERT(DATE, GETDATE() - 8)
```

**Index alignment:**
- Check that columns used in WHERE clauses are contained in an index.
- Databases can only use **one index per table** to access data. All WHERE columns should be in one index.
- **Column order in the index matters.** Columns with **high cardinality** (many distinct values) must appear first.

**Type matching for hard-coded SQL:**
- If a variable has a different type from the target column, make an explicit conversion in the WHERE clause.
- Example: `@Param1` is `nvarchar(10)` but the column is `varchar(10)`:
  ```sql
  -- Correct: explicit conversion
  SELECT Col1, Col2 FROM Table WHERE Colx = CONVERT(varchar(10), @Param1)

  -- Wrong: implicit conversion (prevents index usage, CPU overhead)
  SELECT Col1, Col2 FROM Table WHERE Colx = @Param1
  ```

## EXISTS vs IN

Prefer `EXISTS` / `NOT EXISTS` over `IN` / `NOT IN` when the inner query returns many rows or takes a long time to execute. Ensure the inner query's table has an appropriate index on the join column.

```sql
-- Avoid: IN with large result set
WHERE RCSQL.RunNumber NOT IN (
    SELECT R_RunNumber FROM HDSQL.DBO.HospitalData_Results
)

-- Prefer: NOT EXISTS with correlated subquery
WHERE NOT EXISTS (
    SELECT 1
    FROM HDSQL.DBO.HospitalData_Results HDR
    WHERE RCSQL.RunNumber = HDR.R_RunNumber
)
```

## Views

- Apply all query best practices (select only needed columns and rows).
- Consider creating an **indexed view** when the view is frequently queried — it materializes the result for faster reads.
- **Snowflake**: Use fully qualified names (`DATABASE.SCHEMA.OBJECT`).

## Stored Procedures

- **Avoid SQL injection.** Always parameterize inputs.
- **Avoid nested stored procedures.** Nesting makes debugging and transaction management difficult.
- **Limit parameters to 5.** If more are needed, consider using a table-valued parameter or breaking the procedure into smaller units.
- **Snowflake**: Use fully qualified names.

## Functions

- **Avoid SQL injection.**
- **Limit parameters to 5.**
- **Snowflake**: Use fully qualified names.
- Prefer deterministic functions when possible — they can be indexed and cached.

## Indexes

- **Give meaningful names.** Never use names suggested by advisor tools. Follow the `ix_<table>__<col>` naming convention.
- **Check for similar indexes** before creating a new one. Redundant indexes waste space and slow writes.
- **FILLFACTOR = 90** for all indexes (PostgreSQL / SQL Server).
- **Column ordering**: High cardinality columns first. The leading column determines the index's selectivity.

## Tables

- **Primary keys** should be composed of one column only (the `id` identity column).
- **FILLFACTOR = 90** during table creation (PostgreSQL / SQL Server).
- Create proper indexes for columns used in WHERE clauses.
- **Historical data tables must be partitioned.** Partition by day or month depending on insert volume and purge frequency. Partitioning enables fast deletion/archival of old data — faster than any other method.

## Columns

- **SQL Server**: `TIMESTAMP` data type is deprecated. Use `ROWVERSION` instead for optimistic concurrency.
- **SQL Server**: Use `NVARCHAR`/`NTEXT` instead of `VARCHAR`/`TEXT` — EMS Solutions standard for Unicode support.
- **PostgreSQL**: Use `jsonb` (not `json`). Always set a default value (`DEFAULT '{}'::jsonb`).
- **PostgreSQL**: Use `timestamp` type for date/time data that includes a time component.
- **Booleans**: SQL Server = `bit`, PostgreSQL = `bool`, MySQL = `tinyint`. Always `NOT NULL` with a `DEFAULT`.
- **Enums**: Use for columns with a fixed set of allowed values (e.g., state codes). Applicable to PostgreSQL and MySQL. SQL Server uses check constraints instead.
- **Domains** (PostgreSQL): Use for columns with a fixed format (e.g., SSN, phone number).

## Partitioning

Tables that accumulate historical data over time (logs, audit trails, events, transactions) should be partitioned:

- **Partition key**: Usually a date/timestamp column.
- **Partition granularity**: By day (high-volume) or by month (moderate-volume).
- **Benefit**: Enables fast `DROP PARTITION` for data purging instead of slow `DELETE` operations.

**PostgreSQL** — declarative partitioning:
```sql
CREATE TABLE "audit_log" (
    "id"                    int8 NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 MINVALUE 1 INCREMENT BY 1 NO CYCLE),
    "event_timestamp"       timestamp NOT NULL,
    ...
) PARTITION BY RANGE ("event_timestamp");

CREATE TABLE "audit_log_2026_03" PARTITION OF "audit_log"
    FOR VALUES FROM ('2026-03-01') TO ('2026-04-01');
```

**SQL Server** — partition function + scheme:
```sql
CREATE PARTITION FUNCTION [pfn_partition_by_month] (datetime2)
AS RANGE RIGHT FOR VALUES ('2026-03-01', '2026-04-01', '2026-05-01');

CREATE PARTITION SCHEME [ps_audit_log]
AS PARTITION [pfn_partition_by_month] ALL TO ([PRIMARY]);
```

**MySQL** — native partitioning:
```sql
CREATE TABLE `audit_log` (
    ...
) PARTITION BY RANGE (YEAR(`event_timestamp`) * 100 + MONTH(`event_timestamp`)) (
    PARTITION p202603 VALUES LESS THAN (202604),
    PARTITION p202604 VALUES LESS THAN (202605)
);
```

## Linked Servers (SQL Server)

- When doing SELECTs over a linked server, check the execution plan.
- If you see a **REMOTE SCAN**, change your query to filter by PK columns.
- Ensure that local variable/column types and sizes match the remote table's column types exactly — mismatches cause implicit conversions that prevent remote index usage.
