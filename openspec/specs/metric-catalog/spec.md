## Purpose
The Metric Catalog is the single set of Metric definitions owned by the Prism API. Dashboard cards, Saved Views, the public API and its OpenAPI document all read from it. This spec characterizes what is implemented today: what a catalog entry declares, how Provider, Service Line and Reporting Division context is applied and shown, how Data Through, caching, Drills and help text work, the resource bounds on every query, and the available Metric families. Derived from the Metric design guide, the Metric inventory, ADR 0006 and 0007, CONTEXT.md, AGENTS.md sections 2 and 4, and the catalog, drift, scope, cache and Drill tests. Claim-level reads (Attention Checks) and Saved View storage are specified in `claims-attention` and `saved-views`.

## Requirements

### Requirement: Single catalog definition
Each Metric SHALL be defined once in the Metric Catalog, and dashboard cards, Saved Views, the public API and the OpenAPI document SHALL all read that definition. Keys SHALL be unique, lowercase snake_case and stable, because Saved Views store them. The catalog SHALL be readable through an authenticated catalog route, cacheable privately for a short time on a 200 response only.

#### Scenario: Catalog feeds every consumer
- **GIVEN** an Available Metric in the catalog
- **WHEN** the catalog route, the OpenAPI document and the dashboard palette are read
- **THEN** each shows the same key, name, description and unit

#### Scenario: Drift fails the build
- **GIVEN** a catalog entry with no backend module, no web renderer or no help entry, or the reverse
- **WHEN** the tests run
- **THEN** the build fails
- **AND** the generated Metric table and the catalog fixture must match the catalog or the build fails

#### Scenario: Unauthenticated catalog read
- **GIVEN** a caller with no session
- **WHEN** they request the catalog
- **THEN** the response is 401 or 403 and no entry is returned

### Requirement: Catalog entry fields
Every catalog entry SHALL declare a key, name, description, unit, category, status (Available or Planned), Placeable flag, claim-level flag, accepted Service Lines, dimensions, presentations and options. It SHALL also declare its context (Provider scoped, applies to air, ground or both, Reporting Division support, group dimensions, time grain), freshness policy, help key and Drills. An Available entry SHALL have exactly one default presentation and SHALL be readable as a table. Its help key SHALL equal its key. Its Service Lines SHALL follow from what it applies to: both lists air, ground and all, air lists air and all, ground lists ground and all.

#### Scenario: Planned Metric cannot be placed
- **GIVEN** a Metric with status Planned
- **WHEN** a Saved View names it or the API is asked for its figures
- **THEN** it is rejected, because Planned Metrics have no API and are shown only as coming soon

#### Scenario: Drill-only Metric is not placeable
- **GIVEN** an Available Metric that is a Drill target with no presentations
- **WHEN** a Saved View tries to place it
- **THEN** it is rejected because it is not placeable

#### Scenario: Claim-level Metric never Available
- **GIVEN** an entry marked claim-level
- **THEN** it cannot be Available in the catalog, and its freshness policy is no-cache

#### Scenario: Division claim needs proof
- **GIVEN** an entry that lists the division dimension
- **WHEN** the catalog guard test runs
- **THEN** it fails unless the Metric is listed with an integration test proving the filter

### Requirement: Provider scope applied by the catalog, never by the endpoint
Every Metric route SHALL carry the Provider in the route, SHALL check it against the user's assignments on every request, and SHALL scope every warehouse query by the Provider that check resolved. The Provider filter SHALL come from the Metric Catalog and query layer, never from an individual endpoint, a body, a header, the session or a display name. A Provider the user cannot read and one that does not exist SHALL both return 404.

#### Scenario: Cross-Provider request
- **GIVEN** a user assigned to Provider A
- **WHEN** they request any Metric of Provider B
- **THEN** the response is 404 and no warehouse data is read

#### Scenario: Provider smuggled in a header or body
- **GIVEN** a request whose route Provider is A
- **WHEN** it also carries a Provider id in a header or query value for Provider B
- **THEN** only Provider A is used

#### Scenario: Anonymous caller
- **GIVEN** a caller with no session
- **WHEN** they request a Metric
- **THEN** the response is 401 or 403

