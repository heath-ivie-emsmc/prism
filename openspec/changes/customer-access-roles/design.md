# Design: Customer roles

## Where permissions live
- **Portal:** login (`prism.app`), assigned Providers with Facility limits, the internal group and staff tiers. Unchanged.
- **Prism:** what a customer user may do in a Provider, as one Customer Role per user and Provider.
- Rejected: write-back to the portal (no write API, and the portal cannot be changed); Prism replacing the Provider list (the portal stays the source of which Providers a user belongs to); individual permissions per user (harder to review and keep consistent).

## Roles are a code catalog
| Role | Permissions | Restricted |
| --- | --- | --- |
| Viewer | dashboard view | no |
| Analyst | Viewer + saved views manage | no |
| Claims reviewer | Analyst + claims view | yes |
| PHI reviewer | Claims reviewer + PHI view | yes |
| Provider admin | Analyst + settings manage + users manage | no |

Restricted roles are the ones that carry claim or PHI access. The catalog is defined next to `Permissions.cs`, so a role cannot be invented at runtime and the matrix and PHI tests can enumerate it.

## Per-request, per-Provider evaluation
- The session claim keeps login and staff tiers only. A customer permission policy reads the Provider from the route (`providerId`), checks it against the user's assigned Providers as today, then loads the user's role in that Provider and checks the permission.
- Routes with no Provider (`/me`, admin) do not use customer permissions. `/me` returns each Provider with the permissions the user holds in it, for the SPA to show or hide.
- Roles are cached briefly per user and Provider with a size cap and a short lifetime, and the cache entry is dropped when a role changes, so a change reaches a live session within the lifetime. A role that cannot be read means no permission (fail closed).
- Rejected: putting roles in the session cookie (cannot be per Provider, and a change would wait for the session to end).

## Recording users
- At sign-in, for each Provider the user is assigned (capped), upsert a Provider user row: portal user id, Provider, display name, last sign-in. A first row gets the Provider's default role.
- Internal Users are not recorded as customer users.
- A user's display name is the portal's. It is bound as text only.

## Delegation
- A Provider admin may assign any non-restricted role to users in their own Provider, never to themself, and never a role above Provider admin.
- A restricted role needs staff, or the Provider's `allow_admin_restricted_grants` setting on (set only by staff).
- The last Provider admin of a Provider cannot be demoted by a Provider admin; staff can.
- Staff (admin manager tier) can set any role in any Provider they name, make the first admin, set the default role and the delegation switch.

## Launch
- Every Provider starts with default role Viewer and delegation off. Staff make the first Provider admin per Provider. Until then no customer user can change roles.
- Existing users' portal roles are not copied. The old customer roles are removed from the catalog; every customer user holds the login-only `PrismUser` role and gets the Provider's default role.

## Audit
- Each change writes an admin audit entry in the same transaction: actor, action, Provider, target user id, old and new role keys. No names, no free text.

## Risks
- Every customer route changes its policy, so the authorization matrix and PHI matrix must prove nothing was widened. This is the largest risk and the main test effort.
- A Provider admin gaining power by editing the people around them: mitigated by no self-change, no restricted roles without staff, and audit.
- Users who have never signed in are invisible until the portal exposes a user list.
