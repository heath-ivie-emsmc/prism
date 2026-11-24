# Prism agent notes

Prism is the revenue cycle management (RCM) BI dashboard for ambulance and fire personnel: a .NET 11 API plus an Angular SPA over a Postgres data warehouse. Domain vocabulary is in `CONTEXT.md` (use those terms exactly: Provider, Reporting Division, Service Line, Transport, Metric, Metric Catalog, Data Through, Label). Architecture decisions are in `docs/adr/`.

These rules are adapted from the M6 (Nemsis) project, where each one came from a real finding. They are binding. A change that touches a listed area must include the listed tests in the same change.

## Architecture decisions already made

- **Tenant is a Provider.** The active Provider is in the route (`/api/v1/providers/{providerId}/...`) and is checked against the user's assignments on every request. It is never stored in the session or taken from a body or header.
- **The API owns the Metric Catalog** and queries the read-only warehouse directly. Each Metric is defined once and feeds both dashboard cards and the public API and its OpenAPI docs.
- **Responses are aggregate-only by default.** Patient-level or claim-level data needs its own permission and an audit record.
- **The warehouse is read-only.** No schema change, migration, write or DDL against it from any code, test or tool. Warehouse access goes through `IWarehouse` only; every query passes `WarehouseSqlPolicy`, runs in a read-only transaction, and the startup check refuses a login that can write. A change to the warehouse is a request to the database team, never a code change. See `docs/adr/0008-warehouse-write-protection.md`.
- **State:** Prism's own Postgres holds durable state (Provider settings, saved views, audit log, session revocation). Redis holds short-lived security state (rate-limit counters, login replay protection, pending SSO state). The warehouse connection is read-only.
- **Auth:** the Account Portal signed login-token flow, then an HttpOnly cookie session issued by the API. Browser code never stores tokens. SAML and other federation are deferred and must plug in behind the auth provider seam.
- **One container** serves the SPA and the API. Customer API traffic uses bearer tokens on separate routes.
- **Saved Views are layouts, not data.** A view stores Metric keys, presentations and grid positions as JSON validated against the Metric Catalog. It never stores figures, Labels, filters, an owner or a Provider taken from a request. Sharing is by emailed single-use invitation. See `docs/adr/0004-saved-views.md`.
- **Personalization is renaming only.** A Label never changes an API key. Defaults come from Service Line.

## Security rules

### 1. PHI: gate, mask, and audit
- Any endpoint or DTO that can carry PHI checks the PHI permission server-side. PHI includes patient names, addresses, GPS, dates of birth, clinical findings, narratives, claim identifiers that map to a patient, and file names built from patient data. Without the permission, mask or omit the fields. Exports, downloads and secondary views follow the same rule as the main view.
- Every PHI read writes an audit record.
- Mark PHI-bearing DTO properties with a PHI attribute.
- **Tests required:** per endpoint, one test as a user with PHI access (fields present, audit row written) and one without (fields absent, no PHI substring anywhere in the JSON body). A PHI matrix test fails when a DTO with a PHI attribute is returned by a route that is not in the matrix.

### 2. Tenant scope: authorize on identity, not names or payload values
- Scope every query by the Provider in the route after checking it against the user's assignments from the portal, and by Facility scope where the user has one. A broad role never widens what a narrower assignment allows.
- Never resolve a Provider or Facility from a display name, natural key or submitted payload field.
- Warehouse queries get the Provider filter from the Metric Catalog, never from an individual endpoint.
- **Tests required:** for every new or changed endpoint or catalog query, a cross-Provider test where user A requests Provider B's data (expect 403 or 404), plus a test for a Facility-restricted user against a sibling Facility.

### 3. Untrusted input must not set trusted state
- Flags that control authorization, auditing or scoping are set by server code only. Strip or reject them when they arrive in a request.
- **Tests required:** send a request containing the flag and assert it is ignored or rejected.

