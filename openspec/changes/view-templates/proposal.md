# Change: View Templates and Assignments

## Why
Internal staff want to give customers a good starting set of dashboards instead of every user building from a blank
Saved View. Today Saved Views are per-user layouts and nothing can be pushed to a customer. A View Template and an
Assignment give staff a controlled way to author a layout once and deliver it to Providers or to a Persona.

## What changes
- A **View Template** is an internal layout: Metric keys, presentations and grid positions. It has no owner, no
  Provider and no figures, and is validated against the Metric Catalog exactly as a Saved View layout is.
- An **Assignment** pushes a View Template to all Providers, to specific Providers, or to a Persona.
- Every push shows a confirmation screen first with counts: Providers, widgets that apply per Provider, and the
  template version. Every push and revoke is audited. There is no second approver.
- Delivery is lazy: on a user's next sign-in the template is cloned into that user's Saved Views for the Provider.
  Delivery is idempotent and bounded.
- Widgets whose Metric does not apply to the viewer's Provider or scope are still delivered and show an empty state.
  The pre-push report is informational and never blocks a push.
- Revoking an Assignment stops future deliveries. Copies already delivered belong to the user and stay.
- Aggregate delivery reports for staff (counts only, no user or patient detail).
- Delivered views carry a "from EMS" marker (the user-side hide and restore is in `user-settings`).

## Impact
- AGENTS.md security sections touched:
  - 2 Tenant scope: delivery is scoped to the Provider in the route and the user's assignments; a customer cannot see another Provider's delivered views; a Facility-restricted user is checked against a sibling Facility.
  - 3 Untrusted input: Provider, owner and source template on a delivered view are set by the server; request values for them are ignored.
  - 4 Resource bounds: template widget count and layout size, Assignment target list size, deliveries per sign-in, report page size.
  - 5 No raw input in logs or stored errors: layout validation failures report codes, not submitted text.
  - 8 Authorization coverage: new admin routes use the tier policies from `internal-admin-access`; the matrix test covers them.
  - Sections 1, 6, 7 and 9 are not touched. Templates and reports carry no PHI, and the warehouse is untouched.
- Specs: new capability `view-templates`.
- Data: new Prism-owned durable state for templates, assignments, deliveries; a source-template reference on Saved Views
  (via the database designer skill and a CI migration; not described here).
- Docs: `docs/saved-views.md`; amend the Saved View entry in the glossary (remove "template" from Avoid) in a separate step.
- Tests: see tasks.

## Portal dependencies
- None beyond `internal-admin-access`. The "to a Persona" target depends on `persona-onboarding` and is inert until Personas exist.

## Out of scope
- Personas, the questionnaire and the persona-driven home page (`persona-onboarding`).
- Hide and restore of delivered views by the user (`user-settings`).
- Pushing figures, filters, Labels or any per-user data inside a template.
- A second approver, scheduled pushes, or A/B delivery.
- Updating delivered copies when the template later changes.

## Open questions
- Which tier may create and revoke Assignments: template publisher only, or publisher and author.
- Whether a template has a draft state and an explicit publish step, and how "template version" is numbered; the
  plan names a version in the confirmation counts and the home page uses the "current published" template.
- Numeric bounds: widgets per template, deliveries per sign-in, targets per Assignment.
- Whether a user who already received a template and then hides it receives it again on a later new Assignment of the same template.
- Whether delivery to a user covers every Provider they belong to at once or only the Provider active at sign-in.
- Whether template deletion is allowed while Assignments exist.
