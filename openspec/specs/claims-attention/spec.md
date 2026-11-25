## Purpose
Prism lets a Provider's users review the claims that need customer action, organized as Attention Checks (the tabs on the review page). Counts per check are aggregates; the claims behind them are claim-level data (PHI) that need the claims permission and leave an audit record. This spec characterizes what is implemented today, derived from ADR 0005, CONTEXT.md, AGENTS.md sections 1, 2, 5 and 6, the claim attention routes, the review page and the integration, unit and web tests. Authentication, Provider scope resolution and warehouse protection are specified in `auth-and-sessions` and `warehouse-access`.

## Requirements

### Requirement: Attention Check catalog
The system SHALL offer a fixed catalog of Attention Checks, each with a key, a name, a description, an order and a row rule: Denied (a denial code is present and money is still owed), Needs attention (denied, on hold, or in collections) and All (every claim in the period). The review page tabs SHALL come from this catalog, ordered Denied, Needs attention, All. A check key that is not in the catalog SHALL NOT be accepted, and request text SHALL NOT alter a row rule.

#### Scenario: Tabs follow the catalog
- **GIVEN** a user with the claims permission opens the review page for a period
- **WHEN** the counts load
- **THEN** one tab per Attention Check is shown, in catalog order, each with its flagged-claim count

#### Scenario: Unknown check is not found
- **GIVEN** a user with the claims permission
- **WHEN** they request the claims or the export for a check key not in the catalog
- **THEN** the system returns 404 and reads no claims and writes no audit record

#### Scenario: Check key cannot change the rule
- **GIVEN** a claims request whose query string also carries a check key, Provider or audit values
- **WHEN** the system handles it
- **THEN** the route's check and Provider are used and the extra values are ignored

### Requirement: Claims permission gates counts, lists and exports
The system SHALL require the claims permission, distinct from the PHI permission, on the counts, the claim list and the CSV export. A user without it SHALL be refused with 403 on all three, SHALL NOT learn how many claims are flagged, and no claim SHALL be read or audited. Anonymous callers SHALL be refused. The review page SHALL ask for claims and counts only when the user holds the permission, and otherwise SHALL show that the user has no access to claim details.

#### Scenario: Permitted user gets rows
- **GIVEN** a signed-in user with the claims permission for the Provider
- **WHEN** they list the Denied claims for a period
- **THEN** the claims are returned with the total, page, page size and Data Through
- **AND** exactly one audit record is written

#### Scenario: User without the permission is refused
- **GIVEN** a signed-in user who lacks the claims permission
- **WHEN** they request counts, the list or the export
- **THEN** each returns 403, the body holds no claim value, no claim is read and no audit record is written

#### Scenario: Anonymous refused
- **GIVEN** a caller with no session
- **WHEN** they request any claim attention route
- **THEN** they are refused and nothing is read

#### Scenario: Page hides claims without the permission
- **GIVEN** a signed-in user without the claims permission
- **WHEN** they open the review page
- **THEN** no claims or counts are requested and the page says to ask an administrator for the claims permission

### Requirement: Counts are aggregates
The counts route SHALL return, for a required period and a Service Line (all, air or ground, default all), the number of claims each Attention Check flags and the balance still owed on denied claims, by date of service in the Provider's latest warehouse load. It SHALL also return the denied rate (denied claims over all claims, 0 to 1, absent when the period has no claims), the top 10 denial reasons plus one Other row for the rest, separate counts for Denied, On hold and In collections (which overlap and are not added), and four filing bands (past due, due within 10 days, due in 11 to 30 days, due later) for needs-attention claims. Counts SHALL carry no claim, patient or encounter value and no individual claim balance. Denial reason text SHALL be treated as untrusted warehouse text: cleaned of control characters and whitespace runs, cut to 120 characters, and shown as "No reason given" when empty. A real reason called "Other" SHALL be shown as "Other reason". Filing bands SHALL be described as estimates while filing limits are a placeholder.

