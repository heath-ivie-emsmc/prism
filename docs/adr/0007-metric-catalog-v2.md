# 7. Metric Catalog v2: per-metric slices, catalog-owned trust fields

Date: 2026-11-23

## Status

Accepted. Parts are implemented and parts are planned; each decision below says which.

## Context

Each Metric was spread across flat folders, one endpoints file and hand-wired UI, so adding one touched many places and nothing failed when a piece was missing.
Users also need to trust a figure: which scope it used, how fresh it is, what it means, and where to go next. These facts belong to the Metric, not to each page.

## Decision

- **Metrics are slices (implemented).** Each Metric has a same-named folder in `Prism.Application`, `Prism.Infrastructure` and `Prism.Api`. It implements `IMetricModule`, which
  registers its routes and has a `RouteOrder` so route matching is deterministic. On the web, `METRIC_RENDERERS` maps a Metric key to its renderer under `web/src/app/metrics/<kebab-key>/`.
- **Registries fail the build (implemented).** Tests fail when a catalog entry has no module, renderer or help entry, or the reverse, and a catalog fixture is cross-checked against the C# catalog.
- **The catalog owns the trust fields (implemented).** `Context` (Provider scope, Service Lines, Division support, group dimensions, time grain), `Freshness`, `HelpKey` and `Drills`
  are on each `MetricCatalogEntry`, so the palette, the badges and the API read one source.
- **Help sentences belong to the SPA, keyed by `helpKey` (implemented).** The catalog carries the key only. Trade-off: the backend cannot render help text, so a PDF or email
  export has to bring its own copy or leave it out. For the same reason glossary keys are not catalog data: the SPA owns the glossary sentences, keyed by `helpKey`, and the catalog has no glossary list.
- **Freshness is `DailyLoad` or `NoCache` only (implemented).** `DailyLoad` is cached against the Data Through watermark and `NoCache` is never cached (claim-level). No other policy exists, because no Metric needs one: a `Static` value would have no user. Add a value when a Metric truly needs it, not before.
- **Freshness is one Data Through stamp (implemented in the UI).** `snapshot_date` is the load date and has no time of day, so Prism shows that date and does not promise a "last updated" time.
- **Always-revalidate HTTP caching (implemented).** Successful Metric responses are `Cache-Control: private, no-cache` with a strong `ETag` of the body, set only on a 200 and checked after the Provider check, so an unauthorized caller never gets a 304; claim-level responses are `no-store` with no validator. The catalog route is `private, max-age=300` on a 200 only. `If-None-Match` is capped at 2048 characters and 32 tags, and anything over is ignored.
- **One Data Through source (implemented).** Every response (Metrics, the "what changed" Insight and the claim Attention Checks) reports the date from `IDataThroughReader`, using the tables listed in `MetricDataThrough`. Claim responses are still never cached.
- **Data-updated notice (planned).** When a new load arrives while a page is open, a notice with a button offers a refresh. The page never swaps figures by itself.
- **Drills navigate (implemented on the API).** A Drill opens a new URL, and the links are issued by the server from the catalog (`DrillLinkBuilder`), never built from client input. Each `payer_mix` share carries `links` (`rel` `drill`) to `payer_mix_detail`, the one target so far. A link names the route Provider and only the filters in effect, and is left out for Facility, Patient responsibility and Not recorded, for a Service Line the target does not serve, or when the caller lacks the target's permission. A drill target is an Available catalog entry that is not placeable on a Saved View, or one that is placeable and serves a clicked value through a dimension of its own. A `MetricDrill` is `rel`, `targetKey` and the target dimension the clicked value fills (`BindsDimension`); it has no `from`, because the source element is implied by that bound dimension (a payer group fills the target's path, a breakdown group fills its `group` filter). `net_cash_breakdown` groups carry `links` (`rel` `drill`) to `payer_mix`, binding `group`.
- **Division only where the warehouse has it (implemented for the six trip-table Metrics).** A Metric lists the `division` dimension only when its source has `reporting_division` and an integration test proves the filter. The others keep `DivisionSupport.None` and the Context Badge says the filter was not applied.

## Consequences

- Adding a Metric is a checklist the tests enforce (see `docs/metrics.md`).
- Help text changes ship with the SPA, not the API.
- The server cache, `dataThrough` on every Metric response, Division support and the drill API (`links` and `payer_mix_detail`) are implemented (see `warehouse.md`). The UI for the data-updated notice and drill-down is separate and is not claimed here.
