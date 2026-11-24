# Data warehouse connection

Prism reads the data warehouse over its own connection. It never writes to it and never runs migrations against it.

## Local setup

The connection string is a secret. Set it once in user secrets (never in a file in the repo):

```bash
dotnet user-secrets set "ConnectionStrings:Warehouse" "Host=<host>;Port=5432;Database=<db>;Username=<readonly user>;Password=<password>;SSL Mode=VerifyFull" --project src/Prism.Api
```

Use a login that has `SELECT` only: the host checks this at startup and refuses to start otherwise (see Write protection). Without the secret in Development the warehouse is simply unavailable and every query reports that; the rest of the app is unaffected.
Deployed environments get `ConnectionStrings:Warehouse` from AWS Secrets Manager, must set `SSL Mode` to `Require`, `VerifyCA` or `VerifyFull`, and refuse to start without it.

Optional limits, in the `Warehouse` section of `appsettings.json` (defaults shown): `MaxPoolSize` 20, `CommandTimeoutSeconds` 30, `MaxRows` 10000.

## Reaching the warehouse from WSL behind Netskope

The warehouse's DNS name resolves to a private address that ordinary programs cannot reach. Netskope gives some programs (pgAdmin) a steered `100.64.x.x` address for the same name,
and the WSL redirect (`~/.config/wsl-proxy/`) forwards that whole range through the Windows proxy. So the connection string uses that steered address as the host, with `SSL Mode=Require`
(the certificate is for the RDS name). Find the current address with `Get-NetTCPConnection` while pgAdmin is connected, as described in `~/.config/wsl-proxy/README.md`, and check it from WSL with
`nc -vz <address> 2345`. Netskope assigns it, so it can change; repeat the lookup if the connection starts timing out. All deployed databases use port 2345 and the address tells them apart.
Deployed environments connect to the real name directly and require `VerifyCA` or `VerifyFull`.

## How it is kept safe

- **A read-only login is the guarantee.** On top of that every session sets `default_transaction_read_only=on` and a server-side `statement_timeout`, and the client has the same time limit.
  A connection string cannot turn those off: Prism sets them after reading it.
- **One place creates the connection** (`WarehouseDataSourceFactory`). A guard test fails if other code opens one. Prism's own database is a separate connection and never shares the pool.
- **Queries go through `IWarehouse`.** The SQL is a fixed string in the Metric Catalog. Every value is a bound parameter. A query must be a single `SELECT`/`WITH`,
  must filter on `@provider_id`, and must pass that parameter, or it is refused before it reaches the database. The Catalog sets `provider_id` from the route
  after the user's Provider assignments are checked, never from a body or header.
- **Bounds:** a 30 second timeout, a row cap, and a pool cap. A query over the cap fails instead of filling memory, so aggregate in SQL.
- Errors say the warehouse is unavailable and nothing more: no SQL, host or connection string.

## Write protection

**Temporary local override.** While a SELECT-only login is being issued, set `Warehouse:SkipLoginCheck` to `true` (user secrets or an environment variable, never the repo). It skips only the startup check of the login's privileges. It never lets Prism write: every query still runs in a read-only transaction and passes the SQL policy. The host logs an error on every start naming what the check found. A deployed host refuses to start with it set. Remove it as soon as the new login works.

The warehouse is read-only for Prism: no schema change, migration, write or DDL, from any code, test or tool. A change to it is a request to the database team. The design is in `docs/adr/0008-warehouse-write-protection.md`. The layers:

1. **The login.** SELECT only, owns nothing, cannot create anything, not a superuser. This is the real guarantee and belongs to the database team.
2. **Startup check.** In every environment, Development and Testing included, when a connection is configured, the host reads the catalog (read-only, bounded) and refuses to start if the login is a superuser, has CREATEROLE, CREATEDB, REPLICATION or BYPASSRLS, belongs to a privileged role, can CREATE in the database or any schema, owns the database, a schema, a table, a view or a sequence, or can write to any table or view or use any sequence. It also refuses if it cannot check. The error says which kind of privilege and how many objects, with the role name, never the connection string or a password. Temporary tables are logged, not refused (every role has TEMP unless it is revoked). If the login does not default to read-only transactions Prism logs a warning, because every query still runs read-only.
3. **Read-only transaction.** Each query runs after `SET TRANSACTION READ ONLY` and is rolled back, never committed.
4. **SQL policy.** `WarehouseSqlPolicy` refuses write, DDL and administrative verbs, `SELECT INTO`, locking clauses and side-effect functions, reading the text the way PostgreSQL does (literals and comments are ignored, quoted identifiers are checked as function names).
5. **Tests and tools.** Test code only ever connects to a Testcontainers database through `TestDatabaseGuard`. Nothing in tests or tools reads the warehouse connection. There is no migration or `.sql` for the warehouse.

### Example login for the database team

This is an example for the database team to review, adapt and run, if they choose. Prism never runs it and never creates or changes a login. Names, the password, the schema list and the timeout are placeholders.