### Requirement: Facility-restricted users get no Provider-level figures
A user restricted to some Facilities SHALL receive 404 for a Metric route unless their Facilities cover every Reporting Division of the Provider. A broad role SHALL NOT widen what a narrower assignment allows, and a Facility-restricted user SHALL NOT reach a sibling Reporting Division.

#### Scenario: Partial Facility coverage
- **GIVEN** a user limited to one Facility of a Provider that has several Reporting Divisions
- **WHEN** they request a Provider-level Metric
- **THEN** the response is 404 and nothing is read

#### Scenario: Sibling division
- **GIVEN** a Facility-restricted user
- **WHEN** they pass the key of a sibling Reporting Division
- **THEN** the response is 404 and nothing is read

#### Scenario: Full coverage is not refused by scope
- **GIVEN** a user whose Facilities cover every Reporting Division of the Provider
- **WHEN** they request the Provider's Metric
- **THEN** the scope check passes

### Requirement: Service Line filter
Metrics SHALL accept `serviceLine` of all (default), air or ground, where air is Rotor Wing and Fixed Wing and everything else is ground. A Metric that applies to air only SHALL treat air and all as air, and SHALL refuse ground with a 400 ProblemDetails that does not echo the value. The response SHALL report the Service Line in effect. A Metric not split by Service Line SHALL say so.

#### Scenario: Air-only Metric under all
- **GIVEN** an air-only Metric
- **WHEN** the caller sends `serviceLine=all`
- **THEN** the figures are air and the response Service Line is air

#### Scenario: Air-only Metric under ground
- **GIVEN** an air-only Metric
- **WHEN** the caller sends `serviceLine=ground`
- **THEN** the response is 400 and the text does not repeat the submitted value

#### Scenario: Unknown value
- **WHEN** the caller sends an unrecognised `serviceLine`
- **THEN** the response is 400

### Requirement: Reporting Division filter
A Metric SHALL honor `division` only if the catalog says it supports it. The value SHALL be a division key from the route Provider's directory, matched by key and never by display name. An unknown or foreign key SHALL be a 400 that does not echo the value. A Metric that does not support Division SHALL ignore the filter, and the response SHALL say the filter was not applied. A Label SHALL never change the `division` key. The Provider's division list for a Service Line SHALL include only divisions with a Transport under it in the latest load.

#### Scenario: Supported division narrows the figure
- **GIVEN** a Metric that supports Division and a valid key
- **WHEN** the Metric is requested with that key
- **THEN** the figures cover only that Reporting Division and the response names it

#### Scenario: Unknown or foreign key
- **WHEN** the caller sends a key that is not in the route Provider's directory
- **THEN** the response is 400 and the key is not echoed

#### Scenario: Display name is not a key
- **WHEN** the caller sends a division display name instead of its key
- **THEN** it is not matched

#### Scenario: Unsupported Metric ignores the filter
- **GIVEN** a Metric whose source has no Reporting Division
- **WHEN** a division is selected
- **THEN** the figures are unchanged and the response says no division was applied

### Requirement: Context Badge and the not-applied statement
Every Metric figure SHALL be shown with a Context Badge, as visible text, naming the Service Line and Reporting Division actually applied. The Division SHALL count as applied only when one is selected and the Metric supports it. When a chosen filter was not applied, the badge SHALL say so by naming all Reporting Divisions. An air-only Metric SHALL read "Air only" whatever the Service Line control says. An empty answer SHALL name the narrowing scope that was applied, and a Metric that did not honor the division SHALL NOT name it.

#### Scenario: Division applied
- **GIVEN** Air selected with a Division on a Metric that supports it
- **THEN** the badge shows the Service Line and the Division name as text

#### Scenario: Division not applied
- **GIVEN** a Division selected on a Metric that does not support it
- **THEN** the badge shows all Reporting Divisions, so the user sees the filter had no effect

#### Scenario: Name is untrusted text
- **GIVEN** a division, group or payer name containing markup
- **WHEN** it is shown in a badge, table or chart tooltip
- **THEN** it is rendered as literal text

