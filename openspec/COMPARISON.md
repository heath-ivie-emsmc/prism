# OpenSpec comparison map

Maps each capability to its source documents and test areas, for gap analysis and for assembling the PRD. The
specs were drafted by reading code, docs and test names; test bodies were not all read and no tests were run to
produce them. Coverage marks are therefore blank until someone counts each spec's `#### Scenario:` lines against
the tests.

How to fill the Coverage column: count the scenarios in the spec, match each to a test, mark **covered**,
**partial** or **gap**.

## Baseline (implemented)

| Spec | Requirements / scenarios / open questions | Sources | Test areas | Coverage |
|------|------------------------------------------|---------|------------|----------|
| `auth-and-sessions` | 17 / 77 / 13 | `docs/auth-setup.md`, ADR 0002, AGENTS.md sections 3, 7, 8 | `tests/Prism.Tests.Integration/Auth`, `tests/Prism.Tests/Auth`, Provider and Facility scope tests in `tests/Prism.Tests.Integration/Metrics` | |
| `metric-catalog` | 19 / 58 / 8 | `docs/metrics.md`, `docs/metric-design-guide.md`, `docs/metric-inventory.md`, ADRs 0006, 0007 | `tests/Prism.Tests/Metrics`, `tests/Prism.Tests.Integration/Metrics` | |
| `saved-views` | 14 / 46 / 7 | ADR 0004, `docs/saved-views.md`, `docs/saved-views-ui-spec.md` | `tests/Prism.Tests/SavedViews`, `tests/Prism.Tests.Integration/SavedViews`, `web/e2e/saved-views.spec.ts` | |
| `claims-attention` | 13 / 41 / 7 | ADR 0005, AGENTS.md sections 1, 2, 5, 6 | `tests/Prism.Tests/Attention`, `tests/Prism.Tests.Integration/Attention`, PHI matrix in `tests/Prism.Tests.Integration/Auth` | |
| `warehouse-access` | 13 / 56 / 7 | ADR 0008, `docs/warehouse.md`, AGENTS.md sections 4, 9 | `tests/Prism.Tests/Architecture`, `tests/Prism.Tests/Warehouse`, `tests/Prism.Tests.Integration/Warehouse` | |

## Proposed (changes, not yet built)

| Change | Requirements / scenarios | Main open questions |
|--------|--------------------------|---------------------|
| `internal-admin-access` | 7 / 20 | Group data source (`user-data` or `rbac-data`), live-session effect of group changes, portal permission names |
| `view-templates` | 8 / 30 | Which tier assigns and revokes, draft and publish lifecycle, redelivery after a user hides a copy |
| `persona-onboarding` | 9 / 25 | Which tier manages Personas, Service Line default for users with several, fallback when a template is unpublished |
| `user-settings` | 9 / 27 | Which terms are renamable, Label limits, date range choices |
| `admin-impersonation` | 8 / 26 | Time limit and renewal, admin PHI permission name, impersonating Internal Users, exports under the read-only mask |

## Findings worth product attention

- Facility-restricted users get 404 on every Provider-level route and on the claims routes.
- Without the claims permission the claims routes refuse with 403; they do not return masked rows.
- Several documented gaps remain open: roles stay in the session for up to 8 hours, there is no revoke-all, rate limits
  are per instance, the logout route is a GET, and the same-login-time rule has nothing behind it because there is no
  password login.
- Eleven Metrics are only planned (`docs/metrics.md`); their definitions are open questions in `metric-catalog`.
- The authorization matrix test carries a stale comment ("no route names a policy"), and some docs refer to `/api/me`
  instead of `/api/v1/me`.
