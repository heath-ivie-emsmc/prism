# Change: Admin impersonation

## Why
Support and customer success need to see what a specific customer user sees to resolve problems, without asking for
screenshots or sharing credentials. It must be narrow, read-only, time-limited and fully audited, because it is the
most sensitive capability in the admin area.

## What changes
- An authorized Internal User can start an impersonation session for one specific user in one Provider.
- Effective access is the target user's access intersected with a read-only mask. It is never more than the target holds.
- Starting needs the customer data viewer tier, a reason (length capped) and a time limit (about 30 minutes).
- Every request during impersonation is audited with actor, target user and Provider.
- PHI is returned only if the target holds the PHI permission AND the admin holds a separate admin PHI permission;
  otherwise it is masked, with the usual audit for PHI reads.
- The impersonation state is a server-side claim issued only by an admin route. It is never accepted from a header or body.
- The SPA shows a persistent banner. Writes are rejected. Onboarding does not appear or save.
- The admin sees the target user's Labels and preferences.

## Impact
- AGENTS.md security sections touched:
  - 1 PHI: double gate and masking; every PHI read audited; exports follow the same rule.
  - 2 Tenant scope: effective scope is the target's Provider and Facility assignments; a Facility-restricted target restricts the admin; the admin cannot pick a Provider the target lacks.
  - 3 Untrusted input: impersonation claim set by server only; headers and bodies cannot set or extend it.
  - 4 Resource bounds: reason length, session time limit and audit text caps (a concurrent-session limit is an open question).
  - 5 No PHI or raw input in logs: reason and audit text carry no quoted input beyond the capped reason.
  - 7 Authentication and transport: session issuance and expiry, revocation on live sessions, no header changing throttling or auth.
  - 8 Authorization coverage: start and stop routes in the matrix with named policies; every route is read-only under impersonation.
  - Sections 6 and 9 are not touched in new behavior. Exports under impersonation still follow section 6 and the warehouse remains read-only.
- Specs: new capability `admin-impersonation`.
- Docs: ADR 0009 (impersonation part), `docs/auth-setup.md`.
- Tests: write rejection, PHI masking, expiry, audit rows, no header or body override; see tasks.

## Portal dependencies
- (b) A per-Provider user list (portal user id, display name, roles, Facility ids), to choose a target.
- (c) Lookup of any user's roles and permissions and assigned Providers by id, to compute the target's access.
- Both block this change. `internal-admin-access` dependency (a) also applies.

## Out of scope
- Write access of any kind while impersonating.
- Impersonating a user with more access than the admin may see, beyond the intersection rule.
- Impersonating without naming a specific user (role or Provider level).
- Approval workflows or notifying the target user.
- Extending a session beyond the time limit.

## Open questions
- Exact time limit and whether an admin may renew; and whether concurrent sessions are allowed.
- Reason length cap and whether reasons must reference a ticket.
- Name of the separate admin PHI permission and which tier or role holds it.
- Whether the target user is notified or can see that they were impersonated.
- Whether an Internal User may impersonate another Internal User.
- Whether the read-only mask also blocks exports and downloads or only blocks mutations.
- Whether impersonation ends on sign-out only, or also when the admin's portal permissions change.
- Whether audit records are visible to Provider users.