### Requirement: Data Through stamp
Every Metric response SHALL carry `dataThrough`, the load date of the Provider's data (a date, never a time of day), from one shared source that names the warehouse tables each Metric reads. The SPA SHALL show one stamp with every Metric and SHALL NOT promise a last-updated time or offer a refresh button. When a newer Data Through arrives while a page is open, the SPA SHALL offer an Update view notice and SHALL NOT swap figures by itself.

#### Scenario: Stamp on every response
- **WHEN** any Available Metric is requested
- **THEN** the response includes `dataThrough` as a date or null

#### Scenario: New load while open
- **GIVEN** a page showing figures through an earlier date
- **WHEN** the tab regains focus and the Data Through has moved
- **THEN** a polite notice offers Update view and the figures stay unchanged until the user acts

### Requirement: Caching keyed on Data Through
DailyLoad Metric results SHALL be cached with a key made of the authorized Provider, the Metric key, the normalized parameters and the Data Through date, so a new load never reuses old results and one Provider never reads another's entry. The cache SHALL have a size cap and a TTL ceiling. A per-Provider gate SHALL limit concurrent queries below a global ceiling. No-cache Metrics SHALL never be cached. Successful responses SHALL be private and revalidated by a strong validator, a 304 SHALL be returned only after the Provider check passed, and an `If-None-Match` over its length and tag-count caps SHALL be ignored.

#### Scenario: New load invalidates
- **GIVEN** a cached result for Data Through D
- **WHEN** the Data Through becomes D+1
- **THEN** the Metric is recomputed

#### Scenario: Unauthorized caller gets no 304
- **GIVEN** a caller who may not read the Provider
- **WHEN** they send a matching validator
- **THEN** the response is 401, 403 or 404 with no validator

#### Scenario: One tenant cannot starve another
- **GIVEN** one Provider saturating its query gate
- **WHEN** another Provider requests a Metric
- **THEN** the second Provider is still served

#### Scenario: Cache is bounded
- **WHEN** more entries are added than the cap allows
- **THEN** older entries are evicted and memory stays bounded

### Requirement: Drills issued by the server
A Drill SHALL be declared in the catalog as a target Metric and the target dimension the clicked value fills. The server SHALL build each link from the catalog and the parameters in effect, name the route Provider, include only filters in effect, and issue one level of links per response. A link SHALL be omitted when the value is not drillable (Facility, Patient responsibility, Not recorded), when the target does not serve the Service Line in effect, when the target is not Available, or when the caller lacks the target's permission. The SPA SHALL map a link to its own route through an allow-list and SHALL NOT navigate to an API address. A Drill SHALL open a new page and never change the current view. Drills SHALL NOT form a cycle or deep chain.

#### Scenario: Drillable value
- **GIVEN** a payer mix share for a drillable payer group
- **THEN** the share carries a drill link to the payer breakdown naming the route Provider and the filters in effect

#### Scenario: Non-drillable value
- **GIVEN** a Patient responsibility, Facility or Not recorded share
- **THEN** no link is issued

#### Scenario: Unknown payer key
- **WHEN** the payer breakdown is requested with a payer key outside the five known groups
- **THEN** the response is 404 and echoes nothing

#### Scenario: Hostile link in the SPA
- **GIVEN** a link whose address is not on the allow-list or exceeds the length cap
- **THEN** the SPA ignores it

### Requirement: Help text keyed by helpKey
The catalog SHALL own only the help key and the one-line description. The SPA SHALL own the help sentences, keyed by help key: a short summary and a glossary of terms. Terms a Provider can rename SHALL be placeholders resolved from the Provider's Label, never literals. Help SHALL use CONTEXT.md terms and one spelling of payer.

#### Scenario: Every key has help
- **GIVEN** every catalog key and Drill target
- **WHEN** the SPA tests run
- **THEN** each has help text, with a short summary, non-empty unique glossary terms and resolved placeholders

#### Scenario: Label renames the term
- **GIVEN** a Provider whose Label calls a Reporting Division a base
- **THEN** help text says base, and the API key is unchanged

### Requirement: Aggregate-only responses
Available Metrics SHALL return aggregates only: counts, sums, shares and rankings. They SHALL NOT return patient-level or claim-level data. Address, city, state and zip of a pickup SHALL never be selected. Payer, group and vehicle names SHALL be cleaned and length-capped before they are returned.