```sql
-- Review before running. Adapt names, schemas and limits to the real warehouse.
CREATE ROLE prism_ro LOGIN PASSWORD '<set by the database team, kept in Secrets Manager>'
    NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS;

-- Every session of this login is read-only and time limited, whatever the client asks for.
ALTER ROLE prism_ro SET default_transaction_read_only = on;
ALTER ROLE prism_ro SET statement_timeout = '30s';
ALTER ROLE prism_ro SET idle_in_transaction_session_timeout = '60s';

GRANT CONNECT ON DATABASE <warehouse_database> TO prism_ro;

GRANT USAGE ON SCHEMA reporting, ods TO prism_ro;
GRANT SELECT ON ALL TABLES IN SCHEMA reporting, ods TO prism_ro;

-- Tables created later by the role that loads the warehouse (replace loader_role) are readable too.
ALTER DEFAULT PRIVILEGES FOR ROLE loader_role IN SCHEMA reporting, ods GRANT SELECT ON TABLES TO prism_ro;

-- No CREATE anywhere. Nothing is granted on schema public, and CREATE is removed from PUBLIC on older PostgreSQL versions
-- (PostgreSQL 15 and later do not grant it by default).
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
-- Optional: also remove TEMP from PUBLIC if the database team wants no temporary tables at all.
-- REVOKE TEMPORARY ON DATABASE <warehouse_database> FROM PUBLIC;
```

The login must not own any object (a table, view, sequence or schema it owns could be changed by it), and must not be a member of a role that can write. If a read replica or a read-only endpoint exists, point `ConnectionStrings:Warehouse` at it: the server then refuses writes no matter what the login can do.

## Provider ids: portal and warehouse differ

The Account Portal's company id and the warehouse's Provider id are different numberings, and they overlap: Mercy Flights is portal company 2193 and warehouse Provider 2274, while warehouse Provider 2193 is Providence Transportation.
The user is authorized on the portal id (it is the one in `/api/v1/me` and the route). `WarehouseProviderResolver` turns it into the warehouse id before every Metric query:
`ods.company.integration_id` (the portal id) leads to `ods.company.id`, which is `ods.provider.integration_id`, which leads to `ods.provider.id`.
An id with no match, or more than one, is "unmapped": the queries return nothing. It never falls back to using the same number. The answer is cached for 30 minutes (at most 2000 entries).

## Net cash (the first Metric)

`NetCollectionsMetric` reads `reporting.kpi_net_collections_ap__prov`: one row per load (`snapshot_date`), Provider and accounting period (`ap_year_month`,
a fixed-width `yyyyMM`). Only the current load (`is_current`) is read, and `net_collections` is summed per period. Rows where `net_collections` is NULL (no figure yet) are skipped, so an empty month is never shown as zero and never becomes the newest period. Routes, under `/api/v1/providers/{providerId}/metrics/net-collections`:

- `GET /periods`: accounting periods with data, newest first. Fills the Period drop-down.
- `GET /?endPeriod=yyyyMM&months=6&serviceLine=all|air|ground`: the months ending at `endPeriod` (default the newest). `months` is 1 to 24. `all` (default) is the Provider's figure from the KPI table. The KPI table has no air or ground column, so `air` or `ground` is added up from the same payments ledger the breakdown uses (see Net cash breakdown), by the encounter's level of service, and an encounter with no trip row counts as ground. For one month, air plus ground equals the breakdown's groups. Any other `serviceLine` is a 400.

The Provider in the route is checked against the user's portal assignments on every call. A Provider the user is not assigned to, or is limited to some Facilities of, is a 404:
the view is Provider-level and would show Facilities they were not given.

Development signs in through the stage Account Portal, so the Providers are real and no stand-in is needed. If a Provider has no warehouse data, `Warehouse:DevelopmentProviderId` (not set by default, Development only) makes the warehouse be asked about another Provider instead.
The user is still authorized for their own Provider first. A deployed host refuses to start with it set, and the host logs a warning at start-up whenever it is active.

Not in this view, so not in the UI yet: call type, pickup location (Reporting Division), projections.

## Net cash projection

`GET /api/v1/providers/{providerId}/metrics/net-collections/projection?endPeriod=yyyyMM&serviceLine=all|air|ground&horizon=1..3` reads
`reporting.forecast_cash_per_trip` (one row per Provider, month and Service Line, `is_air` tells them apart). Rules:

- Only `is_current` rows. The months are the next three (`month_index >= first day after endPeriod`) that have `total_projected_cash`. The table holds only those three months, so `horizon` (default 3) is 1 to 3 and the first `horizon` months are returned; anything else is a 400 that does not echo the value.
- `value` is `SUM(total_projected_cash)`.
- **Components (database team).** `forecasted_ar_cash` is the expected collections on trips that already existed, and `forecasted_budget_cash` is based on the run rate of trips that do not exist yet but are assumed to be run. Prism reports them as `fromExistingTrips` (`SUM(forecasted_ar_cash)`) and `fromFutureTrips` (`SUM(forecasted_budget_cash)`), each null when the column is null for every row of the month, with `existingTripsShare` and `futureTripsShare` (component divided by the month's `value`, four decimals, null when the value is 0 or the component is null). The same two sums and shares are on the `horizon` total. Say "trips that already existed", not "claims already billed": a trip that already existed may not be billed yet. **Prism does not assume the components add up to `total_projected_cash`**: they are returned as read. In the integration fixture (and by the arithmetic of the seeded rows) the components can total more than `value` (for example 130 + 190 against a value of 150), so the shares can add to more than 1. The owners should confirm how the three columns relate.
- **Range.** `low` and `high` are `LEAST` / `GREATEST` of the month's total and the two component sums (NULLs ignored). It is a range across the forecast components, not a statistical interval, and it does not widen with time. The rule was agreed when the two columns were thought to be alternative forecasts; they are additive components, so the Metric owner and the database team should confirm it (open question in `metric-inventory.md`). It is unchanged for now.
- **Change and running total.** Each month has `changeFromPrevious` (`amount` in USD and `percent`, one decimal, null when the base is 0) and a running `cumulative` (`value`, `low`, `high`) over the returned months. The first month is compared with `latestActual`: the newest month with posted net cash among the six ending at `endPeriod` (the `net_collections` series for the Service Line). With no such month, `latestActual` and the first change are null. `horizon` is `{ value, low, high, months, fromExistingTrips, fromFutureTrips, existingTripsShare, futureTripsShare }` for the returned months, and `total`, `low` and `high` are the same sums.
- **Data Through** is the older of the forecast load and the net cash series it compares with (`MetricDataThrough`).
- **Filters.** Service Line only. The forecast table has no Reporting Division, call type, aircraft or payer column, so none is offered.
- `serviceLine=all` adds air and ground. Anything other than `all`, `air`, `ground` is a 400.
- Fewer months than asked for, or none, is a normal answer. The dashboard says there is no forecast.

## Net cash breakdown

`GET /api/v1/providers/{providerId}/metrics/net-collections/breakdown?period=yyyyMM&serviceLine=all|air|ground` (default `air`) splits net cash for a month, and the month before it,
by aircraft type (air) or level of service (ground), and by base and vehicle. `vehicle` is the tail number (confirmed by the database team); the API field keeps the name `vehicle`. `NetCashBreakdownMetric` reads the transaction ledger, not the KPI table, because the KPI has no vehicle.
It uses the rule behind `kpi_net_collections_ap__prov`, so the groups add up to the headline (checked for Mercy Flights, August 2026):

- `ods.encounter_transaction` rows of type `Credit` whose credit (through `ods.encounter_transaction_credit_attribute`) has `credit_type_id` 2 or 5 (payments and refunds), `balance_amount / 100`.
  A transaction counts once however many attributes it has (`EXISTS`, not a join).
- The month is the accounting period the transaction posted in (`accounting_period_id`), not the date of service. Adjustment periods (`-202608`) belong to the month `202608`.
  Periods are looked up per Provider first, which keeps the query fast.
- `user_source = 'Old AR Import'` is left out.
- The transaction's encounter joins `reporting.trip_detail_snapshot` (newest load for the Provider) for `level_of_service`, `vehicle`, `reporting_division`, `response_type` and `loaded_miles`.
  Air is `Rotor Wing` or `Fixed Wing`. Anything else is ground. An encounter with no trip row is a ground "Not recorded" group.
- **Transports are counted by posting month**: the encounters with net cash posted in the month. They do not match transports by date of service.
- The base is the reporting division's own name (`Mercy Flights Incorporated OR - Fixed`), with any `- Old AR ...` suffix removed so a legacy division counts under its current one. `<none>` is no base.
- The mix is scene and interfacility transports for air, emergency and non-emergency for ground (`response_type`).
- Not read: any patient column of the trip table. Only aggregates leave the query.

## Payer mix

`GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix` gives the share of transports by primary payer for one aircraft type or level of service (`group`, a key from the breakdown route), in a month and the month before. It reads `reporting.trip_detail_snapshot`, latest load only, one row per encounter.

- **Month:** the date of service (`dos`), not the posting month. Payer mix counts transports as they happened. The breakdown's transport counts are encounters with cash posted in the month, so the two will differ.
- **Groups:** Commercial is `Insurance` and `TPL`. Medicare is `Medicare` and `Medicare Advantage`. Medicaid is `Medicaid` and `Medicaid MCO`. `Other Govt. Payers` is Other government. `Facility` and `Patient` are their own groups. A transport with no payer shows as Not recorded, and only when there is one.
- **VA:** the warehouse has no VA category. A payer whose name contains the word VA, CHAMPVA (any `CHAMP...`) or TRICARE is VA, whatever financial class it has. For Mercy that is mostly `IV-VA COMMUNITY CLAIMS`, filed under Insurance. The match is on whole words, so a name that only contains the letters (AVA HEALTH) is not VA.
- **Open months:** the newest months have few transports until billing catches up on payers, so their shares move.
- Only counts leave the query. The payer mix route never returns payer names (the payer detail below does, see there).

### Payer mix detail

`GET /api/v1/providers/{providerId}/metrics/net-collections/payer-mix/{payerKey}` (Metric `payer_mix_detail`) lists the payers inside one payer group of the payer mix. Each payer mix share has a `drill` link to it, issued by the server with the Provider from the route and the filters in effect (`group`, `period`, `serviceLine`, `division`). It reads the same `reporting.trip_detail_snapshot` rows, with the same latest load, service line, `group` and `division` filters and the same `PayerGroupSql` case, so its rows add up to the group's share on the payer mix.

- **Groups with detail:** `commercial`, `medicare`, `medicaid`, `other-gov` and `va`. `patient`, `facility`, `not-recorded` and any other key are a 404 (the key is never echoed): those groups have no payers worth comparing, and Facility and Patient are billing parties.
- **Rows:** payers are grouped by `prim_pay`. The 10 with the most transports in the month and the month before together are listed (ties by name), and one `Other` row sums the rest, so at most 11 rows come back whatever the number of payer names. The ranking and the cut are in SQL. A real payer called Other is shown as `Other payer`.
- **Names:** control characters and runs of spaces become one space, the name is trimmed and cut to 120 characters in SQL before grouping, and an empty or missing name is `Unknown payer`. A name is returned as text only; it is not written to logs.
- **Counts only:** no patient, claim or encounter detail leaves the query. The route needs the dashboard permission like every Metric route, and a Facility-limited user gets a 404 as on the other Metric routes.
- **Cache:** an answer is cached against Data Through like the other Metrics, keyed on period, service line, group, division and payer group.

## Transports and loaded miles

`GET /api/v1/providers/{providerId}/metrics/net-collections/transports` feeds the Transports and Loaded miles tiles on the dashboard. It takes `endPeriod`, `months` (default 6, at most 24) and `serviceLine`, and returns transports and loaded miles per month plus the split of the last month by aircraft type or level of service. Each group also carries the month before the last: `priorTransports`, `changeTransports` and `changePercent` (percent units, one decimal, null when the prior month had none), on the same date-of-service basis and with the same Service Line and Division filters. The query reaches one month before the series when `months` is 1. A group with no transports in the last month is not listed, even if it had some the month before.

- **Source:** `reporting.trip_detail_snapshot`, latest load only, one row per encounter.
- **Month:** the date of service (`dos`). A transport counts in the month it happened. This differs from the net cash breakdown, which counts the encounters that had cash posted in the month, so the two counts will not match.
- **Transports:** every encounter row in the month. The snapshot has no cancelled status to exclude. `status` is a billing status (New, Pending, Submitted, Approved, Closed, On_Hold).
- **Loaded miles:** the sum of `loaded_miles`, treated as statute miles. The Nautical switch converts at 0.868976 nautical miles per statute mile. A missing mileage counts as no miles. `loaded_miles` is statute miles (confirmed by the database team), so the Nautical conversion is valid.
- **Open months:** `periodOpen` is true while the last month's accounting period (or its adjustment period) is open. New trips can still arrive, so the tiles say the count may rise.

## Clean claim rate

`GET /api/v1/providers/{providerId}/metrics/net-collections/clean-claims` feeds the Clean claim rate tile. It takes `endPeriod` and `months` (default 6, at most 24) and returns, per month of service, claims accepted first time, rejected, the total and the `rate` (clean divided by total, 0 to 1, null when there were no claims).

- **Source:** `reporting.kpi_clean_claim_rate__prov`, latest `snapshot_date` for the Provider. The `vw_snap_...` view keeps only `is_current` rows, and no rows are flagged current, so Prism does not use it.
- **Pods:** the table can split a month by pod. If a month has a row without a pod, that row is the whole Provider and is used alone. Otherwise the pods are added up. Mercy has no pods.
- **Not split by Service Line:** the tile says so when air or ground is chosen.
- **Data is behind:** on 2026-11-21 the table held a single load (2026-04-07) with January to March 2026 only, and an empty `rolling_12m_avg`. The tile shows the newest month the warehouse has and says which month it is ("Latest in the data: March 2026."), or says there is no data. It never shows an older month as the month on screen.
- **Goal:** the 95% mark on the tile is a placeholder from the mockup. It is not in the warehouse and becomes a Provider setting later.

## Payment age, cash per transport and net cash by group

Three Metrics feed the "What changed" panel. Each is also a public API Metric under `/api/v1/providers/{providerId}/metrics/net-collections/`.

- **`/payment-age`:** net cash by payer group, posting month and age, for the chosen period and the 12 months before it. Age is the posting month minus the transport's date-of-service month (capped at 24). Cash for a transport with no date of service is left out, so the cells can add up to less than the breakdown. Age is whole months only: the ledger has no day-level payment date, so no day figures are given. `payers` summarises each payer group over all the cells: `typicalAgeMonths` (the cash-weighted median age in months, the first age at which running cash reaches half the total; null when the group has no cash) and `bands`, the share (0 to 1, four decimals, summing to 1 within rounding) of the group's cash received at ages 0-1, 2-3, 4-6, 7-12 and 13+ months (`PaymentAgeBands`, the one place the bands are defined; empty when there is no cash). Only positive cells weigh in, so refunds do not push a share out of range. This covers inventory 5.3 (payment timing by payer); the planned `payment_timing_by_payer` entry stays in the catalog.
- **`/cash-per-transport`:** matured net cash per transport by payer group. The cohorts are the 12 date-of-service months that ended at least **6 months** before the chosen period. Every transport counts, paid or not, and cash is everything posted up to and including the chosen period. The response adds `overall` (`transports`, `cash`, `cashPerTransport`): the same measure across all payer groups for the same period, Service Line and Division but without the `group` (aircraft type) filter, which costs a second read when `group` is set. Each payer group has `differenceFromOverall` (USD, null when the group or overall has no transports) and `percentFromOverall` (percent units, one decimal, null when overall is 0 or null). `cohorts` is `{ firstServiceMonth, lastServiceMonth, cashThroughPeriod }` (`yyyyMM`): the service months and the payment period used. The lookback stays 12 cohort months matured by 6 months (decided with the user); the 18 months in the inventory are not used.
- **`/by-group`:** net cash by posting month for each aircraft type or level of service, for the chosen period and the 12 months before it. A group's month equals the same group in the breakdown.
- **One rule for cash and payers:** the net cash rule (payment and refund credits, by posting period, without the Old AR import) is `NetCashSql`, and the payer grouping is `PayerGroupSql`. Every Metric that adds up the ledger or groups payers uses them.

## What changed (insights)

`GET /api/v1/providers/{providerId}/insights/what-changed` explains the change in posted net cash for a group against the month before. It is for the dashboard and is not a public API contract. It answers 204 when neither month has cash.

- **Analyses read Metrics, never the warehouse.** Aged payments (payment age), volume, payer mix (payer mix and cash per transport), aircraft mix and base mix (the net cash breakdown) run in a fixed order so their effects do not overlap.
- **Drivers and the remainder add up to the change.** A payer mix shift is shown beside them as an expected effect, because payer mix is by date of service and cash is by posting month.
- **Aircraft mix and base mix (`aircraft-mix`, `base-mix`).** The mix part of the usual price, volume and mix split of the change in posted cash, from the transports with cash posted and the cash in each aircraft type (or level of service) or Reporting Division, this month and last (net cash breakdown, always for the whole Provider, never one Reporting Division). With `Tn` and `Tp` the transports this month and last, `r` a segment's cash per transport last month and `R` the average of all segments last month, the effect is the sum over segments of (`Tn` in the segment minus its last-month share of `Tn`) times `r`, which equals the sum of (shift) times (`r` minus `R`). Volume plus mix plus rate is the change, so mix is a part of the posted change. A segment with no transports last month is priced at `R` and adds nothing. The finding names the segment with the largest effect and shows its share of transports before and after. Needs two or more segments, transports in both months and a move of at least one point of share.
  - **Aircraft mix is a Driver** (basis posted cash). It is skipped when one group is chosen, because there is nothing to move between.
  - **Base mix is a second cut of the same effect.** Types and divisions cannot both be subtracted, so it is a Driver only when no aircraft-mix finding exists (a group is chosen, or the types did not move). Otherwise it is basis `AlternativeCut`: it is returned in `expected` (beside the Drivers), says it is another view of the same change, and is never subtracted from the remainder.
  - **The Drivers are not fully independent.** Aged payments and volume are computed after a batch of unusually high aged payments is taken out of both months, but the mix is computed on the raw posted cash and transports. When a batch is large, the mix effect can pick up some of it, and the remainder (change minus the Drivers) absorbs the difference. It still adds up exactly.
  - The label is Watch when the mix lowered cash and Info when it raised it. Unlike payer mix, "held for more than a month" (Structural) is not checked, so a mix finding is never called Structural.
  - Cash on an encounter with no trip row sits in the breakdown's Not recorded ground group but not in net cash by group (the headline), so for ground and all service lines the breakdown and the headline can differ by that cash. Air is not affected.
- **Claim status and denial drivers are not built.** They would need a history of claim status by load, and the warehouse keeps only the newest load of each claim, so a month-over-month "denied rose by $X" cannot be worked out and is not estimated. Denied claims are reached from the review page.
- **Aged payments:** a payment is aged when it is older than the age by which 90% of that payer group's transports are normally paid. The excess over the average of the six months before the two compared is the finding. A batch needs at least 3 transports. It says "unusually high payments on older transports", not "one-time", because the warehouse has no payment date or one-time flag.
- **Remainder:** called normal only when it is within one standard deviation of the last 12 months' changes, and only when all 12 months have cash.
- **Thresholds:** a finding is shown at 10% of the size of the change or more, at most 5. They are constants in `InsightThresholds` and are meant to be tuned against real months.
- **Cache:** results are kept for 10 minutes, keyed on Provider, period, Service Line, group and the Data Through date, with a size cap of 500 and at most 4 computed at once.
- **Not built:** an LLM and "Ask about this view". Denied claims are reached from the review page (see Claims needing attention), not computed here: a Finding never carries claim detail.

## Claims needing attention

`GET /api/v1/providers/{providerId}/claims/attention/counts`, `.../{checkKey}` and `.../{checkKey}/export` read claim rows from the latest `reporting.trip_detail_snapshot` load, by date of service in the period. They need `prism.claims.view`; the list and the export write an audit row (`audit_entry`: who, which Provider, which check, period, row count, never claim data).

- **Columns read (an explicit list; the table also holds patient names, SSNs, birth dates and addresses, which are never selected):** `encounter_id`, `run_number`, `dos`, `prim_pay`, `prim_pay_category`, `vehicle`, `reporting_division`, `status`, `denial_code`, `denial_reason`, `balance_due`, `in_collections`, `level_of_service`, `snapshot_date`. The columns and rules live in `ClaimAttentionSql`.
- **Denied:** a denial code and `balance_due > 0`. **Needs attention:** Denied, `status = 'On_Hold'`, or `in_collections = 'Yes'`. **All:** every claim in the period.
- **Claim reference:** `run_number`, or `encounter_id` when it is blank. Confirm this is the number customers know.
- **Counts response additions** (still claims permission, never cached, `no-store`, no ETag, no audit row: they are aggregates):
  - `deniedRate`: denied claims divided by all claims in the period (0 to 1, four decimals), null when there are no claims.
  - `reasons`: the top 10 denial reasons by denied claim count (ties by balance due, then text), each with `count` and `balanceDue`, then one `isOther` row for the rest. The reason text is untrusted warehouse text, cleaned in SQL: input bounded to 2,000 characters, control characters and whitespace runs become one space, trimmed, cut to 120 characters, trimmed again, empty or missing is `No reason given`. A real reason called Other is shown as `Other reason`. The text is returned as text only (the client escapes it) and is never logged.
  - `categories`: `denied`, `on_hold` and `in_collections`, each with a claim count and `balanceDue` (only positive balances are added). A claim can be in more than one category, so they overlap and must not be added.
  - `filingBands`: the needs-attention claims (denied, on hold or in collections) and their balance due by days left from Data Through: `past_due` (below 0), `due_within_10_days` (0 to 10, so due today is not past due), `due_within_30_days` (11 to 30) and `due_later`. The bands use the derived deadline below, so they are only as good as the placeholder filing limits.
  - No assigned owner, and no IDR negotiation, patient-due or missing-documentation category: the warehouse has no source for them.
- **Balance due is not an expected payment.** The table has no estimate, so the page shows what is still owed and says so.
- **Filing deadline is derived, not read.** Due date = date of service + the filing limit for the payer category (`Attention:FilingLimitDays`, 365 days by default, per `prim_pay_category` when set). Days left count from Data Through. The limits are a placeholder until the real ones are supplied.
- **Bounds:** list pages of at most 100; the warehouse row cap (10,000) applies, and more matching claims returns 422 instead of a truncated list. Claim rows are never cached.
- Facility-restricted users get 404, as on every Metric route.

## Call type, fleet, payer effect and pickup Metrics

Six Metrics read `reporting.trip_detail_snapshot` (latest load, one row per encounter) and, for net cash, the transaction ledger. They share the routes' query parameters: `period` (`yyyyMM`, the month and the month before it), `serviceLine`, optional `group` (aircraft type or level of service key, as in the breakdown), optional `division` (a division key), and `months` (6 or 12, default 6) only where a trend is returned. Shared SQL: `TripSql` (latest load, Service Line, group, accounting periods, text cleaning), `CallTypeSql`, `PickupSql`, `DivisionSql`, `PayerGroupSql` and `NetCashSql`.

- **Basis, always stated.** Transports and loaded miles are by date of service (`dos`). Net cash is by posting month (the ledger rule in `NetCashSql`, with the adjustment period folded in). A figure called **net cash per transport** is posting-month cash divided by date-of-service transports. It is a ratio of two bases, **not the expected payment** of a transport: the warehouse has no expected-payment source per transport, so no Metric here estimates one.
- **Cash join.** Net cash is attributed through the encounter's trip row (latest load). An encounter that has cash but no trip row has no tail, division, call type or pickup source and is not counted in these Metrics, so their cash can add up to less than the breakdown (which keeps such cash as a ground Not recorded group).
- **Bounds.** Every query has the warehouse layer's 30 second timeout and 10,000 row cap, and the database bounds the group-by: top 100 tails or divisions plus one Other row, top 10 tails per division, top 20 facilities, at most 12 months. `months` other than 6 or 12 is a 400. `group` is at most 40 characters of `a-z`, `0-9` and `-`, and `division` a key of the route Provider.
- **Warehouse text is untrusted.** Tail numbers and facility names are cleaned in SQL (control characters and runs of spaces become one space, trimmed, cut to 120 characters, blank or missing becomes `Not recorded`) before they are grouped, are returned as text only, and are never written to logs. A real value of `Other` is shown as `Other tail` or `Other facility` so it cannot be mistaken for the roll-up row.
- **Aggregate only.** No patient, claim or encounter detail leaves a query.
- **Service Line.** The two tail-number Metrics are **air only** (`context.appliesTo` is `air`): `serviceLine=air` and `all` both mean air and `ground` is a 400. Every other Metric here applies to both.

### Transport mix by call type (`transport_mix_by_call_type`)

`GET .../metrics/net-collections/transport-mix-by-call-type`. A call type is `scene` or `interfacility` for air (`response_type` starting with Scene or Interfacility) and `emergency` or `non-emergency` for ground (`Emergency`, `Non-Emergency`), otherwise `not-recorded`. Per call type: transports, share of all transports (0 to 1) and point change against the month before, loaded miles, the payer mix of its transports (share by payer group of that call type) and net cash. The trend has one point per month of the window with the transports and share of each call type. **Net cash by call type is by posting month and is a separate figure from the counts.** Expected payment per transport by call type is not available and not estimated.

### Aircraft performance (`aircraft_performance`)

`.../aircraft-performance`. By tail number (`vehicle`, confirmed as the tail number). Transports and loaded miles (statute) by date of service, net cash by posting month, each with the prior month, change and change percent (null when the prior month had none), and net cash per transport (see above). The top 100 tails by transports in the month (then the prior month, then cash, then name) are listed with one Other row. A tail with cash but no transports in the window is still listed if it ranks. Air only.

### Base analysis (`base_analysis`)

`.../base-analysis`. By Reporting Division: the warehouse's name with the legacy `- Old AR` suffix folded (`DivisionSql.NormalizedName`), matched to the Provider's division key from the directory (a name the directory lacks has a null key; no division is `Not recorded`). Same figures as aircraft performance, plus the tail numbers operating from each division in the month with their transport counts (top 10 per division plus an Other entry; aggregate counts only). The top 100 divisions are listed with one Other row that lists no tails.

### Loaded miles by aircraft (`loaded_miles_by_aircraft`)

`.../loaded-miles-by-aircraft`. By tail number: loaded miles, transports and the average loaded miles per transport for the month (null when no transports), and a monthly trend of miles, transports and the average over 6 or 12 months (zeros for months without transports). Tails are ranked by the month's miles, then the window's miles. Top 100 plus Other, including the Other trend. Air only.

### Financial effect of payer mix (`payer_mix_financial_effect`)

`.../payer-mix-financial-effect`. It reads no table of its own: it combines `payer_mix` (share of transports by payer group, by date of service) with `cash_per_transport_by_payer` (matured cash per transport: the 12 service months that ended at least 6 months before the period) through `PayerMixEffect`, the one calculation the "what changed" Insight also uses (a test compares the two). Per payer group: current and prior share, point change, matured cash per transport and the effect (change in share times cash per transport times this month's transports). The estimated effect is the sum. **It is an expected effect, not posted cash**, and the recurring monthly effect is the same number on the assumption that the shift persists. Figures are null when a month has no transports or a group has no matured transports.

### Transports by pickup location (`transports_by_pickup_location`)

`.../transports-by-pickup-location`. `reporting.trip_detail_snapshot` has `origin_facility`, `origin_mod`, `origin_address`, `origin_city`, `origin_state` and `origin_zip`.

- **PHI.** On a scene or home pickup the origin address, city, state and zip can be a patient's location, so under `AGENTS.md` section 1 they are PHI. **No query selects `origin_address`, `origin_city`, `origin_state` or `origin_zip`**, and a test asserts it on the SQL text. Pickups are classified with `origin_mod` and grouped by `origin_facility` only for facility-based sources.
- **`origin_mod`** is the CMS ambulance origin modifier (confirmed by the database team). Its single-letter values are mapped as **general CMS ambulance billing knowledge, to be confirmed against the warehouse values and not verified in Prism**: S scene (`scene`), R residence (`residence`), E residential, domiciliary or custodial facility (`custodial-facility`), H hospital (`hospital`), N skilled nursing facility (`skilled-nursing`), P physician's office (`physician-office`), I site of transfer such as an airport or helipad (`transfer-site`), D diagnostic or therapeutic site (`diagnostic-site`), G and J dialysis (`dialysis`, together). The match ignores case and surrounding spaces. Anything else (blank, null, more than one character, an unknown letter) is `other`. The mapping lives in `PickupSql.SourceCase` and `ITransportsByPickupLocationMetric.Sources`, and a test keeps them equal. The destination modifier column is not confirmed and is not used.
- **Rows.** Per source: transports, share, prior month and point change, loaded miles, payer mix and net cash (posting month). Scene, residence and custodial facility are single buckets with no facility name. Other also lists none, because an unknown modifier cannot be trusted to be a facility. The facility-based sources are also split by `origin_facility`: the 20 facilities with the most transports across the whole result are listed (ties by prior month, source, name) and one Other row sums the rest. A facility with no name is `Not recorded`.
- **Open:** the set of `origin_mod` values present in the warehouse, and the name of the destination modifier column.

## Cash change by aircraft, Reporting Division and payer group

Three Metrics answer "who moved cash?" for the month against the month before: `cash_contribution_by_aircraft` (tail numbers, air only), `cash_contribution_by_division` and `cash_contribution_by_payer`, at `.../metrics/net-collections/cash-contribution/aircraft`, `/division` and `/payer`. They share one query shape (`ContributionSql`) and the net cash rule (`NetCashSql`: payments and refunds by posting month, without the Old AR import, the adjustment period counted in its month), attributed to the contributor on the encounter's trip row from the newest load, so an encounter with no trip row is not counted (the same population as net cash by group, which is the Insight headline, and a test checks that they agree).

- **Contributions add up exactly.** Each contributor is its net cash this month minus last month. The 25 that moved cash most by absolute change are listed, then one `Other` row sums the rest. `total` is the sum of the rows, so rows (Other included) always add up to `total.change`. There is no hidden unexplained part: the three Metrics split the same scope three ways. An `Other` row exists only when there are more than 25 contributors.
- **Contributors.** Tail numbers are cleaned in SQL like aircraft performance (a real tail called Other is shown as Other tail). Divisions are the warehouse name with the legacy "- Old AR" suffix folded away, keyed from the Provider's directory (key null for a name the directory does not hold, and for trips with no division, shown as Not recorded). Payer groups are the shared `PayerGroupSql` keys; only keys and fixed names leave the query, never a payer name.
- **Filters.** `serviceLine`, `group` (aircraft type or level of service), `division` and `period`. The aircraft Metric is air only (`AppliesTo` air): `all` means air and ground is a 400. A 12-month lookback is not needed: only the month and the one before are read.
- **`shareOfChange`** is a row's change divided by the total change, null when the total did not change. It can be above 1 or below 0 when contributors offset each other. The payer rows carry a drill to `payer_mix_detail` for payer groups that have detail.
- **Not built:** contribution by aircraft *type* as its own Metric (the breakdown and the aircraft mix finding cover types), and contribution to transports or loaded miles (the inventory item is about cash).

## Data Through

`snapshot_date` is the load date: a date with no time of day. Prism shows it as Data Through and does not claim a "last updated" time. Per-Metric tables and folders are listed in `metrics.md`.

- **One reader.** `IDataThroughReader` (`DataThroughReader`) returns the newest `snapshot_date` a Provider has in a table, through `IWarehouse` with the Provider filter, the standard timeout and row cap. Every aggregate Metric response carries it as `dataThrough` (`yyyy-MM-dd`, null when the Provider has no data in the table). The Metric cache key and the Insights use the same load date, so the figure on screen, the cache and the validators agree.
- **Which table.** `MetricDataThrough` names the source of each Metric. Trip-table Metrics (payer mix, transports, payment age, cash per transport, net cash by group) read `reporting.trip_detail_snapshot`. The ledger Metrics (net cash breakdown, and the air or ground series of net cash) read the transaction ledger, which has no load date, and join the trip table, so the trip table's date is theirs. Net cash for all Service Lines reads `reporting.kpi_net_collections_ap__prov` (current load), the projection `reporting.forecast_cash_per_trip` (current load) and the clean claim rate `reporting.kpi_clean_claim_rate__prov`. One Metric reads two load-dated tables: the projection compares its first month with the latest actual net cash, so its Data Through is the oldest of the forecast load and that series (the KPI table for all Service Lines, the trip table for air or ground). That is the rule for any such Metric: the oldest of their dates (the most cautious claim), and null if any of them has none.
- **Cached for 60 seconds** per warehouse Provider id and table, in a cache of at most 2,000 entries, using the injected `TimeProvider`. A new load shows within a minute.
- Claim-level routes (Claims needing attention) read their own `snapshot_date` with the rows and are not cached.

## Reporting Division filter

The trip-table Metrics (`payer_mix`, `payer_mix_detail`, `transport_volume`, `payment_age`, `cash_per_transport_by_payer`, `net_cash_breakdown`, `net_cash_by_group`, `transport_mix_by_call_type`, `aircraft_performance`, `base_analysis`, `loaded_miles_by_aircraft`, `payer_mix_financial_effect`, `transports_by_pickup_location`, `cash_contribution_by_aircraft`, `cash_contribution_by_division` and `cash_contribution_by_payer`) take an optional `division` query parameter. All of them read `reporting.trip_detail_snapshot` (the payer mix effect through the payer mix and cash per transport Metrics), whose `reporting_division` column holds the division name. `net_collections`, `net_cash_projection` and `clean_claim_rate` read tables with no division and ignore the parameter.

- **Keys.** The warehouse names a division in text; the API uses a stable key. Name rule (`DivisionSql.NormalizedName`): a "- Old AR ..." suffix is removed (a legacy division counts under its current one), and an empty value or `<none>` is no division. Key rule (`ReportingDivision.FromNames`): lower case, each run of characters other than letters and digits becomes one hyphen, no hyphen at the ends, at most 60 characters, `division` if nothing is left. Two names with the same key are numbered `-2`, `-3` in name order. `GET .../metrics/net-collections/divisions` lists the keys and names of the Provider (latest load, at most 500, cached 10 minutes).
- **Validation.** The value must be at most 60 characters of `a-z`, `0-9` and `-`, or the answer is a 400 before anything is looked up. It must then be one of the route Provider's own keys from the directory, or it is a 400. A key is never resolved from a display name, and another Provider's key is unknown to this Provider. An empty value means every division.
- **Filter.** The name behind a key goes to the query as a bound parameter (`@division`, empty for all divisions) and is compared with the normalized `reporting_division` of the Provider's latest load. The Provider filter stays in the Catalog query. The ledger Metrics (breakdown, payment age, by group) join the trip table, so a division keeps only cash for encounters that have a trip row in that division.
- **Facility scope.** A Facility-limited user never reaches the division check: every aggregate route is a 404 for them, as for the whole Provider, so a sibling division is refused too. The portal's Facility ids (`ods.location.integration_id`) have no known link to the division names in the trip table, so the filter cannot be intersected with a user's Facilities. Narrow access would need that link first.
- **Response.** Each of those responses also carries `division` (the key, or null) and `divisionName`. The cache key includes the division key.

## Metric cache

Aggregate Metrics are wrapped by a caching decorator (`Cached*Metric`, registered through `AddCachedMetric` in each Metric's module) over `IMetricCache` (`MemoryMetricCache`).

- **Key:** the portal Provider id the route access check returned, the Metric key, its normalized parameters (period, months, Service Line, group, division) and the Data Through date. A new load changes the key, so old figures are never served for new data. The route checks the Provider against the user's assignments before any Metric is called, and the key includes the Provider, so one Provider can never read another's entry.
- **Bounds:** at most 1,000 answers (one size unit each), a 10 minute ceiling on the lifetime, judged by the injected clock, and at most 8 answers computed at once overall and 2 per Provider (`ProviderGate`: a request waits on its own Provider's gate before it takes a global slot, so one Provider cannot starve the others, and waiting honors cancellation). The table of Provider gates holds only Providers with a request in flight. A second caller of the same key waits and then hits. A Provider with no Data Through date is computed every time and not kept. A failed computation is not kept.
- **Not cached:** claim-level reads (`IClaimAttention`) are never wrapped. A guard test fails if a decorator implements it.
- **Logs** say the Metric key and hit or miss at Debug level, never a parameter value or a Provider id.
- The Insights service keeps its own cache (10 minutes, 500 entries), keyed on the same Data Through watermark from `IDataThroughReader` (trip table), and reads the Metrics through this cache as well. The `dataThrough` of the Insight and of the claim Attention Checks (trip table, never cached) also comes from the shared reader.
