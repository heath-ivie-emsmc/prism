# 9. Internal access, View Template push and impersonation

Status: accepted (access, View Templates and preview); impersonation is still proposed

## Context

Prism is a customer-facing dashboard, and every rule so far says a user reaches only the Providers the portal assigned them. Internal staff now need to author View Templates, push them to customers, and see what a customer user sees to support them. That is a deliberate exception to "tenant comes from the user's assignments", so it needs its own boundary rather than a widened role.

## Decision

1. **Two checks, both required.** An Internal User holds the `InternalUser` marker role (permission `prism.internal`). The portal exposes no group names, so a portal admin attaches that role to the internal group; an optional `Internal:GroupName` is also honoured if the portal ever reports groups. A separate portal permission sets the tier: template author, template publisher, customer data viewer, admin manager. The group without a tier, or a tier without the group, grants nothing. The marker is resolved at sign-in into a server-set claim and fails closed if the portal data is missing. A request can never set it.
2. **A separate route group.** Admin routes live under `/api/v1/admin` with their own named policies. They are not mixed into the Provider routes, and the endpoint authorization matrix covers them automatically.
3. **View Templates are pushed as clones, delivered lazily.** An Assignment records the intent; each user's clone is created the first time they sign in after it, once per Assignment, user and Provider. Prism never writes into the account of someone who has not signed in. A Persona's home page is the exception: it is a live reference to the Persona's published View Template until the user customizes it.
4. **Impersonation is user-specific and read-only.** The admin acts with the target user's access intersected with a read-only mask, never more. It needs the customer data viewer tier, a reason and a time limit, is audited on every request, and is signalled in the SPA. PHI is visible only if the target user holds the PHI permission and the admin holds a separate admin PHI permission. The impersonation state is issued by the server and never accepted from a header or body.
5. **Preview Provider, to lay out a View Template.** A template has no Provider, so an author needs figures from one to arrange widgets. An Internal User with the customer data viewer tier (and the group) may read any Provider's aggregate Metric routes by its number. It is whole-Provider, never a Facility. It is audited once per session and Provider before any figure is read, and the read is refused if the audit cannot be written. It applies only to the aggregate Metric routes: claim-level routes and Saved View routes keep the user's own assignments, so a person who is both a customer user and an Internal User still cannot read another Provider's claims or views. The customer data viewer role carries the dashboard permission and never the claims or PHI permission.
6. **An Internal User has no customer Providers.** The portal's `assigned-companies` is the union of direct assignments and group memberships, and the internal group carries every company. Prism does not read it for a session with the internal claim: `/me` lists no Providers and every Provider route returns 404, so staff reach customer figures only through the audited preview (decision 5). This applies to any member of the group, with or without a tier, so a group member with no tier is not handed every company. The cost is that a person who is both staff and a real customer user loses their customer access in Prism; the portal cannot tell group-granted companies from direct ones, and it cannot be changed.
7. **Every admin action is audited**, with actor, target Provider or user, and the object changed.

## Trade-offs

- Impersonation was chosen over a push-only admin model because a Provider's users have different roles and Facility scopes, and support needs to reproduce one person's view. It is the largest risk in this change. The mitigations are the read-only mask, the double PHI gate, the reason and time limit, and the audit trail.
- Lazy delivery means a user who never signs in never gets a template. That is acceptable, and it keeps persona targeting possible, since Prism learns a Persona only when the user answers.
- Group membership is checked at sign-in, so removing someone from the group reaches a live session only when it ends (up to 8 hours) unless a revoke action is added.

## Consequences

- This change depends on the portal: group membership at sign-in, a per-Provider user list, and a lookup of any user's access by id. Until those exist, only part of it can be built.
- New permissions are published to the portal through `AdminPortalSetup`.
- The authorization matrix, PHI matrix and cross-Provider tests must cover the admin routes, and a test must prove a request cannot set the internal claim or impersonation state.
