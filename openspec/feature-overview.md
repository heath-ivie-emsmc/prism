# Feature overview: admin area, View Templates, personas and user settings

Starting point for the PRD. Detail lives in the five change folders under `changes/`; behavior of what exists today is in `specs/`. Decision record: `docs/adr/0009-internal-access-and-impersonation.md`.

## Problem

Customers see one hard-coded home page, and every user starts from nothing when they build Saved Views. Internal staff cannot help: there is no admin area, no way to give customers a good starting layout, no way to see what a customer user sees, and the dashboard does not fit the person using it. An air chief and an accounting clerk care about different Metrics.

## What we are building

1. **Internal admin area** (`internal-admin-access`). Reachable only by Internal Users: members of one portal group who also hold a tier permission (template author, template publisher, customer data viewer, admin manager). All actions are audited.
2. **View Templates and Assignments** (`view-templates`). Internal staff build layouts and push them to all Providers, chosen Providers, or a Persona. A confirmation shows how many Providers and widgets are affected. Each user gets a clone in their own Saved Views on their next sign-in. Revoking stops future deliveries and leaves delivered copies alone. Widgets that do not apply to a Provider show an empty state.
3. **Personas and onboarding** (`persona-onboarding`). Admin-managed Personas and a two-to-three question questionnaire, mandatory at first login. The answer picks a Persona once per user. The home page is a live reference to the Persona's View Template; customizing it creates the user's own copy, and reset returns to the live one. If nothing is configured, the questionnaire is skipped.
4. **User settings** (`user-settings`). Display preferences, personal Labels (personal, then Provider, then Service Line default), hiding and restoring pushed views, retaking the questionnaire, and resetting the home page. Exports use the Provider's names.
5. **Impersonation** (`admin-impersonation`). Customer data viewers can see the dashboards as one specific user, read-only, with a reason and a time limit. PHI needs both the user's own permission and a separate admin one.

## Who it is for

- Customer users: a home page and starting views that fit their job, and control over their own display.
- Internal staff: tools to author and push layouts and to support customers.
- Product: a way to tune defaults by Persona without a deploy.

## Dependencies and risks

- Portal team: group membership at sign-in, a per-Provider user list (id, name, roles, Facility ids), and a lookup of any user's access by id. Phases 1 and 5 depend on these.
- Impersonation is the highest-risk piece; see the ADR.
- Personal Labels extend a term that was Provider-level, so shared screens and exports need the precedence rules above.

## Open questions

Each change folder lists its own. The main ones: which tier assigns and revokes, how a Persona behaves when its template is unpublished, Label limits, the impersonation time limit, and the admin PHI permission name.

## Phasing

0. Specs and glossary (done).
1. Admin access, group claim, tier policies, admin shell, audit.
2. View Templates, Assignments and delivery.
3. Personas, questionnaire, persona-driven home page.
4. User settings.
5. Impersonation.

## Added later: customer roles (`customer-access-roles`)
Customer permissions move from the portal into Prism as a fixed catalog of Customer Roles, held per user and Provider. A Provider admin manages their own Provider's users, with claim and PHI roles reserved for staff unless staff enable delegation. Staff manage any Provider's roles, make the first admin and set the default role. Users are recorded at sign-in so there is a list to manage. Depends on no new portal work.
