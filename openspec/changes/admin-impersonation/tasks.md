## 1. Decisions and docs
- [ ] ADR 0009: impersonation part; update `docs/auth-setup.md`
- [ ] Confirm portal dependencies (b) and (c) before starting

## 2. Session
- [ ] Start route: customer data viewer tier, target user and Provider, capped reason, time limit
- [ ] Compute effective access: target access intersected with a read-only mask; fail closed on lookup failure
- [ ] Impersonation claim issued server-side only; stripped from every request
- [ ] Expiry checked on every request; stop route ends the session
- [ ] Central rejection of every mutating request while impersonating

## 3. PHI
- [ ] Separate admin PHI permission; PHI shown only when target holds PHI and admin holds it; masking otherwise
- [ ] PHI reads write an audit record naming actor and target

## 4. Audit
- [ ] Per-request audit: actor, target, Provider, action code; no bodies or values
- [ ] Session start and end audit with the reason

## 5. SPA
- [ ] Persistent impersonation banner with remaining time and a stop control
- [ ] Questionnaire hidden and not saved; target user's Labels and preferences shown

## 6. Tests (AGENTS.md sections 1, 2, 3, 4, 5, 7, 8)
- [ ] Start and stop routes in the authorization matrix; non-internal, wrong tier, anonymous denial
- [ ] Write rejection on a representative mutating route and on the central rule for a new route
- [ ] PHI matrix: with both permissions fields present and audit row written; with only one permission no PHI substring in the JSON
- [ ] Cross-Provider: target not assigned to Provider B cannot be viewed in B; Facility-restricted target versus a sibling Facility
- [ ] Expiry: request after the time limit is rejected
- [ ] Audit rows exist per request, with actor, target and Provider
- [ ] No override: impersonation header, cookie or body ignored; spoofed forwarded header changes nothing
- [ ] Reason over the cap rejected; reason absent rejected; reason text not in logs beyond the capped audit field
- [ ] Revoked admin session ends impersonation
- [ ] Questionnaire absent and settings writes rejected during impersonation
- [ ] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for the banner and target picker; `npm run e2e` and read screenshots
- [ ] Regenerate the OpenAPI snapshot deliberately; extend `PhiMatrixTests` for any PHI route

## 7. Verify
- [ ] `dotnet test`; `npm test` and `npm run e2e` in `web/`; manual check as a Facility-restricted target
