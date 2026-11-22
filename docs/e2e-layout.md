# Browser checks (Playwright)

`web/e2e/` holds the browser tests. They check, at eight window sizes (320 to 1920px, plus a phone turned sideways) in light and dark, that:

- the page does not scroll sideways,
- text is not drawn over other text, cut off, or spilling out of its card or box,
- axe finds no WCAG 2.2 AA violation (contrast and target size included).

`layout.spec.ts` runs these on the dashboard, on the four saved views states (list, view, edit, new) and on the claims review page (list, and with claims ticked and the Send to CSE drawer open). `accessibility.spec.ts` and `keyboard.spec.ts` add
headings and landmarks, forced colors, 200% text, 400% zoom, text spacing, reduced motion, Tab order, visible focus, skip links, menus and dialogs.
`saved-views.spec.ts` covers what a user does with saved views: arrange by keyboard and pointer, save, conflict, share, redeem and the refusals.
`needs-attention.spec.ts` covers the claims review: tabs and counts, ticking claims and the count badge on Send to CSE, the drawer, the tab-aware CSV download, the refusal without the claims permission, and the Insights card that links to it.
A new page or state adds itself to those specs in the same change.

Controls under 24px are reported as warnings in the JSON report but do not fail the run.

## Run it

`cd web && npm run e2e`. It starts the app itself if it is not already running, and reuses one that is. Use `npm run e2e:layout` for the layout spec alone.

Output goes to `web/e2e-report/` (not committed): one screenshot and one JSON report per size and scheme. Read the screenshots too. The checks
cannot see text drawn on the chart canvas, or things that are merely ugly.

## Sign-in

By default `GET /api/v1/me` and the Metric, saved views and claims routes are mocked in the browser, so no account, API or portal is needed. To test with a real session,
save a Playwright storage state after signing in and set `PRISM_E2E_STORAGE_STATE=<path>`. Never commit that file: it holds a session cookie.

Set `PRISM_WEB_URL` to test a host other than `http://localhost:4200`.

## Known exclusion

axe's `label-content-name-mismatch` is switched off for the shared library's profile button (`.ems-profile`): the library fixes its accessible name to
"Account: Name", which omits the visible initials. Fix it in shared-ui-component-library and remove the exclusion in `e2e/support.ts`.
