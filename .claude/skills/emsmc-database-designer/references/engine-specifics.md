# Engine-Specific Reference

## Data Type Mapping

### Primary Key

| PostgreSQL | SQL Server | MySQL |
|-----------|------------|-------|
| `int4 NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 MINVALUE 1 INCREMENT BY 1 NO CYCLE)` | `int NOT NULL IDENTITY(1, 1)` | `int NOT NULL AUTO_INCREMENT` |
| `int8 NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 MINVALUE 1 INCREMENT BY 1 NO CYCLE)` | `bigint NOT NULL IDENTITY(1, 1)` | `bigint NOT NULL AUTO_INCREMENT` |

### Strings

| PostgreSQL | SQL Server | MySQL | Notes |
|-----------|------------|-------|-------|
| `varchar(n)` | `nvarchar(n)` | `varchar(n)` | SQL Server uses `nvarchar` (Unicode) — EMS standard |
| `text` | `ntext` or `nvarchar(max)` | `text` | SQL Server uses `ntext`/`nvarchar(max)` — EMS standard |

### Booleans

| PostgreSQL | SQL Server | MySQL |
|-----------|------------|-------|
| `bool NOT NULL DEFAULT false` | `bit NOT NULL DEFAULT 0` | `tinyint NOT NULL DEFAULT 0` |

Always `NOT NULL` with a `DEFAULT` value. Column name uses `is_` or verb prefix.

### Date and Time

| Concept | PostgreSQL | SQL Server | MySQL |
|---------|-----------|------------|-------|
| Date only | `date` | `date` | `date` |
| Date + time (UTC) | `timestamp` | `datetime2` | `datetime` |
| Row version | `xid` or app-managed `int` | `rowversion` | app-managed `int` |

- All timestamps are stored in **UTC**.
- PostgreSQL: use `timestamp` (without time zone). The application and triggers use `AT TIME ZONE 'UTC'` to ensure UTC storage.
- SQL Server: `TIMESTAMP` is deprecated. Use `ROWVERSION` for concurrency and `datetime2` for actual date-time values.

### JSON

| PostgreSQL | SQL Server | MySQL |
|-----------|------------|-------|
| `jsonb NOT NULL DEFAULT '{}'::jsonb` | `nvarchar(max)` (with `ISJSON()` check) | `json` |

PostgreSQL: always use `jsonb` (not `json`). Always set a default value.

### Numeric

| Concept | PostgreSQL | SQL Server | MySQL |
|---------|-----------|------------|-------|
| Small integer | `int2` / `smallint` | `smallint` | `smallint` |
| Standard integer | `int4` / `int` | `int` | `int` |
| Large integer | `int8` / `bigint` | `bigint` | `bigint` |
| Decimal | `numeric(p,s)` | `decimal(p,s)` | `decimal(p,s)` |
| Money | `numeric(19,4)` | `decimal(19,4)` | `decimal(19,4)` |

Avoid engine-specific money types (`money` in PostgreSQL/SQL Server) — use `numeric`/`decimal` for portability and precision control.

## Engine-Specific Syntax

### Table Creation with FILLFACTOR

**PostgreSQL:**
```sql
CREATE TABLE "customer" (
    ...
) WITH (FILLFACTOR = 90);
```

**SQL Server:**
```sql
-- FILLFACTOR is specified on the PK/index, not the table itself
CREATE TABLE [customer] (
    ...
    CONSTRAINT [pk_customer] PRIMARY KEY ([id]) WITH (FILLFACTOR = 100)
);
```

**MySQL:** FILLFACTOR is not supported. Omit it.

### Identity / Auto-Increment

**PostgreSQL:**
```sql
"id" int4 NOT NULL GENERATED ALWAYS AS IDENTITY (START WITH 1 MINVALUE 1 INCREMENT BY 1 NO CYCLE)
```

**SQL Server:**
```sql
[id] int NOT NULL IDENTITY(1, 1)
```

**MySQL:**
```sql
`id` int NOT NULL AUTO_INCREMENT
```

### Quoting

| Engine | Style | Example |
|--------|-------|---------|
| PostgreSQL | Double quotes | `"customer"` |
| SQL Server | Square brackets | `[customer]` |
| MySQL | Backticks | `` `customer` `` |

### Enums

**PostgreSQL:** Native `CREATE TYPE ... AS ENUM`.
```sql
CREATE TYPE "enum_status" AS ENUM ('Active', 'Inactive');
-- Column:
"status" "enum_status" NOT NULL DEFAULT 'Active'
```

**SQL Server:** No native enum. Use a check constraint:
```sql
[status] nvarchar(20) NOT NULL DEFAULT 'Active',
CONSTRAINT [ck_customer__status] CHECK ([status] IN ('Active', 'Inactive'))
```

**MySQL:** Native `ENUM` type:
```sql
`status` ENUM('Active', 'Inactive') NOT NULL DEFAULT 'Active'
```

### Trigger Syntax

See the trigger templates in the main SKILL.md. Key differences:
- PostgreSQL: `BEFORE INSERT OR UPDATE` trigger calling a shared function.
- SQL Server: `AFTER INSERT, UPDATE` trigger with `inserted`/`deleted` pseudo-tables.
- MySQL: Separate `BEFORE INSERT` and `BEFORE UPDATE` triggers (no combined trigger).

## Snowflake Notes

When targeting Snowflake:
- All identifiers are UPPERCASE.
- Use fully qualified names: `DATABASE_NAME.SCHEMA_NAME.OBJECT_NAME`.
- No FILLFACTOR (not applicable).
- VARCHAR is the standard string type.
- VARIANT for semi-structured data (JSON).
