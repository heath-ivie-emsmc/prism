## 1. Data and docs
- [x] Use the `emsmc-database-designer` skill for Provider users (portal user id, Provider, display name, role key, last sign-in) and Provider settings (default role, allow restricted grants); CI migration
- [x] Update `docs/auth-setup.md`; accept ADR 0010; note the Customer Role glossary entry for the user

## 2. Roles and per-request evaluation
- [x] Role catalog in code next to `Permissions.cs`, with restricted marking and the new `prism.users.manage` permission
- [x] Narrow the session permission claim to sign-in and staff tiers (`AdminPortalSetup` publishes only sign-in, the internal marker and the admin tiers; the old customer roles are removed from the catalog)
- [x] Customer permission policy that reads the Provider from the route, checks it against assignments, then the user's role
- [x] Short, size-capped role cache with invalidation on change; unreadable role means no permission
- [x] Switch every customer route's policy to the new one
- [x] `/me` returns the permissions the user holds in each Provider

## 3. Recording users and defaults
- [x] Upsert Provider users at sign-in (bounded); default role for a first row; skip Internal Users

## 4. Customer admin API
- [x] List a Provider's users (bounded page) and change a role, under `prism.users.manage`
- [x] No self-change, no grant above Provider admin, restricted roles only when allowed, last admin protected
- [x] Audit each change in the same transaction

## 5. Staff API
- [x] List and change roles in a named Provider, set default role and restricted-grant switch, under the admin manager tier
- [x] Audit each change

## 6. Web
- [x] Customer `/settings/users` page for Provider admins
- [x] Staff `/admin/customers` page with a Provider chooser
- [x] Apply per-Provider permissions to navigation and actions; an "ask your administrator" state for a user with no useful role

## 7. Tests (AGENTS.md sections 1, 2, 3, 4, 5, 8)
- [x] Authorization matrix and PHI matrix prove nothing was widened by the policy switch
- [x] Cross-Provider: admin of A cannot list or change B; same user with different roles in two Providers
- [x] Portal role grants no customer permission; unknown role; unreadable role denies
- [x] Self-change, above-admin grant, restricted grant (off and on), last admin
- [x] Request-sent role, permission, Provider, acting user or delegation switch ignored
- [x] Bounds: page size, body size, role changes per request, cache size
- [x] Audit in the same transaction; failure rolls the change back; no names in logs
- [x] Sign-in records users, applies default role, skips Internal Users
- [x] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for both pages; `npm run e2e` and read screenshots
- [x] Regenerate the OpenAPI snapshot deliberately

## 8. Verify
- [x] `dotnet test`; `npm test` and `npm run e2e` in `web/`
