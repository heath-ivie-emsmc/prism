# 10. Customer permissions live in Prism

Status: accepted

## Context

A customer user's permissions come from their portal roles, and the portal cannot be changed. Customers need their own administrator, and staff need to manage customers' access. The portal also returns every company for the internal group, which showed that trusting it for more than identity is fragile.

## Decision

1. **The portal decides who may sign in, which Providers a user belongs to, and staff tiers.** Prism decides what a customer user may do inside a Provider, as one Customer Role per user and Provider. Portal customer roles grant nothing beyond sign-in.
2. **Roles are a fixed catalog in code**, with claim and PHI roles marked restricted.
3. **Permissions are resolved per request from the Provider in the route**, not from the session, so one user can hold different roles in different Providers and a change reaches a live session within a short cache lifetime. A role that cannot be read means no permission.
4. **A Provider admin manages their own Provider's users.** They cannot change their own role, grant above Provider admin, or grant a restricted role unless staff enabled that for the Provider.
5. **Staff with the admin manager tier manage any Provider's roles**, make the first admin and set defaults and the delegation switch. Every change is audited in the same transaction.
6. **Users are recorded at sign-in** so there is a list to manage. A user who has never signed in is not listed until the portal exposes a user list.

## Trade-offs

- Moving permissions out of the session touches every customer route's policy. It is the right place for per-Provider roles, but the authorization and PHI matrices must prove nothing widened.
- Existing customer users lose access until they hold a role. The default role (Viewer) and staff making the first admin soften this. Portal roles are not copied: every customer user gets the login-only `PrismUser` role.
- Not writing to the portal keeps Prism independent of it, at the cost of two places that describe a user's access.

## Consequences

- New tables for Provider users and Provider settings, a new `prism.users.manage` permission, new admin routes, and a customer-facing user management page.
- The portal's customer roles (`ProviderAnalyst`, `ProviderAdmin`, `PlatformSupport`, `SuperUser`, now removed from the catalog) stop granting customer permissions. `AdminPortalSetup` still publishes the sign-in and staff permissions.
