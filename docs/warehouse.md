# Data warehouse connection

Prism reads the data warehouse over its own connection. It never writes to it and never runs migrations against it.

## Local setup

The connection string is a secret. Set it once in user secrets (never in a file in the repo):

```bash
dotnet user-secrets set "ConnectionStrings:Warehouse" "Host=<host>;Port=5432;Database=<db>;Username=<readonly user>;Password=<password>;SSL Mode=VerifyFull" --project src/Prism.Api
```

Use a login that has `SELECT` only. Without the secret in Development the warehouse is simply unavailable and every query reports that; the rest of the app is unaffected.
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

`GET /api/v1/providers/{providerId}/metrics/net-collections/projection?endPeriod=yyyyMM&serviceLine=all|air|ground` reads
`reporting.forecast_cash_per_trip` (one row per Provider, month and Service Line, `is_air` tells them apart). Rules:

- Only `is_current` rows. The months are the next three (`month_index >= first day after endPeriod`) that have `total_projected_cash`.
- `value` is `SUM(total_projected_cash)`. The likely range is `LEAST` / `GREATEST` of that sum and the sums of `forecasted_ar_cash` and
  `forecasted_budget_cash` (NULLs ignored). The warehouse has no confidence interval, so this range is a product decision, agreed with the owner.
- `serviceLine=all` adds air and ground. Anything other than `all`, `air`, `ground` is a 400.
- Fewer than three months, or none, is a normal answer. The dashboard says there is no forecast.

## Net cash breakdown

`GET /api/v1/providers/{providerId}/metrics/net-collections/breakdown?period=yyyyMM&serviceLine=all|air|ground` (default `air`) splits net cash for a month, and the month before it,
by aircraft type (air) or level of service (ground), and by base and vehicle. `NetCashBreakdownMetric` reads the transaction ledger, not the KPI table, because the KPI has no vehicle.
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

`GET /api/v1/providers/{providerId}/metrics/net-collections/transports` feeds the Transports and Loaded miles tiles on the dashboard. It takes `endPeriod`, `months` (default 6, at most 24) and `serviceLine`, and returns transports and loaded miles per month plus the split of the last month by aircraft type or level of service.

- **Source:** `reporting.trip_detail_snapshot`, latest load only, one row per encounter.
- **Month:** the date of service (`dos`). A transport counts in the month it happened. This differs from the net cash breakdown, which counts the encounters that had cash posted in the month, so the two counts will not match.
- **Transports:** every encounter row in the month. The snapshot has no cancelled status to exclude. `status` is a billing status (New, Pending, Submitted, Approved, Closed, On_Hold).
- **Loaded miles:** the sum of `loaded_miles`, treated as statute miles. The Nautical switch converts at 0.868976 nautical miles per statute mile. A missing mileage counts as no miles. **Confirm with the warehouse owners that `loaded_miles` is statute.**
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

- **`/payment-age`:** net cash by payer group, posting month and age, for the chosen period and the 12 months before it. Age is the posting month minus the transport's date-of-service month (capped at 24). Cash for a transport with no date of service is left out, so the cells can add up to less than the breakdown.
- **`/cash-per-transport`:** matured net cash per transport by payer group. The cohorts are the 12 date-of-service months that ended at least **6 months** before the chosen period. Every transport counts, paid or not, and cash is everything posted up to and including the chosen period.
- **`/by-group`:** net cash by posting month for each aircraft type or level of service, for the chosen period and the 12 months before it. A group's month equals the same group in the breakdown.
- **One rule for cash and payers:** the net cash rule (payment and refund credits, by posting period, without the Old AR import) is `NetCashSql`, and the payer grouping is `PayerGroupSql`. Every Metric that adds up the ledger or groups payers uses them.

## What changed (insights)

`GET /api/v1/providers/{providerId}/insights/what-changed` explains the change in posted net cash for a group against the month before. It is for the dashboard and is not a public API contract. It answers 204 when neither month has cash.

- **Analyses read Metrics, never the warehouse.** Aged payments (payment age), volume, and payer mix (payer mix and cash per transport) run in a fixed order so their effects do not overlap.
- **Drivers and the remainder add up to the change.** A payer mix shift is shown beside them as an expected effect, because payer mix is by date of service and cash is by posting month.
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
- **Balance due is not an expected payment.** The table has no estimate, so the page shows what is still owed and says so.
- **Filing deadline is derived, not read.** Due date = date of service + the filing limit for the payer category (`Attention:FilingLimitDays`, 365 days by default, per `prim_pay_category` when set). Days left count from Data Through. The limits are a placeholder until the real ones are supplied.
- **Bounds:** list pages of at most 100; the warehouse row cap (10,000) applies, and more matching claims returns 422 instead of a truncated list. Claim rows are never cached.
- Facility-restricted users get 404, as on every Metric route.

## Data Through

`snapshot_date` is the load date: a date with no time of day. Prism shows it as Data Through and does not claim a "last updated" time. Per-Metric tables and folders are listed in `metrics.md`.

- **One reader.** `IDataThroughReader` (`DataThroughReader`) returns the newest `snapshot_date` a Provider has in a table, through `IWarehouse` with the Provider filter, the standard timeout and row cap. Every aggregate Metric response carries it as `dataThrough` (`yyyy-MM-dd`, null when the Provider has no data in the table). The Metric cache key and the Insights use the same load date, so the figure on screen, the cache and the validators agree.
- **Which table.** `MetricDataThrough` names the source of each Metric. Trip-table Metrics (payer mix, transports, payment age, cash per transport, net cash by group) read `reporting.trip_detail_snapshot`. The ledger Metrics (net cash breakdown, and the air or ground series of net cash) read the transaction ledger, which has no load date, and join the trip table, so the trip table's date is theirs. Net cash for all Service Lines reads `reporting.kpi_net_collections_ap__prov` (current load), the projection `reporting.forecast_cash_per_trip` (current load) and the clean claim rate `reporting.kpi_clean_claim_rate__prov`. No Metric reads two load-dated tables today. If one does, its Data Through is the oldest of their dates (the most cautious claim), and null if any of them has none.
- **Cached for 60 seconds** per warehouse Provider id and table, in a cache of at most 2,000 entries, using the injected `TimeProvider`. A new load shows within a minute.
- Claim-level routes (Claims needing attention) read their own `snapshot_date` with the rows and are not cached.

## Reporting Division filter

The trip-table Metrics (`payer_mix`, `transport_volume`, `payment_age`, `cash_per_transport_by_payer`, `net_cash_breakdown`, `net_cash_by_group`) take an optional `division` query parameter. All six read `reporting.trip_detail_snapshot`, whose `reporting_division` column holds the division name. `net_collections`, `net_cash_projection` and `clean_claim_rate` read tables with no division and ignore the parameter.

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
