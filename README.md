# Prism

Prism is the revenue cycle management (RCM) business intelligence dashboard for ambulance and fire personnel. It serves air medical and ground transport customers from a shared data warehouse. A .NET 11 API and an Angular single-page app sit over a read-only Postgres warehouse, and the API also owns a small Postgres database of its own (Provider settings, Saved Views, audit log, session revocation).

Domain vocabulary is in [`CONTEXT.md`](CONTEXT.md). Use those terms exactly (Provider, Reporting Division, Service Line, Transport, Metric, Metric Catalog, Data Through, Label, Drill, Context Badge). Binding engineering rules are in [`AGENTS.md`](AGENTS.md). Architecture decisions are in [`docs/adr/`](docs/adr/).

## Contents

- [Quick start](#quick-start)
- [Repository layout](#repository-layout)
- [Testing](#testing)
- [Documentation map](#documentation-map)
- [Metric and component design guide](#metric-and-component-design-guide)

## Quick start

Requirements: the .NET SDK pinned in `global.json` (11.0.100, roll forward to the latest feature band), Node with npm 12 (`web/package.json` pins `packageManager`), Docker for the integration tests (they use Testcontainers).

```bash
# API (listens on http://localhost:5080 in Development)
cd src/Prism.Api
dotnet user-secrets init
dotnet user-secrets set "Auth:SessionSigningKeyBase64" "$(openssl rand -base64 32)"
dotnet user-secrets set "Auth:ApplicationPrivateKeyPem" "$(cat prism-localhost.pem)"
dotnet run

# Web (the dev server proxies /api to the API)
cd web
npm ci
npm start        # http://localhost:4200
```

Sign-in, the Account Portal, the warehouse connection and every other setting are described in [`docs/auth-setup.md`](docs/auth-setup.md) and [`docs/warehouse.md`](docs/warehouse.md). Secrets never go in the repo. Locally they live in user secrets, and deployed environments read them from AWS Secrets Manager. The web e2e suite needs no account: it mocks `/api/v1/me` and every Metric response (see [`docs/e2e-layout.md`](docs/e2e-layout.md)).

## Repository layout

```
src/
  Prism.Api/             HTTP host: routes, auth, middleware, OpenAPI
    Metrics/<Metric>/    one folder per Metric: <Metric>Endpoints.cs + <Metric>Module.cs
    Metrics/Shared/      IMetricModule, ProviderAccess, DrillLinkBuilder, endpoint support
  Prism.Application/     domain types and the Metric Catalog (no I/O)
    Metrics/<Metric>/    the Metric's request and result types and its Definition
  Prism.Infrastructure/  warehouse access, the metric cache, Redis, Prism's own database
    Metrics/<Metric>/    the Metric's SQL and query class
tests/
  Prism.Tests/              unit tests (no database, Redis or HTTP host)
  Prism.Tests.Integration/  Postgres (Testcontainers), WebApplicationFactory, OpenAPI snapshot
  Prism.Testing.Common/     shared fixtures
web/
  src/app/metrics/<key>/    one folder per Metric: DTO, loader, widget mapper, renderer, help text, components
  src/app/shared/           shared frame, badge, stamp, help panel, notice
  e2e/                      Playwright: layout, accessibility, keyboard and page specs
docs/                       setup, warehouse, ADRs, generated Metric table
tools/                      Prism.Utility.AdminPortalSetup (publishes the permission catalog to the portal)
```

## Testing

Warnings are errors. Run from the repo root unless noted.

```bash
dotnet test                      # unit + integration (integration takes a few minutes)

cd web
npm run test:ci                  # Vitest with the 80% coverage gate
npm run lint
npm run e2e                      # Playwright; starts the app itself if it is not already running
```

After a page or component change, read the screenshots in `web/e2e-report/` too. The layout checks cannot see canvas text.

The OpenAPI document is snapshot-tested. When a change to a route, a DTO or a description is intended, refresh the snapshot on purpose and review the diff:

```bash
PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests.Integration --filter "FullyQualifiedName~OpenApiSnapshotTests"
```

The Metric table in `docs/metrics.md` and `web/src/app/metrics/catalog.fixture.json` are generated from the catalog the same way:

```bash
PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests --filter MetricCatalogSnapshotTests
```

## Documentation map

| Document | What it covers |
|---|---|
| [`CONTEXT.md`](CONTEXT.md) | Domain vocabulary and the words to avoid |
| [`AGENTS.md`](AGENTS.md) | Binding engineering, security, testing and web rules |
| [`docs/metric-design-guide.md`](docs/metric-design-guide.md) | How a Metric and its components are designed: context, freshness, assistive text, drill-down, checklists |
| [`docs/metrics.md`](docs/metrics.md) | Generated table of every Metric, plus the add-a-Metric steps |
| [`docs/warehouse.md`](docs/warehouse.md) | Warehouse connection, source tables and the counting rules for each Metric |
| [`docs/auth-setup.md`](docs/auth-setup.md) | Sign-in, the Account Portal, local setup |
| [`docs/e2e-layout.md`](docs/e2e-layout.md) | The Playwright layout, accessibility and keyboard suites |
| [`docs/saved-views.md`](docs/saved-views.md), [`docs/saved-views-ui-spec.md`](docs/saved-views-ui-spec.md) | Saved Views |
| [`docs/adr/0007-metric-catalog-v2.md`](docs/adr/0007-metric-catalog-v2.md) | Why Metrics are slices and the catalog owns context, freshness, help keys and drills |

## Metric and component design guide

Every Metric is defined once in the Metric Catalog and shown the same way: a Context Badge for the scope it used (Provider, Service Line, Reporting Division), one Data Through stamp, plain-language help, and Drill links to more specific views. The full guide is in [`docs/metric-design-guide.md`](docs/metric-design-guide.md). It covers:

- what a Metric is made of (catalog entry, backend slice, web slice, help text) and the response envelope;
- context filtering: Provider scope, Service Line, Reporting Division and the Context Badge;
- freshness and caching: Data Through, the server cache, ETag with 304, and the "Data updated" notice;
- assistive text: who owns it, how to write it and how it renders;
- drill-down: how a Drill is declared, how the server issues links and how the SPA follows them;
- the shared components, accessibility and responsive rules, the security checklist, the add-a-Metric checklist, and which test enforces each rule.

Read it before adding or changing a Metric.