#### Scenario: Counts carry no claim detail
- **GIVEN** a user with the claims permission
- **WHEN** they request counts
- **THEN** the response holds counts, balances, rates, reasons, categories and bands only, with no claim reference, payer, vehicle or encounter id

#### Scenario: Extra aggregates present
- **GIVEN** a period with denied, on-hold and in-collections claims
- **WHEN** counts are requested
- **THEN** the response includes the denied rate, reasons with an Other row when more than 10 reasons exist, three overlapping categories and four filing bands in fixed order

#### Scenario: Empty period
- **GIVEN** a period with no claims
- **WHEN** counts are requested
- **THEN** the denied rate is absent and counts are zero

#### Scenario: Reason text cleaned
- **GIVEN** denied claims whose reason is blank, contains control characters, or is longer than 120 characters
- **WHEN** counts are requested
- **THEN** blank reasons show "No reason given" and other reasons are cleaned and cut to 120 characters

#### Scenario: Counts denied without the permission or across Providers
- **GIVEN** a user without the claims permission, an anonymous caller, or a user requesting another Provider
- **WHEN** they request counts
- **THEN** they are refused (403, 401 or 403, and 404 respectively) and no count is disclosed

### Requirement: Claim list content, order and Data Through
The claim list route SHALL return the claims flagged by one Attention Check for a required period, Service Line and optional group (an aircraft type or level of service key), ordered by soonest estimated filing deadline, with a stable tie-break. Each claim SHALL show a claim reference, service date, payer, payer category, vehicle, Reporting Division, status, denial code, denial reason, balance due, estimated filing due date and days left. The balance due SHALL be what is still owed, not an estimate of payment. The filing due date SHALL be an estimate (date of service plus a filing limit for the payer category) and days left SHALL count from Data Through. The response SHALL carry Data Through taken from the shared reader for the trip data. A claim row SHALL expose only the listed columns; patient names, birth dates, addresses and similar columns SHALL NOT be read.

#### Scenario: Soonest deadline first
- **GIVEN** denied claims with different filing deadlines
- **WHEN** the list is requested
- **THEN** the claim with the soonest estimated deadline comes first

#### Scenario: Group narrows the read
- **GIVEN** a valid group key such as fixed-wing
- **WHEN** counts, list or export are requested
- **THEN** the read is narrowed to that aircraft type and the audit record names the group

#### Scenario: Data Through stamped
- **GIVEN** the trip data is loaded through a date
- **WHEN** counts or the list are requested
- **THEN** the response carries that date as Data Through

### Requirement: Paging and result limits
The claim list SHALL default to 25 rows per page and SHALL accept a page size from 1 to 100 and a page number from 1 to 1000. The response SHALL report the total number of matching claims. A read matching more than 10,000 claims for one check, period and filter SHALL be refused with 422 and a calm message to narrow the period or Service Line, never truncated and never a 500, and SHALL NOT write an audit record. The export SHALL request every matching claim under the same cap.

#### Scenario: Page asked for
- **GIVEN** more claims than one page
- **WHEN** the user asks for page 2 with a page size
- **THEN** that page and the total are returned

#### Scenario: Out-of-range paging refused
- **GIVEN** a page size of 0 or 101, or a page of 0 or 1001
- **WHEN** the list is requested
- **THEN** the system returns a 400 validation problem and reads nothing

#### Scenario: Too many claims
- **GIVEN** more than 10,000 claims match
- **WHEN** the list or export is requested
- **THEN** the system returns 422 saying to narrow the period or Service Line and writes no audit record

### Requirement: Every claim read is audited
Every claim list read and every export SHALL write an audit record, before the response leaves, holding who read, which Provider, which check, the period, the number of rows and the group if any, and never claim data. The audit action SHALL distinguish a list from an export. A failed audit write SHALL fail the request and SHALL return no claims. A session without a portal user identity SHALL be refused (404) before any claim is read. Counts SHALL NOT be audited.