#### Scenario: Pickup Metric has no address
- **WHEN** the pickup location Metric is requested
- **THEN** no address, city, state or zip is in the response

#### Scenario: Claim-level data is not a Metric
- **GIVEN** a request for claim-level detail through the Metric routes
- **THEN** no Metric provides it, and claim reads need their own permission and audit record

### Requirement: Query and result bounds
Every Metric query SHALL run through the read-only warehouse with a command timeout (30 seconds by default) and a result-size cap (10,000 rows by default). Date ranges and group-by cardinality SHALL be bounded. A query over the cap SHALL fail rather than return a partial set, and a timeout or unavailable warehouse SHALL answer 503 without logging query text or values. Row lists SHALL be capped with an Other row so rows still add up, and a contribution Metric's rows SHALL add up exactly to its total.

#### Scenario: Months out of range
- **WHEN** a caller asks for fewer than one or more than the allowed months
- **THEN** the response is 400

#### Scenario: Result too large
- **GIVEN** a query returning more rows than the cap
- **THEN** it is refused, not truncated

#### Scenario: Warehouse slow or down
- **WHEN** a query exceeds the timeout or the warehouse is unreachable
- **THEN** the response is 503 and the log holds a code and duration, not SQL or values

#### Scenario: Contribution reconciles
- **GIVEN** a contribution Metric with more movers than its cap
- **THEN** one Other row holds the rest and the rows sum to the reported total change

### Requirement: Cash Metrics family
The Cash category SHALL provide `net_collections`, `net_cash_projection`, `net_cash_breakdown`, `net_cash_by_group`, `cash_per_transport_by_payer`, `cash_contribution_by_aircraft`, `cash_contribution_by_division` and `cash_contribution_by_payer`. Net cash SHALL be counted by the month the payment posted. Cash per transport SHALL use matured cohorts (12 service months ending at least 6 months before the chosen month). Contribution Metrics SHALL rank by absolute change and give the share of the change. The projection Metric SHALL look ahead at most three months and SHALL NOT split by Reporting Division. The aircraft contribution Metric SHALL apply to air only. The net cash breakdown SHALL carry a drill to payer mix, and the payer contribution Metric a drill to the payer breakdown.

#### Scenario: Cash figure with prior month
- **WHEN** `net_collections` is requested with a period and months
- **THEN** it returns the monthly series with the change against the prior month and Data Through

#### Scenario: Projection ignores division
- **WHEN** a Division is selected for `net_cash_projection`
- **THEN** the figures are unchanged and the Division is reported as not applied

#### Scenario: Cross-Provider cash
- **GIVEN** a user of Provider A
- **WHEN** they request any Cash Metric of Provider B
- **THEN** the response is 404

### Requirement: Payer Metrics family
The Payers category SHALL provide `payer_mix`, `payer_mix_detail`, `payment_age` and `payer_mix_financial_effect`. Payer mix SHALL count Transports by payer group on the month of service, current and prior, with the point change. The payer breakdown SHALL list the top 10 payers in one group plus Other and SHALL NOT be placeable. Payment age SHALL use whole months. The financial effect SHALL be labelled an expected effect, not posted cash, and SHALL agree with the same calculation in the Insight.

#### Scenario: Payer mix with drill
- **WHEN** `payer_mix` is requested for an aircraft type
- **THEN** shares and counts by payer group are returned with drill links on drillable groups

#### Scenario: Breakdown reconciles
- **GIVEN** a payer breakdown for one group
- **THEN** its rows, including Other, sum to the payer mix count for that group

#### Scenario: Cross-Provider payer breakdown
- **GIVEN** a user of Provider A
- **WHEN** they request a payer breakdown for Provider B
- **THEN** the response is 404

### Requirement: Transports Metrics family
The Transports category SHALL provide `transport_volume`, `transport_mix_by_call_type` and `transports_by_pickup_location`. Transport volume SHALL count by date of service, with loaded miles in statute miles and a Statute or Nautical choice for air, and SHALL allow up to 24 months. Call type SHALL come from the response type (scene and interfacility for air, emergency and non-emergency for ground). Pickup location SHALL bucket by origin modifier, list sending facilities (top 20 plus Other) for facility-based sources, and carry no address.

