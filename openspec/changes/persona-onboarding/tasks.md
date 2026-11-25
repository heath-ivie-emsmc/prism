## 1. Data and docs
- [x] Use the `emsmc-database-designer` skill for Personas, questions, options and a per-user profile; CI migration
- [x] Update docs; note glossary additions (Persona) for the user

## 2. Admin management
- [x] Persona, question and option CRUD under the admin route group with the agreed tier; caps on counts and weights
- [x] Each Persona points to a View Template; reject a template that does not exist
- [x] Publish state so unfinished configuration is never shown to users
- [x] Audit every change

## 3. Questionnaire
- [x] Current-user response gains onboarding-required, set by the server
- [x] Fetch published questions; fail open to the Service Line default on none or on error
- [x] Submit answers: validate against published options, score, store Persona and completed timestamp only
- [ ] Never show or save during impersonation (blocked: impersonation is `admin-impersonation`, Phase 5; there is no impersonation session to detect yet)
- [x] Web: onboarding route and guard before the dashboard

## 4. Home page
- [x] Dashboard renders the Persona's current published template through the existing widget renderer; fallback to the existing layout
- [x] Fork on customize into the user's Saved Views as home; reset returns to the live reference

## 5. Tests (AGENTS.md sections 2, 3, 4, 5, 8)
- [x] Scoring unit tests: highest wins, tie by sort order, option weights across Personas
- [x] Fail-open when unconfigured or loading fails
- [ ] Questionnaire absent and save rejected during impersonation
- [x] Raw answers appear in no table, log or stored error
- [x] Request-sent Persona, completed flag or onboarding-required value ignored
- [x] Unknown option id, duplicate answers, oversize body rejected
- [x] Profile read and write only for the session user; cross-user attempt denied
- [x] Home page data respects the user's Provider and Facility scope
- [x] Admin routes in the authorization matrix; non-internal and wrong-tier denial
- [x] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for onboarding and home; `npm run e2e` and read screenshots
- [x] Regenerate the OpenAPI snapshot deliberately

## 6. Verify
- [ ] `dotnet test`; `npm test` and `npm run e2e` in `web/`
