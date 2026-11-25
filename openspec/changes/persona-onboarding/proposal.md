# Change: Personas, first-login questionnaire and persona-driven home page

## Why
The home page is one hard-coded layout for everyone. Customers differ in what they care about, and staff want new
users to land on a useful page without building one. A short mandatory questionnaire selects a **Persona**, and the
Persona points at a View Template that becomes the user's home page.

## What changes
- Staff manage **Personas**, questions (2 to 3), options and scoring weights. Each Persona points to a View Template.
- At first login every customer user answers the questionnaire. The highest total weight wins; ties go to the lower
  sort order.
- The questionnaire is mandatory but fails open: with no questions or Personas published, or if loading fails, the
  user gets the Service Line default home page and is not blocked.
- It never appears, and never saves, during impersonation.
- Raw answers are not stored. Only the resulting Persona and a completed timestamp are kept.
- One Persona per user (by portal user id), not per Provider. The Persona name is not shown on pages.
- The home page is a live reference to the Persona's current published View Template. Customizing forks it into the
  user's own Saved Views as home; "reset" returns to the live reference.
- The current-user response says whether onboarding is required.

## Impact
- AGENTS.md security sections touched:
  - 2 Tenant scope: the home page content is shown only through the user's own Provider and scope; the profile is keyed by the signed-in user, never by a name or payload value.
  - 3 Untrusted input: the Persona, completed state and onboarding-required flag are set by server code; submitted copies are ignored. Answers only select options that exist.
  - 4 Resource bounds: question, option and Persona counts, weight ranges, answer count and body size are capped.
  - 5 No raw input in logs or stored errors: answers are not logged or stored.
  - 8 Authorization coverage: admin routes use tier policies; user routes use the session user only and appear in the matrix.
  - Sections 1, 6, 7 and 9 are not touched.
- Specs: new capability `persona-onboarding`.
- Data: Persona, question and option configuration and a per-user profile (database designer skill, CI migration).
- Tests: scoring, fail-open, impersonation suppression, bounds, denial; see tasks.

## Portal dependencies
- `internal-admin-access` (group and tiers) for authoring routes.
- `admin-impersonation` for the suppression behavior to be exercised end to end.
- Persona-based Assignments in `view-templates`.

## Out of scope
- Showing the Persona name on pages.
- Storing or analyzing raw answers.
- Per-Provider Personas.
- Template authoring and push (`view-templates`).
- Display preferences and personal Labels (`user-settings`).

## Decisions taken while building
- Tier: authoring (Personas, questions, options, weights) needs the template author tier; publishing needs the template publisher tier. No new tier.
- A published Persona needs a published View Template. Unpublishing the Persona, or its template, sends the users who have it back to the Service Line default; nothing is deleted.
- The questionnaire is usable only while two or three questions are published (a fourth cannot be published), each with at least two options, and at least one published Persona points at a published template. Otherwise nobody is asked.
- Answering again replaces the Persona. A forked home page is kept. The settings page that offers a retake is in `user-settings`.
- A Persona cannot be deleted while a user has it or an option weighs it. Unpublish it instead.
- Bounds: at most 20 Personas, 10 questions (3 published), 2 to 8 options per question, weights 0 to 100, sort order 0 to 1000, request body 16 KB.
- The home page and its copy are per Provider (a Saved View belongs to one Provider); the Persona is per user. Deleting the copy as a Saved View is the same as a reset.

## Open questions
- How the Service Line default is chosen for a user with several Service Lines (today the standard dashboard decides).
- Whether staff need an aggregate count of how many users resolved to each Persona (out of scope for now).
- Impersonation suppression is specified but cannot be built until `admin-impersonation` exists.