#### Scenario: Volume with groups
- **WHEN** `transport_volume` is requested
- **THEN** the series and the last month's groups with prior-month change are returned

#### Scenario: Months above 24
- **WHEN** `months` is above 24
- **THEN** the response is 400

#### Scenario: Cross-Provider transports
- **GIVEN** a user of Provider A
- **WHEN** they request Provider B's transports
- **THEN** the response is 404

### Requirement: Fleet and bases Metrics family
The Fleet and bases category SHALL provide `aircraft_performance`, `loaded_miles_by_aircraft` and `base_analysis`. The two aircraft Metrics SHALL apply to air only, list at most 100 tail numbers plus Other, and show a blank tail as Not recorded. Base analysis SHALL list at most 100 Reporting Divisions plus Other, with tail numbers per division (top 10 plus Other). Net cash per transport SHALL be stated as posting-month cash over Transports, not an expected payment.

#### Scenario: Aircraft Metric under ground
- **WHEN** an aircraft Metric is requested with `serviceLine=ground`
- **THEN** the response is 400

#### Scenario: Tails capped
- **GIVEN** more than 100 tail numbers
- **THEN** the rest are summed in one Other row

#### Scenario: Facility-restricted base analysis
- **GIVEN** a Facility-restricted user without full Provider coverage
- **WHEN** they request base analysis
- **THEN** the response is 404

### Requirement: Claims and AR Metrics family
The Claims and AR category SHALL provide `clean_claim_rate`: claims accepted first time, rejected and total, with the rate per month of service. It SHALL NOT be split by Service Line or Reporting Division and SHALL say so. It SHALL carry only aggregates.

#### Scenario: Rate without filters
- **WHEN** `clean_claim_rate` is requested with a Service Line and a Division
- **THEN** the figures are unchanged and both filters are reported as not applied

#### Scenario: Cross-Provider claims rate
- **GIVEN** a user of Provider A
- **WHEN** they request Provider B's rate
- **THEN** the response is 404

### Requirement: Insights Metric family
The Insights category SHALL provide `what_changed`: the written summary of Findings for one view and period. Drivers SHALL add up with the remainder to the change in posted cash, and a Finding on another basis SHALL be shown beside the Drivers and not added. At most five Findings SHALL appear, each at 10% of the change or more. It SHALL NOT be placeable, SHALL carry no patient or claim detail, and SHALL pass the chosen Reporting Division to every Metric it reads.

#### Scenario: Drivers reconcile
- **WHEN** the Insight is requested
- **THEN** Driver dollar impacts plus the remainder equal the change in posted cash

#### Scenario: Payer mix is not a Driver
- **GIVEN** a payer mix Finding on the expected-effect basis
- **THEN** it is shown beside the Drivers and excluded from their sum

#### Scenario: Cross-Provider Insight
- **GIVEN** a user of Provider A
- **WHEN** they request Provider B's Insight
- **THEN** the response is 404

## Open questions
1. Planned Metrics have no API: `net_cash_by_aircraft_type`, `expected_cash_by_payer`, `payment_timing_by_payer`, `ar_days`, `open_negotiations`, `claim_status_distribution`, `dollars_at_risk`, `denied_claims`, `claims_needing_attention`, `timely_filing_risk` and `expected_payment_per_transport`. Several have no warehouse source or definition yet.
2. The pickup source mapping (origin modifier letters) is general CMS knowledge not yet confirmed against warehouse values, and the destination modifier column is unconfirmed. Should call type be reconciled with origin modifier?
3. The projection range rule (lower and higher of the total and the two component sums) predates knowing the components are additive. Should it change, and how does the total relate to the components?
4. What is the production definition of a clean claim, where does the 95% target live, and can the rate be split by Service Line or Reporting Division?
5. Who owns each Metric definition, and what are the targets and thresholds? Insight thresholds (10%, five Findings) are tunable constants not yet tuned on real months.
6. Is there a source for expected payment per transport by tail, base, call type or pickup source?
7. Custom date ranges are not supported: every Metric compares one month with the prior month.
8. A Metric over the result-size cap fails with an error. Should it degrade to an Other row instead?
