# EMSight Air analytics inventory: Prism status mapping

This maps every analytic in the EMSight Air Analytics Inventory (sections 1 to 8) to what Prism has today. It uses the vocabulary and rules in [`metric-design-guide.md`](metric-design-guide.md) and the Metric table in [`metrics.md`](metrics.md). The inventory describes candidate analytics, not the final MVP scope, and its prototype values are illustrative. AI-generated explanations, questions, summaries and recommendations are out of scope, as in the inventory.

This is a snapshot of the code at the time of writing. Where a statement rests on a source other than the code, it says so. Items marked "confirm" need an answer from the warehouse or business owners before anyone relies on them.

## How to read the status

| Status | Meaning |
|---|---|
| **Built** | An Available Metric (or feature) covers the item. Gaps against the inventory are listed. |
| **Partial** | Part of the item exists, inside another Metric or feature, or with a narrower definition. |
| **Planned** | A Planned entry exists in the Metric Catalog. It has no API and cannot be placed. |
| **Not built** | No Metric, no catalog entry, or no source identified. |

## Vocabulary: inventory term to Prism term

| Inventory says | Prism says | Notes |
|---|---|---|
| Transport mode (Air, Ground, Air + Ground) | **Service Line** (`serviceLine` (air, ground or all)) | Air is Rotor Wing and Fixed Wing. Everything else is ground. |
| Base location | **Reporting Division** (`division` key) | A Provider's Label can rename it (Air Providers say base). The key never changes. |
| Aircraft type | `group` (aircraft type for air, level of service for ground) | A key from the net cash breakdown. |
| Tail number | `vehicle` column | Confirmed by the database team. The API field keeps the name `vehicle`. |
| Call type | `response_type` column | Scene and interfacility for air, emergency and non-emergency for ground. |
| Pickup location | `origin_facility`, classified by `origin_mod` | The trip table also has `origin_address`, `origin_city`, `origin_state` and `origin_zip`, which are PHI on a scene pickup (a patient's home) and must not be returned without the PHI permission and an audit record. `origin_mod` is the CMS origin modifier (S scene, H hospital, R residence and so on, general CMS knowledge to be confirmed against the data). The destination modifier column name is not confirmed. No Metric is built yet. See `warehouse.md`. |
| Payer class | payer group | Commercial, Medicare, Medicaid, Other government, VA, Facility, Patient responsibility, Not recorded. |
| Trip, run | **Transport** | |
| Client | **Provider** | The tenant. |

## Dimension support

Which of the inventory's recurring filters Prism can apply today. "Filter" means a query parameter that narrows a Metric. "Breakdown" means the figures are split by it but you cannot choose a value.

| Inventory dimension | Prism today | Where |
|---|---|---|
| Transport mode | Filter | Every Metric takes `serviceLine`, except `clean_claim_rate`, which is not split by Service Line and says so. |
| Period | Filter | One month (accounting period or month of service, depending on the Metric), always compared with the month before. No custom range. |
| Last 6 or 12 months | Filter | The `months` option on `net_collections`, `transport_volume` and `clean_claim_rate`. |
| Base location | Filter on trip-table Metrics | `division` is supported by the three `cash_contribution_by_*` Metrics, `payer_mix`, `payer_mix_detail`, `transport_volume`, `payment_age`, `cash_per_transport_by_payer`, `net_cash_breakdown`, `net_cash_by_group`, the fleet, call type, payer effect and pickup Metrics, `net_collections` (from the ledger joined to the trip table when a division is chosen) and the `what_changed` Insight. Not supported: `net_cash_projection` (the forecast table has no division column) and `clean_claim_rate` (split by pod only, with no known pod-to-division link). Also a breakdown by base in `net_cash_breakdown`. |
| Aircraft type | Filter and breakdown | `group` on `payer_mix`, `payer_mix_detail`, `payment_age` and `cash_per_transport_by_payer`. A breakdown in `net_cash_breakdown`, `net_cash_by_group` and `transport_volume`. |
| Tail number | Breakdown only | `net_cash_breakdown` rows carry `vehicle` (the tail number) with cash, transports and loaded miles. No filter. |
| Call type | Breakdown only | Scene and interfacility counts (air) or emergency and non-emergency (ground) in `net_cash_breakdown`. No filter. |
| Pickup location | None | A source is identified (see the vocabulary table) but no Metric or filter is built. |
| Payer class | Breakdown, and a drill | `payer_mix` splits by payer group. `payment_age` and `cash_per_transport_by_payer` split by it. `payer_mix_detail` is filtered to one group. No payer-class filter on the other Metrics. |
| Specific payer or plan | Drill only | `payer_mix_detail` lists the top 10 payers in one group plus Other. Patient responsibility, Facility and Not recorded have no detail. |
| Actual vs projected | Separate Metric | `net_cash_projection` is its own Metric. There is no toggle on a single series. |
| Claim status, denial reason, deadline | Claim-level feature | Claims needing attention (see 6.4 to 6.7). Needs the claims permission and writes an audit record. |
| Statute vs nautical miles | Dashboard switch | On the Loaded miles tile for air. Ground is always statute. |

## Item by item

Each row names the Prism Metric (the catalog key) or feature, its status, what exists, and the gaps against the inventory. "Drill" means a server-issued drill link.

### 1. Revenue and cash

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 1.1 | Net cash collected | `net_collections`, `net_cash_breakdown` | Built | Net cash by accounting period (the period the payment posted, not the service date), 6 or 12 months, by Service Line. The dashboard panel shows the latest month, the percent change against the prior month and a table with the change by month. The breakdown gives current and prior cash by aircraft type and by base and vehicle. | No dollar-change figure found. Aircraft type, tail, call type, pickup and payer class are not filters on the headline series. `division` is supported on `net_collections`, from the ledger and trip table (cash with no trip row or no division is in no division, so the divisions add up to the headline less that cash). Actual vs projected is a separate Metric. |
| 1.2 | Net cash trend | `net_collections`, `net_cash_by_group` | Built | Monthly series, 6 or 12 months. `net_cash_by_group` gives the month-by-month series for each aircraft type, for the chosen month and the 12 before. | No filters beyond Service Line and Division. "Unusual payment events" appear only through the aged-payments Finding in `what_changed`. |
| 1.3 | Projected net cash | `net_cash_projection` | Built | The next one to three months (`horizon`, default 3) from `reporting.forecast_cash_per_trip`. Each month has the projected value, a range across the forecast components (the lower and higher of the total and the two component sums, not a statistical interval), `changeFromPrevious` (the first month compares with the latest actual posted net cash month, `latestActual`, when there is one), a running `cumulative` and a `horizon` total. | The table holds only three months, so the horizon cannot exceed 3. No Division, call type, pickup or payer-class filters: the table has none (Service Line only). The range rule was agreed when the two components were thought to be alternatives (see open question 4). |
| 1.4 | Cash from trips that already existed | `net_cash_projection` | Built | `fromExistingTrips` (`forecasted_ar_cash`: expected collections on trips that already existed) and `existingTripsShare` of the month's projected value, per month and for the horizon. | Not "claims already billed": a trip that already existed may not be billed yet. There is no claim-status breakdown of it. The components are returned as read and are not assumed to add up to the total (see open question 4). |
| 1.5 | Cash from future transports | `net_cash_projection` | Built | `fromFutureTrips` (`forecasted_budget_cash`: based on the run rate of trips that do not exist yet but are assumed to be run) and `futureTripsShare`, per month and for the horizon. | The volume and payer-mix assumptions behind the run rate are not in the warehouse, so the inventory's "assumed volume" input does not exist. |
| 1.6 | Expected payment per transport | `expected_payment_per_transport` (catalog) | Planned | The nearest built Metric is `cash_per_transport_by_payer`, which is historical (see 5.2), not expected. | No expected-payment model. No comparison against the overall Air average. |

### 2. Transport analytics

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 2.1 | Total transports | `transport_volume` | Built | Transports per month by date of service, with a trend and the change against the prior month, by Service Line and Division. Months are 6 or 12 on the dashboard (up to 24 in the API). | No tail, call type, pickup or payer-class filter. The count is by date of service. The breakdown counts encounters by posting month, so the two will not match (documented in `warehouse.md`). |
| 2.2 | Transports by aircraft type | `transport_volume`, `net_cash_breakdown` | Built | The latest month split by aircraft type (air) or level of service (ground), each group with the prior month's transports and the change (count and percent) on the date-of-service basis, by Service Line and Division. `net_cash_breakdown` gives current and prior transports per group by posting month. | No tail, call type, pickup or payer-class filters. A group with no transports in the latest month is not listed, even if it had some the month before. |
| 2.3 | Scene vs interfacility mix | `transport_mix_by_call_type` (also `net_cash_breakdown` counts) | Built | Per call type (scene and interfacility for air, emergency and non-emergency for ground, from `response_type`): transports, share, prior month and point change, loaded miles, payer mix of each call type, net cash by posting month as a separate labelled figure, and a 6 or 12 month trend of the shares. Service Line, aircraft type or level of service (`group`) and Reporting Division filters. | Expected payment per transport by call type is not available in the warehouse and is not estimated. Whether the split should come from `origin_mod` instead of `response_type` is not decided (open question 2). |
| 2.4 | Transports by pickup location | `transports_by_pickup_location` | Built | Transports, share, prior month and point change, loaded miles, payer mix and net cash (posting month) per pickup source, a bucket of `origin_mod` (scene, residence, custodial facility, hospital, skilled nursing, physician office, transfer site, diagnostic site, dialysis, other). Facility-based sources also list sending facilities from `origin_facility` (top 20 plus Other). Scene, residence and custodial pickups are single buckets with no name. Address, city, state and zip are never selected (a test asserts it on the SQL). | The `origin_mod` letter mapping is general CMS ambulance billing knowledge and is not yet confirmed against the warehouse values (open questions 1 and 2). A modifier with more than one character, a blank or an unknown letter is Other, so the real set of values may move rows out of Other. No destination modifier is used: its column name is not confirmed. |
| 2.5 | Transports by payer class | `payer_mix` | Built | Transport counts and shares by payer group for one aircraft type or level of service, in the month and the month before, with the point change. Drill to `payer_mix_detail`. By Service Line and Division. | The inventory has six payer classes. Prism also has Facility and Not recorded. The inventory filters (base, call type, pickup) are not all available: Division yes, call type and pickup no. |

### 3. Aircraft, fleet and base

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 3.1 | Net cash by aircraft type | `net_cash_by_group`, `net_cash_breakdown` | Built | Cash per aircraft type per month, plus current and prior in the breakdown. Division supported. `net_cash_by_aircraft_type` is a Planned catalog entry that these cover in effect. | Tail, call type, pickup and payer-class filters. |
| 3.2 | Contribution to cash movement | `cash_contribution_by_aircraft`, `cash_contribution_by_division`, `cash_contribution_by_payer`, `what_changed` (Insight) | Built | For the month against the month before, how much each tail number (air only), Reporting Division and payer group added to or took from the change in net cash (payments less refunds by posting month, without the Old AR import). The 25 largest movers are listed and one Other row sums the rest, so the rows always add up exactly to the total change. Each row has its net cash, prior net cash, change and share of the change. Service Line, aircraft type (`group`) and Reporting Division filters. The Insight adds aircraft mix and base mix findings. | Aircraft type has no contribution Metric of its own (the breakdown and the aircraft mix finding cover it). No claim-status dimension (the warehouse keeps no claim status history). Contribution is to cash only, not to transports or miles. |
| 3.3 | Aircraft-level performance | `aircraft_performance` (also `net_cash_breakdown` rows) | Partial | Per tail number (`vehicle`, air only): transports and loaded miles by date of service, net cash by posting month, each with the prior month and the change, and net cash per transport (posting-month cash divided by transports, not the expected payment). Top 100 tails by transports plus Other, blank tails as Not recorded. | Expected cash per transport by tail is not available: there is no expected payment source per transport (see 5.4 for the matured cash per transport by payer, which is not per tail). |
| 3.4 | Base-level performance | `base_analysis` (also `net_cash_breakdown` rows) | Partial | Per Reporting Division (key and the warehouse name, legacy suffix folded): transports and loaded miles by date of service, net cash by posting month, each with the prior month and the change, net cash per transport (posting-month cash divided by transports, not the expected payment), and the tail numbers operating from each division with their transport counts (top 10 plus Other). Top 100 divisions plus Other. | Expected payment per transport by base is not available (no source per transport). |

### 4. Mileage

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 4.1 | Loaded miles | `transport_volume` | Built | Loaded miles per month beside transports, with a Statute or Nautical switch for air (conversion 0.868976). Ground stays statute. | `loaded_miles` is statute (confirmed by the database team), so the Nautical conversion is valid. No normalization between air and ground units. No tail, call type or pickup filter. |
| 4.2 | Loaded miles by aircraft | `loaded_miles_by_aircraft` | Built | Per tail number (air only): loaded miles (statute) and transports in the month, the average loaded miles per transport (null with no transports) and a 6 or 12 month trend of miles, transports and the average. Top 100 tails by the month's miles plus Other. | None known. Nautical miles are not offered here (the transport volume tile has the switch). |

### 5. Payer analytics

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 5.1 | Payer mix | `payer_mix` | Built | Share and count of transports by payer group, current and prior, point change, by Service Line, aircraft type or level of service, and Division. Month of service basis. | Pickup location and call type filters. |
| 5.2 | Historical net paid per transport by payer | `cash_per_transport_by_payer` | Built | Matured net cash per transport by payer group, with `overall` (all payer groups, same period, Service Line and Division, without the aircraft-type filter) and per payer `differenceFromOverall` and `percentFromOverall`. `cohorts` names the service months and the payment period used. The cohorts are the 12 months of service that ended at least 6 months before the chosen month. Every transport counts, paid or not. | The inventory cites an 18-month history. Prism keeps 12 cohort months matured by 6 months (decided with the user). Service period and payment period are shown, not selectable. |
| 5.3 | Payment timing by payer | `payment_age` (covers it), `payment_timing_by_payer` (catalog, stays Planned) | Partial | Net cash by payer group, posting month and age (posting month minus service month, capped at 24 months), and per payer group `typicalAgeMonths` (cash-weighted median) and `bands`: the share of cash received at 0-1, 2-3, 4-6, 7-12 and 13+ months. 5.3 is covered by `payment_age`; the `payment_timing_by_payer` entry is kept as planned and is not a separate build. | Age is whole months only: the ledger has no day-level payment date, so there are no days to payment. No future-month distribution of expected payments. The inventory's per-payer assumptions (for example Medicare 14 to 30 days) are not encoded anywhere. |
| 5.4 | Financial effect of payer-mix changes | `payer_mix_financial_effect`, `what_changed` Insight | Built | Per payer group: current and prior share, point change, matured cash per transport and the estimated effect on the month's cash. Totals: the estimated mix effect and the recurring monthly effect, which is the same number on the stated assumption that the shift persists. It is an expected effect, not posted cash, and shares one calculation (`PayerMixEffect`) with the Insight, so the two agree. | Null figures when a month has no transports or no group has matured cash per transport. The recurring effect is an assumption, not a forecast. |

### 6. Revenue cycle and claims

Claim-level views are a separate feature with their own permission and audit record (see ADR 0005), not catalog Metrics. A count of flagged claims is an aggregate. The claims behind it are claim-level data.

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 6.1 | Clean claim rate | `clean_claim_rate` | Built | Claims accepted first time, rejected, total and the rate per month of service, from `reporting.kpi_clean_claim_rate__prov`. The tile shows the latest month the warehouse has. | The 95% goal is a placeholder from the mockup, not in the warehouse. Not split by Service Line or Division. The warehouse table was behind at last check (one load, January to March 2026). The production definition (population, exclusions, first pass vs final) is still open. |
| 6.2 | Accounts receivable days | `ar_days` (catalog) | Not built | none | No source and no definition identified. |
| 6.3 | Open negotiations | `open_negotiations` (catalog) | Not built | none | No source. The No Surprises Act window and business-day rule are not modelled. |
| 6.4 | Denied claims | Claims needing attention, "Denied" check | Partial | A denial code with money still owed (`balance_due > 0`). The counts now carry `deniedRate` (denied over all claims in the period) and `reasons` (top 10 denial reasons by claim count with balance due, plus one Other row; text cleaned and cut to 120 characters). The list shows the claims with the claims permission and writes an audit row. CSV export is available. | Balance due is not an expected payment, so the page says what is owed. No split of initial, final and overturned denials: no source. `denied_claims` stays a Planned catalog entry. |
| 6.5 | Claims needing attention | Claims needing attention, "Needs attention" check | Partial | Denied, `On_Hold` status, or in collections. The counts carry `categories`: separate counts and balance due for Denied, On hold and In collections (they can overlap). | Categories in the inventory (IDR negotiation, patient due, missing documentation) have no source. No assigned owner: no source. |
| 6.6 | Dollar value at risk | `dollars_at_risk` (catalog) | Not built | The attention page shows what is still owed, and the counts give balance due by category (denied, on hold, in collections) and by filing band. | No expected collectible value. |
| 6.7 | Timely-filing risk | Claims needing attention, `timely_filing_risk` (catalog) | Partial | The filing deadline is derived (date of service plus a filing limit per payer category, 365 days by default) and days left count from Data Through. The counts carry `filingBands`: needs-attention claims and balance due that are past due, due within 10 days, within 30 days, and later. | The limits are a placeholder until the real payer rules are supplied, so the bands are estimates. The 10 and 30 day bands are Prism's choice (constants in `FilingBands`) and are not confirmed by the business owner. |
| 6.8 | Claim status distribution | `claim_status_distribution` (catalog) | Not built | The `status` column is a billing status (New, Pending, Submitted, Approved, Closed, On_Hold). | The inventory's taxonomy (denied, IDR, patient due, in review, insurance pending, paid) needs a mapping from the source statuses. |

### 7. Location and source

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 7.1 | Pickup-location performance | `transports_by_pickup_location` | Partial | Transports, miles, payer mix and net cash (posting month) by pickup source, and by sending facility for facility-based sources. See 2.4 and `warehouse.md`. Address, city, state and zip are never returned. | Expected payment per transport by source is not available. Patient-level or address-level pickup detail would need the PHI permission and an audit record and is not built. The `origin_mod` mapping is unconfirmed. |
| 7.2 | Sending facility vs scene | `transports_by_pickup_location`, `transport_mix_by_call_type` | Partial | Loaded miles, payer mix and net cash (posting month) split by source type (scene, residence, facility-based), and by call type. | Expected payment per transport by source or call type is not available in the warehouse. Reconciling `response_type` with `origin_mod` is not decided (open question 2). |

### 8. Diagnostics

All of this lives in the "what changed" Insight, which is dashboard-only and never carries patient or claim detail.

| # | Inventory item | Prism | Status | What exists | Gaps against the inventory |
|---|---|---|---|---|---|
| 8.1 | Drivers of cash change | `what_changed` | Partial | Drivers (aged payments, volume, aircraft mix) add up with the remainder to the change in posted cash. Payer mix is shown beside them as an expected effect, and base mix as another cut of the same change when aircraft mix already explains it (a Driver when it does not). A finding appears at 10% of the change or more, at most 5. | No claim status or denial drivers: they need a history of claim status by load, and the warehouse keeps only the newest load, so they cannot be computed and are not estimated. The Drivers are not fully independent (the mix is computed on raw posted cash, after aged payments are taken out of volume only), and the remainder absorbs the difference. A mix finding is never Structural because it is not checked against the month before. The thresholds are constants meant to be tuned against real months. |
| 8.2 | One-time payment normalization | none | Not built | The aged-payments Finding says "unusually high payments on older transports" and deliberately does not say "one-time". | No identified one-time payments, no normalized change. |
| 8.3 | Temporary vs persistent change | none | Not built | Aged payments (timing) and volume are separate Findings. | No temporary-versus-persistent classification or ongoing monthly impact. |
| 8.4 | Residual variation | `what_changed` | Built | The remainder is called normal when it is within one standard deviation of the last 12 months' changes, and only when all 12 months have cash. | The inventory's percent-of-movement figure is not shown. |

## Shared filter and drill-down framework (inventory section 9)

| Group | Status today |
|---|---|
| Time | Current month and prior month: the period picker, always compared with the month before. Last 6 or 12 months: the `months` option. Custom period: not supported. Service, billing and payment date: not a selector. Each Metric has a fixed date basis (posting month for cash, date of service for transports and payer mix). Projection horizon: 1 to 3 months (`horizon`), because the forecast table holds three. |
| Transport mode and operation | Air, ground and both: Service Line. Rotor and fixed wing: `group`. Base: `division`. Tail, scene and interfacility, pickup location: breakdown only or not available (see the dimension table). |
| Payer | Six payer classes plus Facility and Not recorded as a split. Specific payer: `payer_mix_detail` only. |
| Claims and revenue cycle | The attention page covers claim status, denial reason and deadline. Negotiation status, assigned owner and attention categories beyond Denied, On_Hold and in collections are not available. |
| Units and presentation | Statute and nautical miles: yes. Point change and percentage change: yes (for example "+7 pts"). Table, trend, comparison and composition: bar, line, stacked, donut and table presentations, with a table view for every Metric. |

Drill-down today: net cash breakdown group card to `payer_mix`, then a payer-mix share to `payer_mix_detail` (top 10 payers plus Other). Saved View widgets drill the same way. The claim-level end of a drill is the attention page, behind the claims permission. There is no drill from a Metric to the underlying transports or payments.

## Inventory section 10: what each Metric definition needs, and where Prism records it

| Required field | Where Prism holds it today | Gap |
|---|---|---|
| 1. Business definition | Catalog `Description`, and the help summary and glossary in `web/src/app/metrics/<key>/help.en.ts` | The decision each Metric supports is not written down. |
| 2. Formula | Counting rules in prose in [`warehouse.md`](warehouse.md) and the SQL | No single formula statement per Metric, and rounding is not documented. |
| 3. Date basis | `warehouse.md` and the help text (posting month vs date of service) | None for the built Metrics. |
| 4. Population | `warehouse.md` (for example the Old AR import is excluded, air versus ground) | Exclusions are in prose only. |
| 5. Source system | `warehouse.md`, and `MetricDataThrough` for the tables read | None for the built Metrics. |
| 6. Refresh cadence | Catalog `Freshness` (`DailyLoad`) and the Data Through stamp | The warehouse load time of day is not recorded. |
| 7. Dimensions | Catalog `Dimensions` and `Context` | See the dimension table above. |
| 8. Reconciliation | The breakdown groups add up to the headline (checked for one Provider and month). `payer_mix_detail` rows sum to the payer-mix share (tested). | No reconciliation path from an aggregate to underlying payments for cash Metrics. |
| 9. Targets and thresholds | None. The clean-claim 95% mark is a placeholder, and the Insight thresholds are tunable constants. | Needs the business owner. |
| 10. Ownership | Not recorded | Needs the business owner. |

## Open questions to confirm

Answered by the database team and no longer open: `loaded_miles` is statute; `vehicle` is the tail number; `origin_mod` is the CMS ambulance origin modifier; `forecasted_ar_cash` is the expected collections on trips that already existed and `forecasted_budget_cash` is the run rate of trips that do not exist yet.

1. What is the set of `origin_mod` values present in the warehouse, and which of them are scene or home pickups? What is the name of the destination modifier column (likely to exist, not confirmed)? Prism buckets only single letters S, R, E, H, N, P, I, D, G, J and puts everything else in Other.
   1a. Please confirm the CMS mapping used for the pickup source (S scene, R residence, E residential or custodial facility, H hospital, N skilled nursing facility, P physician's office, I site of transfer, D diagnostic or therapeutic site, G and J dialysis) against the warehouse values. It is general CMS ambulance billing knowledge, not verified in Prism.
   1b. Expected payment per transport by call type, pickup source, tail number or base is not available in the warehouse. Is there a source? Until then Prism shows net cash per transport (posting-month cash divided by transports) and says it is not an expected payment.
2. Should the scene versus interfacility counts (2.3, 7.2) be derived from or reconciled with `origin_mod` instead of `response_type`? (Metric owner.)
3. Is the claim number customers know `run_number`? (`warehouse.md` flags this.)
4. The projection range (`low` to `high`) is the lower and higher of the total and the two component sums. That rule was agreed when `forecasted_ar_cash` and `forecasted_budget_cash` were thought to be alternative forecasts. They are additive components, so the Metric owner and the database team should confirm the rule. They should also confirm how `total_projected_cash` relates to the two components: Prism returns all three as read and does not assume they add up (in test data the components total more than the value).
5. What are the real timely-filing limits per payer? Prism uses a 365-day placeholder, and the 10 and 30 day bands are its own choice.
6. What is the production definition of a clean claim, and where does the 95% target live?
7. Is there a source for AR days, open negotiations, IDR status, patient-due status, missing documentation and an assigned owner?
8. Who owns each Metric definition, and what are the targets and warning thresholds?

Decided with the user: payer-level cash per transport keeps Prism's 12 cohort months matured by 6 months, not the 18 months cited in the inventory.

## Inventory section 11: the suggested first group against today

| Suggested initial metric | Status |
|---|---|
| Net cash collected | Built (`net_collections`) |
| Transport volume | Built (`transport_volume`) |
| Loaded miles | Built (in `transport_volume`) |
| Clean claim rate | Built, but the warehouse data was behind and the definition is open |
| Payer mix | Built (`payer_mix`, with drill to `payer_mix_detail`) |
| Expected payment per transport | Planned, no model yet |
| Denied claims | Partial (the attention page) |
| Timely-filing risk | Partial (derived deadlines with placeholder limits) |
| Open negotiations | Not built, no source identified |
| Aircraft and base drill-downs | Partial: breakdown rows by base and vehicle, and a drill from the breakdown to payer mix |
