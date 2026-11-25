## 1. Decisions and docs
- [x] ADR 0009: internal access and impersonation (access part)
- [x] Update `docs/auth-setup.md` (group claim, tiers, portal dependency)
- [x] Add Internal User to the glossary (CONTEXT.md is edited in a separate step by the user)

## 2. Configuration
- [x] Internal options: group name, validated on start, empty in base configuration, required in deployed environments
- [x] Per-environment values for the group name

## 3. Session and policies
- [x] Read group membership from the portal at sign-in; fail closed when absent
- [x] Issue the Internal User claim only on an exact group match; strip client-sent flags
- [x] Four tier permissions; publish them through the portal setup tool
- [x] Named policies requiring the claim and the tier permission

## 4. Admin route group
- [x] Admin route group, authenticated by default, with its own policies
- [x] Current-user response gains Internal User flag and admin permissions
- [x] Web: admin routes with a UX-only guard and an admin shell; hide the entry for non-internal users

## 5. Audit
- [ ] Extend the audit record for admin actions (actor, action, target, Provider, time; no free text)

## 6. Tests (AGENTS.md sections 3, 4, 5, 8)
- [x] Handler test per policy: internal and permitted, internal and wrong tier, non-internal with permission, anonymous
- [x] Authorization matrix covers every admin route: anonymous 401 or 403, authenticated without permission 403 or 404
- [x] Request-sent Internal User flag (header, cookie, body) is ignored
- [x] Options validation test: empty group name fails in a deployed environment
- [x] Missing portal group data yields a non-internal session
- [ ] Log and stored-error capture contains no request payload markers
- [ ] Audit row written per admin action, with no payload text
- [x] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for the admin shell; run `npm run e2e` and read screenshots
- [x] Regenerate the OpenAPI snapshot deliberately; fix the stale comment in the authorization matrix test

## 7. Verify
- [ ] `dotnet test` from the repo root; `npm test` and `npm run e2e` in `web/`
