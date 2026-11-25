## 1. Data and docs
- [ ] Use the `emsmc-database-designer` skill for per-user preferences, personal Labels and hidden state on delivered views; CI migration
- [ ] Note the Label glossary amendment for the user; update docs

## 2. API (session user only)
- [ ] Read and update display preferences from a closed key set; validate values; cap sizes
- [ ] Read and update personal Labels; cap length and count; never quote Label text in errors
- [x] Hide and restore delivered views; bounded list
- [x] Reset home page (uses the live-reference behavior from persona-onboarding)
- [ ] Label resolution: personal, Provider, Service Line default
- [ ] Provider-level Labels behind a named policy using the settings-manage permission
- [ ] Reject writes during impersonation

## 3. Exports
- [ ] CSV and Excel headers use Provider-level names through the shared writer

## 4. Web
- [x] `/settings` page: preferences, personal Labels, pushed views with "from EMS" marker, reset home page
- [ ] Apply preferences at load; Label service returns personal Labels on screen

## 5. Tests (AGENTS.md sections 2, 3, 4, 5, 6, 8)
- [ ] Settings routes in the authorization matrix; anonymous denial
- [ ] Cross-user and cross-Provider: another user's settings unreachable; a request naming another user is ignored
- [ ] Default Service Line or Division outside the user's scope is not honored; Facility-restricted user
- [ ] Request-sent owner, Provider or hidden flag ignored
- [ ] Bounds: Label length and count, preference size, unknown key, hidden-list size
- [ ] Label precedence unit tests
- [ ] Label never changes an API key
- [ ] Logs and stored errors contain no Label text markers
- [ ] Export test: headers use Provider-level names; formula payload in a Label, with and without embedded quotes, is neutralized
- [ ] Hide and restore round trip; hidden view is not redelivered
- [ ] Writes rejected during impersonation; impersonating admin sees the target user's Labels
- [ ] Provider-level Label change denied without the settings-manage permission
- [ ] Web: Vitest, axe, Playwright layout, accessibility and keyboard specs for `/settings`; `npm run e2e` and read screenshots
- [ ] Regenerate the OpenAPI snapshot deliberately

## 5. Verify
- [ ] `dotnet test`; `npm test` and `npm run e2e` in `web/`
