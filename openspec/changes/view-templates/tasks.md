## 1. Data and docs
- [x] Use the `emsmc-database-designer` skill for the tables: templates, assignments, deliveries (unique per assignment, user, Provider) and a source-template reference on Saved Views; migration in the CI step
- [x] Update `docs/saved-views.md`; note the glossary amendments for the user

## 2. Template authoring
- [x] Template create, edit, list, delete under the admin route group using the author tier
- [x] Reuse layout validation against the Metric Catalog; reject invalid layouts with codes only
- [x] Preview using the existing widget renderer with no figures stored
- [x] Web: template builder reusing the Saved View builder

## 3. Assignments
- [x] Create Assignment (all Providers, specific Providers, Persona) and revoke
- [ ] Pre-push report: Providers, widgets applying per Provider, template version (informational)
- [x] Confirmation step required before any push
- [x] Audit record per push and per revoke
- [x] Aggregate delivery report

## 4. Delivery
- [x] Clone on the Saved View list path, scoped to the route Provider and the user's assignments
- [x] Idempotent per assignment, user and Provider; bounded per request
- [x] "From EMS" marker on delivered views
- [x] Revoke stops future deliveries and leaves copies

## 5. Tests (AGENTS.md sections 2, 3, 4, 5, 8)
- [x] Authorization matrix covers all new admin routes; handler tests for tier use; non-internal, wrong tier, anonymous denial
- [x] Cross-Provider: user A cannot read Provider B's delivered views; delivery scoped to the route Provider
- [x] Facility-restricted user versus a sibling Facility
- [x] Request-sent owner, Provider or source template ignored
- [x] Delivery idempotence on repeated sign-in and concurrent requests
- [ ] Bounds: widget count, target list size, deliveries per request, report page size
- [x] Invalid layout rejected; logs and stored errors contain no submitted text
- [ ] Empty-state widget delivered for a non-applying Metric
- [x] Revoke leaves delivered copies and stops new ones
- [ ] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for each new page; `npm run e2e` and read screenshots
- [x] Regenerate the OpenAPI snapshot deliberately

## 6. Verify
- [ ] `dotnet test`; `npm test` and `npm run e2e` in `web/`
