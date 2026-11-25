# Design: customer user settings

## Separate from internal administration
- Settings are for customer users about themselves. They live under a user route keyed by the session user and need
  no admin permission. Provider-wide settings (Provider-level Labels) keep the existing settings-manage permission.
- Rejected: one combined admin and settings area (blurs who may change what; the user asked for separation).

## Session user only
- Every settings route reads the user from the session. No route or body value can name another user, so one user
  cannot read or change another's settings (rule 2 and 3).

## Label precedence
- Personal, then Provider, then Service Line default. A personal Label overrides only the display name for that user.
  It never changes an API key (AGENTS.md architecture decision on personalization).
- Rejected: personal Labels override exports (a shared file would read differently per author, confusing recipients).

## Exports use Provider-level names
- Screens show the viewer's Labels; CSV and Excel exports use the Provider-level name so a file means the same to
  everyone it is sent to. Header cells pass through the shared export writer, so Label text with a leading `=`, `+`,
  `-`, `@`, tab or CR is neutralized like any other cell.
- Risk: a user sees one name on screen and another in the file. Mitigated by documenting it on the settings page.

## Preferences are a closed set
- Theme, default Service Line, default Reporting Division and default date range only. Values are validated against
  fixed options or against the user's own scope and fall back to defaults when stale. A fixed key list bounds storage.
- Rejected: free-form key-value storage (unbounded, hard to validate).

## Pushed views: hide and restore
- Delivered copies are the user's. Hiding is a reversible flag on the user's own copy; restore clears it. No delete is
  needed to get them out of the way, and a hidden view is never re-delivered by the same Assignment.
- Rejected: forced delete (not restorable; delivery idempotence would redeliver it).

## Impersonation
- An admin impersonating a user sees that user's Labels and preferences. Settings writes are rejected during
  impersonation because the session is read-only.

## Risks
- Personal Labels could mislead (renaming Reporting Division to a Provider name). Bounded length and the fact that
  exports use Provider names limit damage; reserved words are an open question.
- Default Division or Service Line may become invalid after a scope change; resolve at use, not at save.