#### Scenario: List audited
- **GIVEN** a permitted user lists denied claims
- **WHEN** the response is sent
- **THEN** one list audit record holds the user, Provider, check, period and row count

#### Scenario: Export audited
- **GIVEN** a permitted user exports a check
- **WHEN** the download is sent
- **THEN** one export audit record holds the number of claims exported

#### Scenario: Audit failure fails the read
- **GIVEN** the audit store is unavailable
- **WHEN** a permitted user lists claims
- **THEN** the request fails with a server error and the body holds no claim

#### Scenario: Session without an identity
- **GIVEN** a session that carries no portal user identity
- **WHEN** it requests the list or export
- **THEN** the system returns 404 and reads no claim

### Requirement: PHI masking and marking
Every field of a claim row SHALL be treated as PHI because it can identify a patient's claim, and SHALL be marked as PHI in the response contract. Without the claims permission the system SHALL omit the rows entirely (refuse the request) rather than return masked rows, so no PHI value appears anywhere in the response. The counts response SHALL NOT be PHI-bearing. The list and export SHALL follow the same rule.

#### Scenario: No PHI substring without the permission
- **GIVEN** a user without the claims permission
- **WHEN** they call the list or export
- **THEN** the JSON or error body contains no claim reference, payer or other claim value

#### Scenario: PHI matrix coverage
- **GIVEN** the claim list response type is PHI-bearing
- **WHEN** the PHI matrix test runs
- **THEN** every route that returns it is in the matrix with the claims permission, and the counts response is not PHI-bearing

### Requirement: CSV export
The export SHALL return the same claims, in the same order, as the list, as a UTF-8 CSV download through the one shared CSV writer, with a header row and every cell quoted. A text cell starting with `=`, `+`, `-`, `@`, tab or carriage return SHALL be neutralized with a leading apostrophe inside the quotes, and embedded double quotes SHALL be doubled, including in cells that do both. The balance due SHALL be a quoted number cell that is not altered. An overdue claim SHALL read "Overdue by N" in the days-left column, not a negative number. The file name SHALL be built only from the check key and the date, never from claim or patient data. The export SHALL require the claims permission and an audit record.

#### Scenario: Formula neutralized
- **GIVEN** a claim whose reference starts with `=`
- **WHEN** it is exported
- **THEN** the cell is quoted and begins with an apostrophe inside the quotes

#### Scenario: Embedded quotes doubled
- **GIVEN** a denial reason containing double quotes
- **WHEN** it is exported
- **THEN** each embedded quote is doubled inside the quoted cell

#### Scenario: File name has no claim data
- **GIVEN** a claim whose reference holds a marker value
- **WHEN** the file is downloaded
- **THEN** the name is the product prefix, the check and the date only

#### Scenario: Overdue wording
- **GIVEN** a claim past its estimated filing deadline
- **WHEN** it is exported
- **THEN** the days-left cell reads "Overdue by" followed by the number of days

#### Scenario: Export refused without the permission
- **GIVEN** a user without the claims permission
- **WHEN** they request the export
- **THEN** they are refused and no file is produced

### Requirement: Provider and Facility scope
The system SHALL scope every claim read by the Provider in the route, after checking it against the user's assignments, and SHALL take no Provider from a query string, body or header. A Provider the user may not read SHALL return 404 on all three routes, without reading or auditing. A Facility-restricted user SHALL be refused (404) on all three routes, because claims cannot yet be filtered by Reporting Division.

#### Scenario: Cross-Provider read
- **GIVEN** user A holds the claims permission for Provider A
- **WHEN** they request Provider B's claims, counts or export
- **THEN** the system returns 404, reads nothing and writes no audit record

#### Scenario: Facility-restricted user
- **GIVEN** a user restricted to some Facilities of a Provider
- **WHEN** they request counts, the list or the export for that Provider
- **THEN** each returns 404 and no claim is read, including for a sibling Facility

#### Scenario: Request cannot set the Provider
- **GIVEN** a request with a different Provider id in the query string
- **WHEN** it is handled
- **THEN** the route's Provider is the only one read and audited

