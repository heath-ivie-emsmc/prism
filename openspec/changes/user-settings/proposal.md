# Change: Customer user settings

## Why
Customer users cannot change how Prism looks or reads for them. Theme and defaults are not remembered, a Provider's
Labels are the same for every user, and a view pushed by EMS cannot be hidden. A `/settings` page, kept separate from
internal administration, gives each user control of their own experience.

## What changes
- A settings page for the signed-in customer user with:
  - display preferences: theme, default Service Line, default Reporting Division, default date range
  - personal Labels (renaming only)
  - pushed views: hide and restore, with a "from EMS" marker
  - reset of the home page to the live reference (from `persona-onboarding`)
  - a questionnaire retake option, if the open question below is decided yes
- Label precedence: personal, then Provider, then Service Line default. A Label never changes an API key.
- On screen each viewer sees their own Labels. CSV and Excel exports use the Provider-level name.
- Provider-level Labels are set by users who hold the settings-manage permission.
- During impersonation the admin sees the target user's Labels and preferences, read-only.
- Settings endpoints are keyed by the session user only.

## Impact
- AGENTS.md security sections touched:
  - 2 Tenant scope: preferences and Labels are per user and never widen Provider or Facility access; default Reporting Division and Service Line are checked against the user's scope, not trusted from the body.
  - 3 Untrusted input: owner, Provider and visibility flags are server-set; request copies are ignored.
  - 4 Resource bounds: Label length and count, preference value size and allowed keys are capped; hidden-view list is bounded.
  - 5 No raw input in logs or stored errors: Label text is never quoted in an error or log.
  - 6 Exports: header cells in CSV and Excel use Provider-level names and go through the shared writer's quoting and formula neutralizing, including Label text.
  - 8 Authorization coverage: user routes appear in the matrix; Provider-level Labels use a named policy.
  - Sections 1, 7 and 9 are not touched. Labels are display names, not PHI.
- Specs: new capability `user-settings`; amends the Label behavior.
- Data: per-user profile fields (preferences, personal Labels) and hidden state for delivered views (database designer skill, CI migration).
- Docs: glossary amendment of Label (separate step for the user).
- Tests: see tasks.

## Portal dependencies
- None new. Provider-level Labels use the existing settings-manage permission.

## Out of scope
- Internal administration and View Template authoring.
- Saved View sharing (already exists).
- Changing a Label's API key, or renaming terms not on the renamable list.
- Notification and email preferences.
- Persona name display.

## Open questions
- Exact list of renamable terms and whether every one is personally renamable.
- Maximum Label length, and the allowed character set.
- Whether a personal Label is validated against reserved or misleading words.
- Whether the "retake questionnaire" option exists in v1 (the plan says the change option is optional).
- Whether default Reporting Division and default Service Line are validated at save or at use when the user's scope later changes.
- Default date range choices.
- Whether hiding a pushed view also hides it from sharing or only from the user's own list.
- Whether Excel and CSV exports of Provider-level names apply to every export or only to Attention Check exports initially.
