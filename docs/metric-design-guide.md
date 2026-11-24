# Metric and component design guide

This guide is binding for anyone adding or changing a Metric, a metric component, assistive text, a drill or a context filter (see `AGENTS.md`). It says how a Metric is built and how it is shown, so a new one behaves like the ones that exist. Back to the [README](../README.md). The reasons behind it are in [ADR 0007](adr/0007-metric-catalog-v2.md). Where a rule is enforced by a test, the test is named in [section 11](#11-what-the-tests-enforce).

## 1. Principles

1. **The Metric Catalog is the single source.** A Metric is defined once, in `MetricCatalog`. Dashboard cards, Saved Views, the public API and its OpenAPI document all read from it. Nothing about a Metric is declared only in a page.
2. **A Metric is a slice, not a scatter.** Everything about one Metric lives in a same-named folder in each project (and one folder on the web). Adding a Metric must not mean editing a central list.
3. **A figure carries its own trust cues.** Every Metric shows the scope it used (Context Badge), how fresh it is (Data Through stamp) and what it means (help text), in the same place every time.
4. **Aggregate-only by default.** A Metric returns counts and sums. Patient-level or claim-level data needs its own permission and an audit record and is never cached.
5. **Drift fails the build.** The catalog, the backend modules, the web renderers, the help text and the generated docs are cross-checked by tests, so a half-added Metric cannot merge.
6. **The server decides what is reachable.** Drill links, Division scope and permissions are decided on the server. The browser follows links it is given and builds nothing from untrusted input.

## 2. Anatomy of a Metric

A Metric has four parts that must agree: a **catalog entry**, a **backend slice**, a **web slice** and **help text**.

### Catalog entry (`src/Prism.Application/Metrics/MetricCatalog.cs`)

| Field | Meaning |
|---|---|
| `Key` | Lowercase snake_case id (`payer_mix`). It is stable: it is stored in Saved Views and appears in API responses. |
| `Name`, `Description`, `Unit` | Plain-language name, one-line description (also the OpenAPI text), and unit (`USD`, `transports`, `percent`). |
| `Category` | Group in the palette (cash, payer, transports, claims, insights). |
| `Status` | `Available` (has an API behind it) or `Planned` (listed as coming soon, cannot be placed). |
| `Placeable` | May be added to a Saved View. A drill-only target (`payer_mix_detail`) and `what_changed` are not placeable. |
| `ClaimLevel` | The Metric needs the claims permission and an audit record. A claim-level Metric can never be `Available` in the catalog. |
| `ServiceLines` | Which `serviceLine` values it accepts: `air`, `ground`, `both` (`both` is the `all` word on the wire). Follows from `AppliesTo`. |
| `Context.AppliesTo` | `air`, `ground` or `both`: whether the Metric is about air only, ground only or both. The rule: `both` lists `air`, `ground`, `both` in `ServiceLines`, `air` lists `air` and `both`, `ground` lists `ground` and `both`. A test enforces it. |
| `Dimensions` | What it can be broken down by or filtered on: `period`, `months`, `serviceLine`, `group`, `division`, `payerGroup`. |
| `Presentations` | The ways it can be drawn (`line`, `column`, `stacked`, `donut`, `tile`, `table`), with sizes and exactly one default. Every Metric can be read as a table. |
| `Options` | Per-widget options (for example a 6 or 12 month choice). |
| `Context` | `ProviderScoped`, `ServiceLines`, `DivisionSupport` (`None` or `Supported`), `GroupDimensions`, `TimeGrain`. |
| `Freshness` | `DailyLoad` (cached against Data Through) or `NoCache` (claim-level). No other policy exists. |
| `HelpKey` | Key of the help text. It equals `Key` for every Available Metric. |
| `Drills` | Where the Metric can lead next (see [section 6](#6-drill-down)). |

### Backend slice

For a Metric `Foo`, add a `Foo` folder in each project:

- `Prism.Application/Metrics/Foo/Foo.cs`: the `IFooMetric` interface, its result records and its static `Definition` (`MetricDefinition(Key, Name, Description, Unit)`).
- `Prism.Infrastructure/Metrics/Foo/FooMetric.cs`: the SQL and query class. The Provider filter, the query timeout (30 seconds by default) and the result-size cap come from the warehouse layer. Do not add your own Provider logic. Only counts leave the query.
- `Prism.Api/Metrics/Foo/FooEndpoints.cs` and `FooModule.cs`: the route, the response DTOs and an `IMetricModule` that registers the service and maps the route. Give the module a unique `RouteOrder` (path order decides the OpenAPI document order). Every route needs a summary, a description, response types including `ProblemDetails`, and examples.

`IMetricModule`s are discovered by assembly scan, so nothing else in the host changes.

### Web slice (`web/src/app/metrics/<kebab-key>/`)

- `<key>.dto.ts`: response types, matching the OpenAPI document.
- `<key>.loader.ts`: the HTTP call, a function taking `HttpClient` and a `MetricQuery`.
- `<key>.widget.ts`: maps the response to `WidgetData` for Saved View widgets.
- `<key>.renderer.ts`: the entry for `METRIC_RENDERERS` (`loader`, `toWidgetData`, `presentations` as the catalog lists them). A drill-only target goes in `DRILL_TARGETS` instead.
- `help.en.ts`: the help text (see [section 5](#5-assistive-text)).
- Components, if the Metric has its own page.

### Contribution Metrics

A Metric that explains a change (`cash_contribution_by_*`) must reconcile: its rows, including one `Other` row for everything past the cap, add up exactly to the total it reports, and a test sums them. `total` is computed from the rows, never read separately, so the two cannot drift. Rank rows by absolute change, sort in code (not by database collation), and give a row the `shareOfChange` of the total change (null when it is zero). Where a Metric is about air only (tail numbers, aircraft) it declares `AppliesTo` `air`.

### Response envelope

Every Metric response includes `dataThrough` (`yyyy-MM-dd` or null) and, where it supports Division, `division` and `divisionName`. The envelope is what lets the stamp, the badge and the cache agree.

## 3. Context: Provider, Service Line, Reporting Division

Context is what a figure was computed for. It is decided on the server and shown with the figure.

### Provider

- The Provider is in the route: `/api/v1/providers/{providerId}/metrics/...`. It is checked against the user's assignments on every request (`ProviderAccess.ResolveAsync`). It is never stored in the session or taken from a body or header.
- A Provider the user cannot read and one that does not exist both return 404, so existence is not leaked.
- Every warehouse query is scoped by the Provider id the portal id resolves to. The Provider filter comes from the catalog and the query layer, never from an individual endpoint.
- A user restricted to some Facilities gets 404 unless their Facilities cover every Reporting Division of the Provider. A broad role never widens what a narrower assignment allows.

### Service Line

- A query parameter, `serviceLine=all|air|ground` (`both` in the C# enum). Air is `Rotor Wing` and `Fixed Wing`. Everything else is ground.
- A Metric lists the lines it serves in `ServiceLines`.
- **Air only and ground only.** A Metric whose subject exists on one line only (tail numbers and aircraft) declares `AppliesTo` `air` (or `ground`). For it `serviceLine=air` and `serviceLine=all` both mean air, because it only has air data, and `serviceLine=ground` is a 400 ProblemDetails that says the Metric covers air only and does not echo the value. The response `serviceLine` is the line in effect. The Context Badge reads "Air only" (or "Ground only") for such a Metric, whatever the Service Line control says. `AppliesTo` is in the catalog as `context.appliesTo`. A drill is not offered when the target does not serve the line in effect.
- The Service Line sets the default Labels (for example what a Reporting Division is called).

### Reporting Division

- A query parameter, `division`, taking a division **key** from the route Provider's directory. It is matched by key, never by display name, and an unknown or foreign key is a 400 that does not echo the value. A Facility-restricted user never reaches a sibling division.
- A Metric supports it only if its source table has `reporting_division` **and** an integration test proves the filter. Today that is the trip-table Metrics: `payer_mix`, `payer_mix_detail`, `transport_volume`, `payment_age`, `cash_per_transport_by_payer`, `net_cash_breakdown`, `net_cash_by_group`, the fleet, call type, payer effect and pickup Metrics, and the three `cash_contribution_by_*` Metrics. `net_collections` (with a division it is added up from the ledger joined to the trip table, not the KPI table, so its Data Through is the trip table's) and `what_changed` (the division goes to every Metric it reads; base mix is skipped) support it too. The two whose tables have no division column (`net_cash_projection`, `clean_claim_rate`) have `DivisionSupport = None`; see `docs/warehouse.md` for why they are not split.
- The catalog guard test fails if a Metric claims `division` without being listed with a test.
- A Label can rename the term (Air Providers call it a base) but never changes the `division` key.
- **One control.** Every page that shows a Reporting Division choice uses `app-division-select` (`shared/division-select.component.ts`), never its own `ems-select`. The list is the Provider's real divisions from `GET .../metrics/net-collections/divisions`, loaded once in `DashboardFilterContext` (keyed on Provider and the data-freshness revision), as `{ key, name }`: the `key` is what the `division` parameter takes, the `name` is warehouse text shown as text only. The dashboard and saved views use `mode="edit"`; pages that take the division from the address (`division`: payer mix, payer breakdown, net cash breakdown) use `mode="follow"` with `selectedKey`, which shows the chosen division off, with "Set on the dashboard".
- **States are never silent.** While the list loads, after a failure (with "Try again") and when the Provider has no divisions, the control is off with a visible reason. A chosen key that is not in the Provider's list is reset to all, and is never sent to a Metric.
- **Service Line.** The divisions route takes an optional `serviceLine` (`air`, `ground`, `all`) and lists only the divisions with a Transport under it in the latest load, so the picker never offers a choice that can only be empty. `DashboardFilterContext` loads the list for the current Service Line (Provider, Service Line and data-freshness revision). Keys are the same under every line. If a Service Line change leaves the chosen division out of the new list, the choice is **kept, not reset**: the picker shows it as "<name> (no air transports)", a note and a live region say its figures are empty and to choose All, and Metrics still receive its key (the API validates it against the full directory). Choosing All clears it.
- **Empty answers name their scope.** One function, `emptyMessage` (`shared/empty-scope.ts`), writes every empty-state sentence. When the Service Line (only if the Provider has more than one and the Metric is split by line) or a Reporting Division the Metric actually applied narrowed the answer, it says so: "No net cash collected for <division name> under Air only in September 2026. Try All bases or Air + ground." (Label for the term, name as text). With no narrowing filter the plain sentence stays. A Metric that does not honor the division never names it and never changes when it changes (it shows its "Not filtered by" note, also in the empty state). The "What these numbers mean" help stays under the sentence. New Metric views and panels must use it.

### The Context Badge

Every Metric shows the scope it actually applied, as visible text and not only a tooltip:

| Situation | Badge reads |
|---|---|
| Air, no Division selected | "Air · All bases" (the term comes from the Label) |
| All service lines | "All service lines · All reporting divisions" |
| Division selected and the Metric supports it | "Air · Mercy Flights Base 1": the division's name from the loaded list, as text. "Selected <Label>" only if the name is not in the list |
| Division selected and the Metric does not support it | "All <Label plural>", so the user can see the filter had no effect |

`divisionApplied` is true only when a Division is selected **and** the catalog says the Metric supports it. The badge is driven by the catalog and the applied query, so it cannot claim more than the query did. Saved Views never store a Division, a Label or any figure: they store Metric keys, presentations and grid positions only, so a view always shows what its viewer may see.

## 4. Freshness and caching

### One stamp

- The warehouse loads once a day. `snapshot_date` is the load date and has no time of day, so **Data Through** is that date. Prism shows one stamp, "Data through Nov 22 · Updates once a day", and never promises a "last updated" time.
- Every Metric response, including the "what changed" Insight and the claim Attention Checks, reports `dataThrough` from the single `IDataThroughReader` (`src/Prism.Application/Warehouse/IDataThroughReader.cs`). `MetricDataThrough` names which source table each Metric reads, and the reader takes the date for those tables. The cache key, the ETag and the stamp therefore agree.
- There is no refresh button. The stamp tells users the data changes daily, so they do not try to refresh.

### Server cache

- `MemoryMetricCache` caches aggregate Metric results. The key is the authorized Provider, the Metric key, the normalized parameters and the Data Through date, so a new load never reuses old results. The cache has a size cap and a TTL ceiling, so memory is bounded.
- A per-Provider gate limits how many queries one Provider can run at once, below a global ceiling, so one tenant cannot starve the others. Waiting honors cancellation.
- Each Metric's cache decorator is one line in `CachedMetrics.cs` using `MetricCacheCall.Cached(...)`. The cache key encodes each value with a length prefix, so values cannot collide.
- `NoCache` Metrics (claim-level) are never cached.

### HTTP caching

- Successful Metric responses are `Cache-Control: private, no-cache` with a strong `ETag` of the body (`MetricCacheHeaders.Revalidate`). The browser always revalidates and the server answers **304** with no body when nothing changed, so repeat views are cheap and never stale.
- The filter runs **after** Provider access. A 401, 403 or 404 is never turned into a 304 and carries no ETag, so a caller cannot learn anything from an ETag.
- The catalog endpoint is static between deploys and has `private, max-age=300` on 200 responses only.
- Claim-level routes stay `Cache-Control: no-store`, with no ETag.

### When new data arrives

The SPA does not poll. It revalidates when the tab regains focus and when the user navigates (throttled, one in-flight request). If the Data Through date moved, a quiet notice appears: "Data updated through Nov 23. Update view". It is announced politely, never takes focus, and **never swaps figures on its own**. The button refetches what is on screen and announces "Updated to Nov 23".

## 5. Assistive text

Every Metric explains itself in plain language, in the same place, with the same structure.

### Who owns what

- The **catalog** owns the `HelpKey` (equal to the Metric key) and the one-line English `Description` that feeds the OpenAPI document. It does not own sentences or glossary lists.
- The **SPA** owns the user-facing sentences, in `web/src/app/metrics/<kebab-key>/help.en.ts`, registered in `metrics/help-registry.ts`. This keeps the door open for more languages: a new language adds `help.<locale>.ts` files and no API change.
- Trade-off: the backend cannot render help text, so a PDF or email export has to bring its own copy or leave it out.

### Shape

```ts
export interface MetricHelp {
  readonly summary: string;                                   // what the card shows, how it is counted, what it leaves out
  readonly glossary: readonly { term: string; text: string }[]; // "What these numbers mean"
}
```

### Writing rules

- **Summary:** what the figure shows, how a Transport or dollar is counted (for example "counted in the month it happened, under its primary payer"), and what it leaves out. Aim for 65 words or fewer.
- **Glossary:** 3 to 7 terms the reader might not know, each non-empty and unique, in the style of the "What these numbers mean" panel.
- **Plain language.** Spell out accounts receivable (AR) on first use. Avoid ETL, KPI and DOS in user-facing text.
- **One spelling:** "payer".
- **Renamable terms are placeholders, never literals.** Write `{reportingDivision}` (lower case, mid-sentence) or `{ReportingDivision}` (the Label's own capitals). `resolveHelpText` fills them from the Provider's Label. Never hard-code "base" or "reporting division".
- **State the rules that surprise people:** posting month versus date of service, Air versus ground, excluded sources, "not a confidence interval" for a projection range, "patient transports only", and why a group has no drill.
- **Use CONTEXT.md terms:** Transport, not trip or run. Reporting Division, not base or station.

### Rendering

Use `<app-metric-help metric="payer_mix" [serviceLine]="line()">` (inputs: `metric`, `serviceLine`, `labels`). It shows the summary and a "What these numbers mean" disclosure button with `aria-expanded` and `aria-controls`, opening a glossary list. It is an inline disclosure so it also works inside the full-screen frame. Enter and Space toggle it, and Escape closes it with focus staying on the toggle. The glossary is not a Tab stop.

## 6. Drill-down

A Drill takes a reader from one element of a Metric (a bar, a donut slice, a legend entry, a card, a table row) to a more specific Metric view. The example: on rotor wing payer mix, clicking Commercial opens the commercial payer breakdown.

### Declaring a Drill

In the catalog, a Metric lists `Drills`. Each is `MetricDrill(rel, targetKey, bindsDimension)`:

- `rel` is `drill`.
- `targetKey` is the catalog key of the Metric it opens. It must be Available.
- `bindsDimension` is the target's dimension that the clicked value fills (a payer group fills `payerGroup`, a breakdown group fills `group`).
- There is no `from`: the source element is implied by the bound dimension.

Current drills: `payer_mix` to `payer_mix_detail` (binds `payerGroup`), `cash_contribution_by_payer` to `payer_mix_detail` (binds `payerGroup`), and `net_cash_breakdown` to `payer_mix` (binds `group`).

### REST drives the next step

The server issues the links. `DrillLinkBuilder` (`src/Prism.Api/Metrics/Shared/DrillLinkBuilder.cs`) builds each link from the catalog `Drills` and the parameters in effect, and each data point in a response carries `links: [{ rel, href, title }]`. The `href` names the **route Provider** and includes only the filters actually in effect (`group`, `period`, `serviceLine`, `division`), URL-encoded. A link is **omitted** when:

- the clicked value is not drillable (Patient responsibility, Facility and Not recorded have no payer detail);
- the target does not serve the Service Line in effect;
- the target lacks the bound dimension or is not Available;
- the caller lacks the target's permission.

Only one level of links is issued per response. A test fails on a drill cycle or a deep chain.

### What the SPA does with a link

The SPA never navigates to an API `href`. `web/src/app/metrics/drill-link.ts` parses the href against a strict allow-list (only known target paths, validated keys, a length cap), and maps it to the SPA's own route and query parameters. Anything unknown is ignored. To add a target, add its path to the allow-list and to `routeOf` in `drill-link.ts`, and register a view in `DRILL_TARGETS` (or `METRIC_RENDERERS`).

### Navigate, never swap in place

A Drill opens a new URL: `dashboard/metrics/:key`, with the scope in the query string, and the existing named pages (`dashboard/net-cash/:group`, `dashboard/metrics/payer-mix/:group/:payerKey`). This keeps Back working, makes the view linkable, and avoids swapping content inside a fixed-size card. The generic `app-metric-view` host dispatches by key, shows a calm "View not found" for an unknown key, and redirects keys that have their own page.

On arrival:

- a **breadcrumb** shows the path (Net cash > Rotor wing payer mix > Commercial);
- focus moves to the page heading and the page is announced;
- the freshness stamp, the Context Badge and the help panel are present, as on every Metric;
- scroll position is restored when the reader goes Back.

### Drill UX requirements

- A drillable element has a visible hover and focus treatment and an accessible name like "View Commercial breakdown".
- Mouse is never the only way. Drill links are real links in the legend, the table view and the Saved View widget, so keyboard and screen-reader users can follow them. The chart's click handler is only a pointer shortcut.
- Elements without a link are not styled as links, and the table or help text says why (for example, patient payer names are never shown).
- Drills from a Saved View widget navigate away. A Saved View stores no drill state.
- A drill target that returns payer or group names must treat them as untrusted text (see the escaping rule below).

### The payer detail example

`payer_mix_detail` (`GET .../metrics/net-collections/payer-mix/{payerKey}?group=&period=&serviceLine=&division=`) lists the payers within one payer group over the month and the month before: the top 10 by count plus an "Other" row, aggregate counts only. `payerKey` must be one of `commercial`, `medicare`, `medicaid`, `other-gov` or `va`. Anything else is a 404 that echoes nothing. Names are cleaned in SQL (control characters and runs of spaces collapsed, trimmed, cut to 120 characters) and rendered as text.

## 7. The shared components

All in `web/src/app/shared/` unless noted. All are standalone, `OnPush`, use `inject()` and signals, and have unit tests with axe.

| Component | Selector | Inputs | Purpose |
|---|---|---|---|
| `DataFrameComponent` | `app-data-frame` | `heading` (required), `expandable`, `context`, `dataThrough`, `serviceLine`, `divisionApplied`, `labels`; output `expandedChange` | The card for any chart or table: title, full screen, a `frameActions` slot, and a footer with the stamp and badge when `dataThrough` is set. |
| `FreshnessStampComponent` | `app-freshness-stamp` | `dataThrough` | "Data through Nov 22 · Updates once a day", formatted in the user's locale. Renders nothing for a missing or invalid date. |
| `ContextBadgeComponent` | `app-context-badge` | `serviceLine`, `divisionApplied`, `labels` | The scope actually applied, as visible text (see [section 3](#the-context-badge)). |
| `DivisionSelectComponent` | `app-division-select` | `mode` (`edit` or `follow`), `selectedKey`, `line`, `unavailable`, `ignoredBy` | The one Reporting Division control, fed by `DashboardFilterContext`. See the rules under [Reporting Division](#reporting-division). |
| `MetricHelpComponent` | `app-metric-help` | `metric` (required), `serviceLine`, `labels` | Summary plus the "What these numbers mean" disclosure. |
| `DataUpdatedNoticeComponent` | `app-data-updated-notice` | none (reads `DataFreshnessService`) | The "Data updated through ... Update view" notice. |
| `ChartComponent` (`charts/`) | `app-chart` | `option`, `label` (the text alternative); output `elementActivate` | The ECharts host. `label` is the `role="img"` text. |
| `SegmentedControlComponent` (`dashboard/`) | `app-segmented-control` | `label`, `options`, `value`, `iconOnly` | The presentation switcher (bar, stacked, donut, table). |
| `LoadProblemComponent`, `SkeletonComponent` | | | Calm error with a retry, and the loading placeholder. |

How they combine for a Metric on a page:

```html
<app-data-frame heading="Payer mix" [dataThrough]="dataThrough()" [serviceLine]="line()" [divisionApplied]="divisionApplied()" [labels]="labels()">
  <app-segmented-control frameActions ... />
  <app-chart [option]="option()" [label]="textAlternative()" (elementActivate)="open($event)" />
  <!-- the same data as a table, and real drill links -->
  <app-metric-help metric="payer_mix" [serviceLine]="line()" />
</app-data-frame>
```

Component rules (from `AGENTS.md`): standalone and `OnPush`, `inject()`, `signal()` and `computed()` for state, `rxResource` or `toSignal` for loading, no plain mutable fields in templates, no component methods inside `@for`, and precompute row view models. Do not read and then write the same signal in an `effect()`: track only the trigger and run side effects in `untracked()`. User-facing terms a Provider can rename come from the Label service, never from literals. Saved View widgets use the same frame, stamp, badge and drill links as the pages.

## 8. Accessibility and responsive rules

The target is WCAG 2.2 AA (ADA and Section 508). A page or component is not done until it passes the Playwright accessibility and keyboard tests, not only axe in jsdom.

- **Every chart has a text alternative** (`ChartComponent.label`) and a table view with the same data.
- **Color is never the only signal.** Rises and falls use text such as "+7 pts".
- **Keyboard:** logical Tab order, visible focus, no trap, a path to every drill link, disclosure buttons that toggle with Enter and Space and close with Escape, a skip link.
- **Targets** are at least 24 by 24 CSS pixels (WCAG 2.5.8).
- **Responsive from 320px to 1920px**, portrait and landscape, light and dark, at 200% text, at 400% zoom (reflow), with the WCAG text-spacing overrides, in forced-colors mode and with reduced motion. No sideways page scroll, no text over text, no text cut off.
  - At 400px and below, a card header puts the title, the subtitle and the view switcher on their own rows.
  - On a narrow bar chart, axis ticks are thinned so labels never overlap.
- **Live announcements:** state changes ("Updated to Nov 23", a moved widget) go through the shared live announcer, politely, without taking focus.
- **Escape untrusted text.** Warehouse text (payer names, group names, vehicles, division names) can contain markup. ECharts tooltips are HTML by default, so every tooltip formatter that interpolates a name must call `escapeHtml` (`shared/escape-html.ts`). Angular bindings already escape, so do not use `innerHTML`, `bypassSecurityTrust*` or `[href]` with data.
- New pages and states add themselves to `e2e/layout.spec.ts`, `accessibility.spec.ts` and `keyboard.spec.ts` in the same change. Do not disable an axe rule to pass.

## 9. Security checklist for a Metric

These come from `AGENTS.md` sections 1 to 9. A Metric change must include the matching tests.

- [ ] **Scope:** the route Provider is checked against the user's assignments. Cross-Provider test (user A asks for Provider B: 404) and a Facility-restricted user versus a sibling division.
- [ ] **No identity from names or payloads:** Provider and division resolve from ids and keys, never display names. Division and group keys are validated by pattern and length before any use.
- [ ] **Untrusted input:** length caps before logging or processing, a maximum count on anything built from input, finite regex timeouts or `NonBacktracking`. An adversarial-input test per bound.
- [ ] **Bounded work:** a 30-second query timeout, a result-size cap, bounded date ranges and group-by cardinality (for example top 10 plus Other).
- [ ] **Aggregate only:** no patient or claim detail. If it is claim-level, it needs its own permission and an audit record and is `NoCache` and `no-store`.
- [ ] **No raw values in logs or errors:** log codes, paths and lengths, not payer names, query values or cache keys. A log-capture test with known markers.
- [ ] **Authorization coverage:** the route is under the authenticated `/api` group. The route matrix test (anonymous gets 401 or 403, an authenticated user without the permission gets 403 or 404) discovers it automatically.
- [ ] **Caching is tenant-safe:** cache keys include the authorized Provider, the ETag filter runs after Provider access, and a 304 is never returned to an unauthorized caller.
- [ ] **Drill links are server-built** from the catalog and the route Provider only. The SPA maps them to its own routes.
- [ ] **Exports** (if any) use the shared CSV writer that quotes and neutralizes formula cells.
- [ ] **Read-only warehouse:** new queries pass `WarehouseSqlPolicy` and use `IWarehouse` only (no own connection, no write or DDL; the guard test over every SQL text enforces it).

## 10. Adding a Metric: the checklist

1. **Catalog:** add the entry in `MetricCatalog` (key, description, unit, category, service lines, dimensions, presentations with one default, `Context`, `Freshness`, `HelpKey` equal to the key, and any `Drills`).
2. **Backend slice:** add the `Foo` folder in Application, Infrastructure and Api (interface and `Definition`, SQL and query class, endpoint, DTOs and `IMetricModule` with a unique `RouteOrder`). Add the cache decorator line in `CachedMetrics.cs` and the Data Through source in `MetricDataThrough`.
3. **Division:** if the source has `reporting_division`, add the filter, an integration test, and list the Metric in the division coverage. If not, leave `DivisionSupport = None`.
4. **Web slice:** add `web/src/app/metrics/<kebab-key>/` with the DTO, loader, widget mapper, renderer entry (`METRIC_RENDERERS`, or `DRILL_TARGETS` for a drill-only view), `help.en.ts` registered in `help-registry.ts`, and any components.
5. **Page or widget:** wrap it in `app-data-frame` with `dataThrough`, `serviceLine`, `divisionApplied` and `labels`, add `app-metric-help`, give the chart a text alternative and a table view, and render any drill links as real links.
6. **Tests:** the unit and integration tests from [section 9](#9-security-checklist-for-a-metric), Vitest specs with axe, and the new page or state in the Playwright layout, accessibility and keyboard specs.
7. **Regenerate:** run the OpenAPI snapshot update and the catalog snapshot update (see [Testing](../README.md#testing)), then review both diffs.
8. **Docs:** add a section to [`warehouse.md`](warehouse.md) with the source table and counting rules, and note anything decided in an ADR if it is hard to reverse and surprising.

## 11. What the tests enforce

| Rule | Test |
|---|---|
| Every Available, placeable catalog entry has exactly one `IMetricModule` and every module points at an Available entry | `MetricModuleRegistryTests` |
| Keys unique and lowercase; every entry has a known category; exactly one default presentation; every Metric is readable as a table; `HelpKey` equals `Key`; `NoCache` only for claim-level; claim-level never Available | `MetricCatalogTests` |
| Drills target an Available entry, bind a dimension the target has, and form no cycle or deep chain | `MetricCatalogTests` |
| A Metric claims `division` only if listed with an integration test | `MetricCatalogTests` (division coverage) |
| `docs/metrics.md` and `catalog.fixture.json` match the catalog | `MetricCatalogSnapshotTests` |
| Every Available (placeable) Metric has a web renderer, and a renderer's presentations are in the catalog | `metric-renderers.spec.ts` |
| Every catalog key and drill target has help text; summaries are short; glossary terms unique; no "payor" or hard-coded "base"; placeholders resolve | `help-registry.spec.ts` |
| The OpenAPI document changes only on purpose | `OpenApiSnapshotTests` |
| Cross-Provider and Facility-sibling requests are refused for every division-aware route | `DivisionScopeEndpointTests` |
| A 304 is never returned to an unauthorized caller; claim-level routes have no ETag and `no-store` | `MetricCacheEndpointTests`, `ClaimAttentionEndpointTests` |
| One tenant cannot starve another; the cache is size-capped | `MemoryMetricCacheTests` |
| Drill links name the route Provider, are omitted when not allowed, and the SPA accepts only known targets | `DrillLinkBuilderTests`, `drill-link.spec.ts` |
| Hostile names are escaped in tooltips and shown literally | `payer-mix.spec.ts`, `net-cash-chart.options.spec.ts`, `e2e/xss.spec.ts` |
| Layout, axe, keyboard and text alternatives hold at every size and mode | `e2e/layout.spec.ts`, `accessibility.spec.ts`, `keyboard.spec.ts` |
| The "Data updated" notice appears without swapping figures and is keyboard operable | `e2e/data-updated.spec.ts` |
| A Saved View stores only keys, presentations and positions | `view-builder.component.spec.ts` |
