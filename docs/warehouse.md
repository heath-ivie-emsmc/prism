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
- Only counts leave the query. Payer names are used inside it and never returned.

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

## Not yet done

- Caching results by Provider, query and the Data Through watermark.
