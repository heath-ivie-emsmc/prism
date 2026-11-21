# Browser layout checks (Playwright)

`web/e2e/layout.spec.ts` opens the dashboard at seven window sizes (320 to 1920px) in light and dark, and fails on:

- the page scrolling sideways,
- text drawn over other text,
- text cut off, or spilling out of its card or box,
- an axe WCAG 2.1 AA violation (contrast included).

Controls under 24px are reported as warnings in the JSON report but do not fail the run.

## Run it

1. Start the web app yourself (VS Code **Run all (API + Web)**, or `npm start` in `web/`). The tests never start servers.
2. `cd web && npm run e2e:layout`

Output goes to `web/e2e-report/` (not committed): one screenshot and one JSON report per size and scheme. Read the screenshots too. The checks
cannot see text drawn on the chart canvas, or things that are merely ugly.

## Sign-in

By default `GET /api/v1/me` is mocked in the browser, so no account, API or portal is needed. To test with a real session, save a Playwright
storage state after signing in and set `PRISM_E2E_STORAGE_STATE=<path>`. Never commit that file: it holds a session cookie.

Set `PRISM_WEB_URL` to test a host other than `http://localhost:4200`.

## Known exclusion

axe's `label-content-name-mismatch` is switched off for the shared library's profile button (`.ems-profile`): the library fixes its accessible name to
"Account: Name", which omits the visible initials. Fix it in shared-ui-component-library and remove the exclusion in `e2e/layout.spec.ts`.
