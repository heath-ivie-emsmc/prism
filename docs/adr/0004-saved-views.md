# 4. Saved Views: layouts of Metric keys, shared by invitation

Date: 2026-11-22

## Status

Accepted

## Context

Users want to build their own dashboards: pick Metrics from a categorized list, drop them on a grid, save the arrangement, and share it with colleagues
in the same Provider. The Metric Catalog is meant to move toward a semantic API, so a saved view must not depend on how a Metric is queried. The shared
rules still hold: the Provider comes from the route and is checked on every request, responses are aggregate-only, and untrusted input must not set trusted state.

## Decision

- **A view is a layout.** `saved_view.layout` is jsonb: `{ version, density, widgets: [{ id, metricKey, presentation, options, x, y, w, h }] }`. It holds no
  figures, no Labels, no filter values and no endpoint or SQL. A reader gets data through the normal Metric routes, so sharing never widens what anyone can see.
- **The Metric Catalog lists what can be placed.** `MetricCatalog` (Prism.Application) is the one registry of Metrics, categories, presentations, options,
  dimensions and status (`available` or `planned`). `GET /providers/{id}/metrics/catalog` serves it. The palette, the layout validator and the widget settings
  form all read it, so a new Metric or option is catalog data, not UI code. This is the seam the semantic API replaces. Claim-level Metrics stay `planned` until they have a PHI permission and an audit record.
- **The server validates the layout** (`LayoutValidator`): at most 24 widgets on 12 columns, no overlap, known and placeable Metrics, allowed presentations,
  options and sizes, 32 KB. It answers with codes and never the value sent. The stored JSON is written from the validated object, so unknown properties never persist.
- **Owner and Provider are never read from a body.** They come from the session and the checked route Provider. A view is loaded by Provider and then by
  owner or share membership; anything else is a 404. Saves use optimistic concurrency (`xmin` as the `ETag`, `If-Match` on `PUT`, 412 on a stale save).
- **Sharing is by emailed invitation.** There is no user directory to pick from. The owner enters addresses; each gets a link with a random 32-byte token
  that works once and expires (default 7 days). Only the SHA-256 of the token is stored. Redeeming needs a signed-in user with access to the view's Provider
  and consumes the token in one conditional update, so two people racing for one link cannot both win. Every failure is the same 404. The session carries no
  email claim, so the link itself is the credential: single use, short lived, revocable, and the owner sees who redeemed it.
- **Invitations are off by default** (`SavedViews:Invitations:Enabled`). A host turns them on in its own `appsettings.<Environment>.json` with an https public
  address, an SMTP host and a sender address; the SMTP credentials come from Secrets Manager. Options are validated at start-up.
- **New permission** `prism.views.manage` (create, edit, delete, share) for analysts, admins and super users. Reading a shared view needs only `prism.dashboard.view`.
- **Web.** The page filters (Provider, Service Line, period, Reporting Division) live in one `DashboardFilterContext` shared with the dashboard. Each widget
  reads those signals as the params of its own resource, so a filter change refetches every widget with no event wiring, and the page announces it once.

## Consequences

- The Reporting Division filter reaches the Metrics that read `reporting.trip_detail_snapshot` (payer mix, transports, payment age, cash per transport, net cash breakdown, net cash by group),
  which list the `division` dimension and say `DivisionSupport.Supported` in the catalog. The others (net cash, projection, clean claim rate) ignore it, and their widgets say so. A Saved View still
  stores no division: the filter is the page's, and it is validated against the Provider's own divisions on every request. A test fails if a Metric claims the dimension without an integration test.
- Planned Metrics are listed in the palette as "coming soon" and cannot be placed or saved.
- Moving the layout to a semantic API changes the catalog and the widget data service, not stored views.
- Using more of the shared UI library (dialogs, live announcer) from a lazy route adds that code to the initial chunk, because the library is one flat module:
  the initial bundle grew from about 622 kB to 679 kB (the 700 kB error budget still holds; the 600 kB warning already fired). Splitting the library by
  entry point, or raising the budget, is a follow-up.
- Email delivery needs an SMTP relay (Amazon SES supports SMTP). Until a host enables it, sharing answers 503 with a calm message.
