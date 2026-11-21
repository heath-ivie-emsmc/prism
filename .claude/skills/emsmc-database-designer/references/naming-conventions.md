# Naming Conventions — Full Reference

## Identifier Max Length

| Engine     | Max Length |
|------------|-----------|
| PostgreSQL | 63        |
| SQL Server | 128       |
| MySQL      | 63        |

## General Rules

- **Casing**: Always lowercase for all engines.
- **Word separation**: Always `snake_case`.
- **Quoting**: PostgreSQL `"x"`, SQL Server `[x]`, MySQL `` `x` ``.

## Tables

- `snake_case`, lowercase.
- Junction tables (many-to-many): `<parent_table>_<child_table>` where parent is the stronger/categorizing entity. If the combined name sounds awkward, choose a more natural name.

**Examples:**
```
customer
order_item
customer_role        -- junction: customer (parent) + role (child)
trip_diagnose        -- junction: trip (parent) + diagnose (child)
```

## Columns

- `snake_case`, lowercase.
- **PK**: Always `id`.
- **FK columns**: End with `_<referenced_table>_<referenced_column>` when natural.
  - Example: `customer_id` in `order` references `customer.id`
  - Example: `ordering_facility_id` in `trip` references `facility.id`
  - If the name is ugly, too long, or unclear, shorten sensibly.
- **Booleans**: `is_` prefix or verb prefix (`has_`, `contains_`, `combines_`). Must be `NOT NULL` with `DEFAULT`.
- **Date columns**: `_date` suffix (e.g., `birth_date`).
- **Datetime columns**: `_datetime` suffix (e.g., `created_datetime`).
- **Timestamp columns**: `_timestamp` suffix (e.g., `created_at_timestamp`), except `row_version`.

## Views

- Prefix: `vw_`
- Format: `vw_<descriptive_name>`
- Example: `vw_active_customers`, `vw_monthly_revenue`

## Stored Procedures

- Prefix: `usp_`
- First word after prefix must be an infinitive verb.
- Example: `usp_calculate_billing`, `usp_generate_report`
- Limit parameters to 5.

## Functions

- Prefix: `fn_`
- First word after prefix must be an infinitive verb.
- Example: `fn_get_full_name`, `fn_set_timestamps`
- Limit parameters to 5.

## Partition Functions (SQL Server)

- Prefix: `pfn_`
- First word after prefix must be an infinitive verb.
- Example: `pfn_partition_by_month`

## Synonyms

- Use the same name as the base object, OR:
- Prefix: `sn_` + the base object's prefix.
- Example: `sn_vw_active_customers`

## Indexes

- Prefix: `ix_`
- Format: `ix_<table>__<col1>__<col2>__<col3>`
- Double underscore (`__`) separates table name from columns and between columns.
- Include `__<function>_<column>__` if the index uses a function on a column (not SQL Server).
- Append `__include` if it has an INCLUDE clause (SQL Server).
- FILLFACTOR = 90 (PostgreSQL / SQL Server).

**Truncation:**
1. Remove column names, add `__plus` suffix.
2. If still too long, remove all columns and add numeric suffix: `ix_<table>_1`

**Examples:**
```sql
-- Full name
CREATE INDEX "ix_trip_diagnose__trip_id__diagnose_id__id"
    ON "trip_diagnose" ("trip_id", "diagnose_id", "id")
    WITH (FILLFACTOR = 90);

-- Truncated (too long)
CREATE INDEX "ix_trip_diagnose__trip_id__plus"
    ON "trip_diagnose" ("trip_id", "diagnose_id", "id", "status")
    WITH (FILLFACTOR = 90);
```

## Primary Keys

- Prefix: `pk_`
- Format: `pk_<table_name>`
- FILLFACTOR = 100 (PostgreSQL / SQL Server).

```sql
-- PostgreSQL
CONSTRAINT "pk_customer" PRIMARY KEY ("id") WITH (FILLFACTOR = 100)

-- SQL Server
CONSTRAINT [pk_customer] PRIMARY KEY ([id]) WITH (FILLFACTOR = 100)
```

## Foreign Keys

- Prefix: `fk_`
- Format: `fk_<referencing_table>__<referencing_col>__<referenced_table>`
- Double underscore between parts.
- NEVER use ON DELETE CASCADE or ON DELETE SET NULL.

**Truncation:**
1. Remove referenced table name.
2. If still too long, remove columns and add numeric suffix: `fk_<table>_1`

```sql
-- Full name
CONSTRAINT "fk_order__customer_id__customer"
    FOREIGN KEY ("customer_id") REFERENCES "customer" ("id")

-- Truncated
CONSTRAINT "fk_order__customer_id"
    FOREIGN KEY ("customer_id") REFERENCES "customer" ("id")
```

## Unique Keys

- Prefix: `uk_`
- Format: `uk_<table>__<col1>__<col2>`
- FILLFACTOR = 90.

**Truncation:** Same pattern as indexes — `__plus`, then numeric suffix.

```sql
CONSTRAINT "uk_customer__email" UNIQUE ("email") WITH (FILLFACTOR = 90)
```

## Check Constraints

- Prefix: `ck_`
- Format: `ck_<table>__<column>`

```sql
CONSTRAINT "ck_order__total_amount" CHECK ("total_amount" >= 0)
```

## Triggers

- Prefix: `trg_`
- Format: `trg_<table>__<purpose>`

```sql
CREATE TRIGGER "trg_customer__timestamps" ...
CREATE TRIGGER "trg_order__audit_log" ...
```

## Enums (PostgreSQL, MySQL)

- Prefix: `enum_`
- Values: PascalCase with underscores. Must match application code exactly.

```sql
CREATE TYPE "enum_order_status" AS ENUM ('Pending', 'In_Progress', 'Completed', 'Cancelled');
```

## Domains (PostgreSQL)

- Prefix: `dm_`

```sql
CREATE DOMAIN "dm_email" AS varchar(320)
    CHECK (VALUE ~ '^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$');
```

## Sequences

- Prefix: `seq_`

```sql
CREATE SEQUENCE "seq_order_number" START WITH 1000 INCREMENT BY 1;
```
