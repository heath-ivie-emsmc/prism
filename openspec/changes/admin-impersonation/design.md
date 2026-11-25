# Design: admin impersonation

## User-specific, not role-level
- The admin impersonates one named user in one Provider. The effective permissions are what that user holds, which
  makes the view reproduce exactly what the customer sees.
- Rejected: a generic "view as Provider" mode (would show access no one holds and hide permission problems);
  copying the target's session cookie (no audit separation, no expiry control).

## Intersection with a read-only mask
- Effective access = target access AND read-only mask. The result can never exceed what the target holds, and can
  never write. Rejected: union with the admin's access (privilege escalation).
- The target's Provider and Facility assignments apply in full, so a Facility-restricted target restricts the admin to
  that Facility.

## Server-issued claim, no override
- The impersonation state is a server-side claim issued only by the admin start route and bound to the admin's
  session. It is stripped from any request; a header, cookie value or body field cannot set, extend or end it in a
  way the server does not own. This is rule 3 and rule 7: no request header changes auth in deployed environments.
- Stopping and expiry are server-driven. The SPA banner reflects the claim; it does not enforce it.

## Reason and time limit
- Starting requires a reason (length capped) and sets an expiry of about 30 minutes. The reason is stored in the audit
  record for the session, not repeated on every request line. Expiry is checked on every request, not only at issue.
- Rejected: unlimited sessions; free-text reasons without a cap (log and storage risk).

## Audit every request
- Each request records actor, target user, Provider, action code and time. It records codes and paths, never bodies,
  query values or PHI (rule 5).

## PHI double gate
- PHI fields appear only if the target holds the PHI permission and the admin holds a separate admin PHI permission.
  Otherwise the existing masking applies. PHI reads write the audit record required by rule 1, with the actor being the admin.
- Rejected: admin PHI permission alone (could show PHI the customer cannot see); target PHI permission alone (every
  support person would see PHI).

## Read-only enforcement
- A single server check rejects every mutating request during impersonation, including settings and onboarding
  saves, regardless of route. This is a central rule, not per-endpoint, so a new endpoint is safe by default.
- Rejected: per-endpoint opt-out (easy to forget).

## Risks
- Portal dependencies (b) and (c) must return accurate access or the intersection is wrong; fail closed on any lookup error.
- Impersonation of a user whose access changes mid-session; the scope cache expiry bounds staleness, an open question.
- Support staff may be tempted to share screenshots containing masked-but-sensitive data; policy, not code.