### 4. Resource bounds on attacker-reachable paths
- Every `Regex` or `[GeneratedRegex]` has a finite match timeout or `RegexOptions.NonBacktracking`, and its input has a length cap.
- Cap lengths before processing or logging any submitted value.
- Every collection built from input has a maximum count. Every cache has a size or TTL limit (Metric caches are keyed on the Data Through watermark, so they also have a size cap).
- Every Metric Catalog query has a timeout (30 seconds by default) and a result-size cap. Date ranges and group-by cardinality are bounded.
- Before authentication, never allocate from a client-declared `Content-Length`, and put the concurrency gate before any parsing.
- Failed-auth budgets are checked before expensive work. Rate-limit partition keys are normalized the same way as the lookup.
- Anonymous health endpoints (`/ready`) cache their result briefly, share one check between concurrent callers, and have a short timeout.
- **Tests required:** an adversarial-input test per bound, and an architecture test that fails on any `Regex` or `[GeneratedRegex]` without a timeout or `NonBacktracking`.

### 5. No PHI or raw input in logs or stored error text
- Never log request bodies, query text with literal values, or error messages that quote field values. Log codes, paths and lengths.
- **Tests required:** feed a payload with known PHI markers through the failure path and assert captured logs and stored errors do not contain them.

### 6. Exports
- CSV and Excel output always quotes cells and neutralizes cells that start with `=`, `+`, `-`, `@`, tab or CR, including cells with embedded double quotes. Use one shared writer.
- **Test required:** a formula payload, with and without embedded quotes, in each exported text column.

### 7. Authentication and transport
- The client IP for rate limits comes from the trusted forwarded-header configuration, never the proxy address or an unvalidated header.
- No request header may change throttling, auth or metrics in deployed environments.
- Unknown-user and known-user login paths take the same time.
- Post-login redirects accept only same-origin relative paths: reject control characters, `//` and backslashes. Bind SSO callbacks to the initiating browser with a state cookie.
- Login tokens are single use (replay store in Redis) and short lived. Session revocation takes effect on live sessions.
- Service-to-service calls use TLS. Deployed config must not contain `http://` URLs for internal services.
- **Tests required:** spoofed `X-Forwarded-For`, a test-mode header in a deployed environment, redirect bypass strings (`/\t/evil`, `//evil`, `/\\evil`), callback replay from another browser, token replay, and a config test that fails on `http://` internal URLs in deployed appsettings.

### 8. Authorization coverage
- The `/api` route group requires an authenticated user by default. Intentional public routes use `.AllowAnonymous()` and must be listed with a reason in the documented anonymous-routes list used by the authorization matrix test. Health probes (`/up`, `/ready`) stay anonymous.
- Permission-gated routes use named policies. A new policy needs a handler test.
- An endpoint-authorization matrix test discovers every route at runtime and asserts: anonymous gets 401 or 403, and an authenticated user without the permission gets 403 or 404.
- Services return `null` for a missing or out-of-scope resource, which maps to 404, deliberately merging "not found" with "not visible". Typed domain exceptions map to specific 4xx ProblemDetails in one place.

### 9. The warehouse is read-only
- Prism never changes the warehouse: no `Migrations` for it, no `migrationBuilder`, no `.sql` file or production SQL with DDL or DML against `reporting` or `ods`, no EF context on its connection. Only `src/Prism.Infrastructure/Warehouse` and the single registration point read `ConnectionStrings:Warehouse`.
- Every warehouse query goes through `IWarehouse`. Each passes `WarehouseSqlPolicy` (no write, DDL or administrative verbs, no locking clauses, no side-effect functions, literals and comments ignored) and runs in a transaction that starts `SET TRANSACTION READ ONLY` and is always rolled back. The database login having SELECT only is the real guarantee: at startup, in every environment including Development and Testing, the check refuses to start the host if the login is a superuser, can create anything, or can write to or own anything in any non-system schema, or if it cannot be checked.
- Tests and tools never connect to a real warehouse. Test code reaches a database only through `PostgresFixture` (a Testcontainers database) and `TestDatabaseGuard.Assert`, and never reads `ConnectionStrings:Warehouse` from user secrets, the environment or real configuration (a fake `dw.invalid` string in a wiring test is fine). A developer with a production string in their secrets must be unable to hit it from `dotnet test`.
- **Tests required:** the startup check against Testcontainers roles (superuser refused, SELECT-only passes, INSERT, CREATE, ownership and write-role membership refused, no connection string or password in the message); a write sent past the policy is rejected by the server (SQLSTATE 25006) in the read-only transaction; the policy tested against malicious and harmless inputs, and over every query in the code; and the architecture guards in `WarehouseProtectionGuardTests` (test connections only through the guard, no warehouse connection outside the warehouse folder, no migrations or `.sql` for the warehouse, no DDL or DML against its schemas).