### Requirement: Input validation and resource bounds
The period SHALL be a month written yyyyMM, the Service Line SHALL be all, air or ground, and the group SHALL be a plain lowercase key of at most 40 characters checked against a bounded pattern; invalid values SHALL return 400 before any read or audit. A very long period SHALL be refused without a read. Claim reads SHALL be rate limited per signed-in user, independent of the address, returning 429 with a retry hint when exceeded, and one user's budget SHALL NOT affect another user on the same address. Warehouse reads SHALL be bounded by the result cap and a warehouse timeout, and a warehouse failure SHALL return 503 with no claim data.

#### Scenario: Bad group key refused
- **GIVEN** a group such as `fixed-wing` followed by a newline, `Fixed-Wing`, a key with a space, or one longer than 40 characters
- **WHEN** counts, list or export are requested
- **THEN** each returns 400, no read is made and no audit record is written

#### Scenario: Huge period
- **GIVEN** a period of 5,000 characters
- **WHEN** the list is requested
- **THEN** the system returns 400 without a read

#### Scenario: Per-user budget
- **GIVEN** a user who has used their per-minute budget of claim reads
- **WHEN** they read again
- **THEN** the system returns 429 with a retry hint
- **AND** another user on the same address is still served

#### Scenario: Warehouse unavailable
- **GIVEN** the warehouse cannot be reached
- **WHEN** a permitted user requests the list
- **THEN** the system returns 503 and the body holds no claim data

### Requirement: No caching of claim data
Counts, lists and exports SHALL be returned with no-store caching and no validator, SHALL NOT be stored in the shared Metric cache, and SHALL NOT be saved by the browser.

#### Scenario: No-store headers
- **GIVEN** a permitted user
- **WHEN** they call counts, list or export
- **THEN** each response carries no-store and no ETag

### Requirement: No PHI or raw input in logs and errors
The system SHALL NOT log request bodies, query strings, denial reason text or claim values from these routes, and error responses SHALL NOT echo submitted values. Logs may hold codes, paths and lengths.

#### Scenario: Marker in the query
- **GIVEN** a request whose period, Service Line and page hold a PHI marker
- **WHEN** it is refused with 400
- **THEN** neither the response nor any captured log entry holds the marker

#### Scenario: Warehouse text in a failure path
- **GIVEN** warehouse denial reasons hold a known marker and a later read fails
- **WHEN** the failure is logged and returned
- **THEN** neither the response nor the logs hold the reason text

### Requirement: Findings stay aggregate and Send to CSE does nothing
The claim count and link SHALL NOT be part of the shared, cached Insights response; the web app SHALL ask the counts route only when the user holds the claims permission. The Send to CSE action SHALL list the ticked claims in a drawer and SHALL NOT send anything. Ticked claims SHALL be cleared when the period, Service Line or aircraft type changes.

#### Scenario: Insights carry no claim count
- **GIVEN** a user without the claims permission views Insights
- **WHEN** the response loads
- **THEN** it holds no claim count and no card links to the review page

#### Scenario: Send to CSE sends nothing
- **GIVEN** a user ticks claims and chooses Send to CSE
- **WHEN** the drawer opens
- **THEN** it lists the selected claims and no message is delivered

## Open questions
- Filtering claims by Reporting Division, and a path for Facility-restricted users, need a division mapping the warehouse table does not carry. Until then those users are refused.
- Filing limits per payer category are a placeholder; the filing due date and the filing bands are estimates, and balance due stands in for the expected payment.
- Send to CSE needs a recipient directory, a delivery channel and a PHI review of what a message may contain.
- Counts are not audited because they are aggregates, yet a flagged count is behind the claims permission; confirm this is the intended audit boundary.
- The audit table is not partitioned; the DBA team must decide how before it grows.
- There is no assigned owner, IDR, patient-due or missing-documentation category because the warehouse has no source.
- The export is capped at 10,000 claims, the same as the list; confirm whether a larger asynchronous export is wanted.
