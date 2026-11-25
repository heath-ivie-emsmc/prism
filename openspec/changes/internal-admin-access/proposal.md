# Change: Internal admin access (group gate, tier permissions, admin route group, audit)

## Why
Prism has no Internal User concept. There is no admin area, no way to tell an employee from a customer, and no
place to author View Templates, manage Personas or support customers. Every later admin capability needs one
trustworthy gate first, so it is specified once here and reused by `view-templates`, `persona-onboarding` and
`admin-impersonation`.

## What changes
- An **Internal User** is a signed-in user who belongs to the one portal group named in a single per-environment
  configuration value. Membership is read from the portal at sign-in and recorded in the session by server code only.
- Group and role are independent. The group gates entry to the admin area; a portal role or permission sets the
  tier. Both must pass. Four independent tier permissions, combined by portal roles: template author, template
  publisher, customer data viewer, admin manager.
- A separate admin route group, authenticated by default, with its own named policies that require the Internal User
  claim and the tier permission.
- The current-user response tells the SPA whether the user is an Internal User and which admin permissions they hold,
  so the SPA can show or hide the admin area. The SPA guard is UX only; the server is the gate.
- Every admin action writes an audit record with the actor, the action and the Provider where one applies.
- A new architecture decision record (ADR 0009) covers internal access and impersonation.

## Impact
- AGENTS.md security sections touched:
  - 3 Untrusted input must not set trusted state: the Internal User claim and admin permissions are set by server code only; client-sent copies are stripped or rejected.
  - 4 Resource bounds: admin lists are paged and capped; audit text lengths are capped.
  - 5 No PHI or raw input in logs: audit and error text carry codes, ids and lengths only.
  - 8 Authorization coverage: new route group in the authorization matrix; named policies with handler tests.
  - 2 Tenant scope is touched only where an admin route takes a Provider (the customer data viewer tier); it is checked as in 8 and never inferred from a name.
  - Sections 1, 6, 7 and 9 are not touched by this change (7 applies to impersonation in its own change).
- Specs: new capability `internal-admin-access`.
- Docs: `docs/auth-setup.md`, new ADR 0009.
- Tests: handler tests per policy; authorization matrix covers the admin routes; non-internal, wrong-tier and
  anonymous denial; request-sent flag ignored; configuration validation.

## Portal dependencies
- (a) The portal must expose the user's group membership at sign-in. Blocks this change.
- Portal roles or permissions for the four tiers must be published in the portal. The exact permission names are an
  open question.

## Out of scope
- Authoring View Templates, Personas and questionnaires (`view-templates`, `persona-onboarding`).
- Impersonation (`admin-impersonation`).
- Any customer-facing settings (`user-settings`).
- SAML or other federation.
- A second approver for admin actions.

## Open questions
- Exact value of the group name per deployed environment (the plan uses "EMSbilling.com Users" as the single group).
- Whether group membership is delivered in the user-data or the rbac-data portal response (portal team decides).
- Whether a group change in the portal takes effect on live sessions at once or only at next sign-in or the next
  scope refresh.
- Exact portal permission names and which default portal roles bundle which tiers.
- Which tier may create and revoke Assignments (author, publisher or both); settled in `view-templates`.
- Retention period for admin audit records.
