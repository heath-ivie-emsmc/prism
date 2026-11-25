# Change: Customer roles, managed in Prism

## Why
Today a customer user's permissions come from their portal roles, and the portal cannot be changed. Customers need their own
administrator who can decide what each colleague may do, and EMS staff need to do the same for customers who have none yet.
The portal keeps what it does well: login rights, which Providers a user belongs to, and staff access. Prism owns what a
customer user may do inside a Provider.

## What changes
- **Customer Roles** are a fixed catalog in Prism: Viewer, Analyst, Claims reviewer, PHI reviewer and Provider admin, each a set of permissions.
- A user holds **one role per Provider**, stored in Prism. The portal's customer roles no longer grant anything beyond sign-in (`prism.app`); the Provider list and Facility limits still come from the portal.
- The signed-in session carries login and staff tiers only. A customer permission is worked out per request from the Provider in the route, so one user can be an admin in one Provider and a viewer in another.
- **Provider admin** (new permission `prism.users.manage`) can list the users of their own Provider and change their roles.
- A Provider admin can grant only ordinary roles. Roles with claim or PHI access (**restricted roles**) are granted by EMS staff, or by a Provider admin once staff enable that for the Provider.
- **EMS staff** with the admin manager tier manage the roles of any Provider's users in the admin area, make the first Provider admin, set each Provider's default role, and switch restricted-role delegation on or off.
- Prism **records each user at sign-in** (portal user id, display name, Provider) so there is a list to manage. A user who has never signed in is not listed.
- A new user gets the Provider's **default role** (Viewer unless staff change it) the first time they sign in.
- Every role change is audited with who, which user, which Provider, and the old and new role.

## Impact
- AGENTS.md security sections touched:
  - 1 PHI: PHI and claim access become role-gated per Provider; nobody gets them by default.
  - 2 Tenant scope: a role applies only inside its Provider; a Provider admin sees and changes only their own Provider's users; staff routes name the Provider explicitly and never infer it.
  - 3 Untrusted input: the role, the acting user and the Provider are set by server code; a role or permission sent in a body for another user or Provider is rejected or ignored; a user cannot change their own role.
  - 4 Resource bounds: users listed per page, role changes per request, audit text, cache size and age.
  - 5 No PHI or raw input in logs: display names are not logged with role changes.
  - 8 Authorization coverage: new policies per Provider route (`prism.users.manage`, staff tier); all new routes appear in the matrix; a customer-permission policy is added that reads the Provider from the route.
  - Sections 6, 7 and 9 are not touched in new behavior.
- Specs: new capability `customer-access-roles`; amends `auth-and-sessions` (the permission claim narrows to login and staff tiers).
- Data: Provider user and Provider setting tables (database designer skill, CI migration).
- **Breaking behavior:** every existing customer user would lose access until they have a role. The launch path is the default role plus staff making the first admin; see design.
- Docs: new ADR 0010, `docs/auth-setup.md`, glossary entry **Customer Role**.

## Portal dependencies
- None new for this change. It uses what the portal already returns (login, assigned Providers and Facilities, the internal group).
- A per-Provider user list from the portal would let staff see users who have never signed in; until then only users who have signed in are listed.

## Out of scope
- Writing anything to the portal.
- Per-Facility roles (Facility limits stay as the portal sets them).
- Custom roles defined by customers or staff (the catalog is fixed in code).
- Removing a user from a Provider (that stays a portal assignment).

## Open questions
- Exact catalog contents and names, and whether Provider admin should include claims by default.
- Whether a Provider admin may see users' last sign-in time.
- Whether staff need a bulk "set role for all users in a Provider" action for launch.
- Cache lifetime for per-Provider roles and how a change reaches a live session.