### Definition of done
1. List which of sections 1 to 9 the change touches in the PR description.
2. Add the required tests for each one. The positive case alone is not enough: include the denied, cross-Provider or adversarial case.
3. Run the full build and tests, and the web unit tests if the web app changed. If a page or component changed, also run `npm run e2e` in `web/` (layout, accessibility, keyboard at every size) and look at the screenshots.

## Development rules

- **Code style:** follow `.editorconfig` (indentation, line endings, namespaces, analyzer severities). Build uses `TreatWarningsAsErrors`, so fix warnings, do not suppress them. A pragma suppression needs a comment saying why.
- **Method parameters:** at most 7 (Sonar S107). Fix CA1859 when it appears.
- **Complexity:** prefer early returns and guard clauses, split large methods, and extract a named piece per concern in middleware and validators. Do not add another `if` level to a method already near the limit.
- **Primary constructors:** prefer them for new DI-injected types with no complex initialization. Do not mass-rewrite existing types.
- **Clock and ids:** do not call `DateTime.UtcNow` or `Guid.NewGuid()` in application or infrastructure code. Inject `TimeProvider` and an id provider. Store time as UTC and convert at the edge to the Provider's time zone.
- **Host composition:** register services in one composition extension per host so DI can be tested as a whole.
- **Environment guards:** use a single `IsDeployedEnvironment` helper (true for every environment other than `Development` and `Testing`), never `env == "Production"`, to gate hardening every cluster needs.
- **Configuration:** one `appsettings.<Environment>.json` per deployed environment. Anything whose value depends on the environment (hosts, bucket and queue names, secret ids, CORS origins, portal URLs, application ids) goes there, not in base `appsettings.json`. Base leaves resource identifiers empty on purpose, so a host with no environment file fails options validation instead of reusing another environment's resources. Use the options pattern with `ValidateOnStart`. Secrets come from AWS Secrets Manager, never from the repo.
- **Metric and component design guide:** before you add or change a Metric, a metric component, assistive text, a Drill or a context filter (Provider, Service Line, Reporting Division), read `docs/metric-design-guide.md`. It is binding like the rules in this file. It says how a Metric shows its Context Badge, Data Through stamp, help text and Drill links, and lists the security, accessibility and add-a-Metric checklists. Help sentences live in the SPA keyed by `helpKey`, and Drill links are issued by the server and mapped by the SPA, never built from client input.
- **New Metric:** same-named slice folders in `Prism.Application`, `Prism.Infrastructure` and `Prism.Api/Metrics` with an `IMetricModule`; a `MetricCatalog` entry; a web `metrics/<kebab-key>/` folder with a `METRIC_RENDERERS` entry (or `DRILL_TARGETS` for a drill-only Metric); and a `help.en.ts` entry keyed by its `helpKey`. Regenerate `docs/metrics.md` and `web/src/app/metrics/catalog.fixture.json` with `PRISM_UPDATE_SNAPSHOTS=1 dotnet test tests/Prism.Tests --filter MetricCatalogSnapshotTests`. The drift and registry tests fail the build otherwise.
- **Database:** use the `emsmc-database-designer` skill (`.claude/skills/emsmc-database-designer/`) for every schema change. It carries the DBA team's conventions, which are binding. EF Core and PostgreSQL only. Migrations run in a deliberate CI step, never at application startup. Configure the DbContext through one shared extension, so a guard test can fail on direct `UseNpgsql` calls. The warehouse connection is read-only and has no migrations.
- **API documentation:** every endpoint has a summary, description, response types (including ProblemDetails errors) and examples. Metric definitions flow into the OpenAPI document from the Metric Catalog. CI compares a snapshot of the generated document and fails on an unreviewed breaking change. Routes are versioned under `/api/v1`.
- **Shell and installs:** do not run or suggest `winget`.

## Testing rules

