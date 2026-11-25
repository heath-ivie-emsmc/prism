# Design: internal admin access

## Gate = group AND tier
- Entry to any admin route requires the Internal User claim AND the tier permission for that route. Either alone
  is denied. This keeps "who is an employee" (portal group) separate from "what may they do" (portal role), so a
  customer who somehow holds an admin-looking permission is still denied, and an employee with no tier sees nothing.
- Rejected: group only (any employee could do anything), permission only (a mis-assigned customer role would open
  the admin area), a Prism-side list of employees (a second source of truth that drifts from the portal).

## Tiers are independent permissions
- Template author, template publisher, customer data viewer, admin manager are four separate permissions. Portal
  roles bundle them; Prism never infers one from another.
- Rejected: a ladder where higher tiers include lower ones. It hides what a role really grants and is hard to audit.

## Group name is one configuration value
- A single per-environment value names the group. The base configuration leaves it empty so a host with no
  environment file fails options validation instead of treating everyone as internal or nobody as internal silently.
- Rejected: hard-coding the name; matching on a prefix or display name (AGENTS.md rule 2 spirit: authorize on
  identity values, not look-alike strings).

## Fail closed
- If the portal response lacks group data, or the configured name does not match exactly, the user is not an
  Internal User. Admin routes return 403 to authenticated non-internal users, never 404-shaped hints beyond what the
  authorization matrix expects.
- The claim is issued only when the session is issued. A request header, cookie value or body field named like the
  flag is ignored or rejected (rule 3).

## Separate admin route group
- Admin routes live under their own group with their own policies, so the authorization matrix test discovers them
  and a missing policy fails the build. Customer routes keep Provider-in-route scoping; admin routes that touch a
  Provider take it from the route and never from a body.
- Rejected: reusing customer endpoints with an "if internal" branch (widens a customer route by accident).

## Audit
- Every admin action records actor, action code, target type and id, Provider where relevant, and time. No free-text
  payload echo (rule 5). Audit reads are themselves an admin action.

## Risks
- Portal dependency (a) is not yet confirmed; the work is blocked until it exists.
- Stale group membership on long sessions; mitigated by the existing scope cache expiry and session revocation, but
  the exact lag is an open question.
- Tier sprawl: four permissions may not match how the business wants to staff roles; kept independent so roles can
  be reshaped in the portal without code changes.