- **Test projects:** unit tests mock dependencies and use no real database, Redis, Testcontainers or HTTP host. Integration tests (Postgres, Redis, `WebApplicationFactory`, full host DI) go in the integration project. Shared fixtures go in a shared testing project. Add those projects when the first integration test appears, and add a CI check that fails if a unit project references them.
- **No EF InMemory.** Integration tests use real PostgreSQL through Testcontainers. Never `EnsureCreated`; apply migrations.
- **CancellationToken in tests:** pass `TestContext.Current.CancellationToken` to any method that takes a token. Never `default`, `CancellationToken.None` or an omitted argument. Analyzers run on test projects here, so xUnit1051 catches this at build time.
- **xUnit v3** with `xunit.v3.mtp-off` and `xunit.runner.visualstudio`, versions managed centrally in `Directory.Packages.props`. Test names may use underscores.
- **Test before refactor:** before restructuring code without changing behavior, make sure tests lock in the current behavior. Confirm green, refactor in small steps, and re-run after each step. Do not edit test expectations to make a refactor pass unless a behavior change was requested.

## Web (Angular) rules

- Standalone components, `ChangeDetectionStrategy.OnPush` on every component, and `inject()` instead of constructor injection.
- Component state uses `signal()` and `computed()`. Do not bind plain mutable fields in templates. Prefer `rxResource` or `toSignal` over manual load-and-subscribe.
- **`effect()` feedback loops:** an effect re-runs when any signal it reads changes. Do not read a signal and then write that same signal in the effect body. Track only the trigger signal and run side effects inside `untracked()`.
- **Templates:** do not call component methods inside `@for` or table row bindings. Precompute row view models in `computed()` or in pure display mapper functions.
- **Labels:** user-facing terms that a Provider can rename come from the Label service, never hard-coded strings.
- **Auth in the browser:** no tokens in `localStorage` or `sessionStorage`. The HTTP interceptor sends credentials and the CSRF header, and handles 401 once. Sanitize every return URL.
- **Accessibility, from day one:** the target is WCAG 2.2 AA (ADA and Section 508). Run axe in component tests. Charts need a text or table alternative. A page or component is not done until it passes the Playwright accessibility and keyboard tests below, not only axe in jsdom (jsdom has no layout, so contrast, target size, focus visibility and overlap are only checked in a real browser).
- **Responsive, from day one:** every page works from a 320px phone to a 1920px desktop, in portrait and landscape, light and dark, at 200% text size and 400% zoom (reflow), with WCAG text-spacing overrides, in forced-colors mode, and with reduced motion. No sideways page scroll, no text over other text, no text cut off or outside its card.
- **Everything is tested through Playwright, automatically.** `web/e2e/` holds the browser tests. `npm run e2e` in `web/` starts the app itself if it is not already running (it reuses a running one) and needs no account: `/api/v1/me` is mocked unless `PRISM_E2E_STORAGE_STATE` supplies a real session. No test may depend on a manual step. Layout analysis (`e2e/layout-analysis.ts`), axe at WCAG 2.2 AA, keyboard (Tab order, visible focus, no trap, menus, skip link) and text-alternative checks run on every page at every size in `e2e/layout.spec.ts`, `accessibility.spec.ts` and `keyboard.spec.ts`. A new page or state adds itself to those specs in the same change. Read the screenshots in `web/e2e-report/` too: the checks cannot see canvas text or plain ugliness. Do not disable an axe rule to pass; a rule exclusion needs a comment naming who owns the fix.
- Tests use Vitest with an 80% coverage gate, plus Playwright for end-to-end and accessibility tests.

## Git and CI

- **Commits:** never run `git commit` unless the user asked for one in the current request. Show the full proposed message first and wait for approval. Do not use `--no-verify`, `--amend` or force-push unless asked. Do not stage files the user did not intend to include.
- **No agent attribution:** never add attribution to Claude, Cursor or any AI coding agent unless the user explicitly asks. That covers commit messages and trailers (no `Co-Authored-By`), pull request titles and bodies (no "Generated with" footer), source comments, file headers and docs. Write commits and PRs as normal engineering output.
- **GitHub Actions pinning:** every third-party action is referenced by its full 40-character commit SHA with the version in a trailing comment. CI runs a script that fails on unpinned actions.
- Workflows triggered by pull requests never run on persistent or privileged self-hosted runners that deploy jobs share. Production credentials live only in environment-scoped secrets behind protected environments.
- Dependency installs in CI are pinned and lock-file based (`npm ci`, locked restore).
