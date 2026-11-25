# Design: Personas, onboarding and home page

## Scoring
- Each option carries a weight for one or more Personas. The Persona with the highest summed weight wins; a tie goes
  to the lowest sort order. This is deterministic and testable without storing answers.
- Rejected: free-text answers or model-based classification (unbounded input, privacy risk, not reproducible).

## Mandatory but fails open
- The questionnaire is shown to every user without a Persona. If no questions or Personas are published, or loading
  fails, the user proceeds on the Service Line default home page. Failing closed would lock customers out of a
  dashboard because of a configuration gap.
- Rejected: optional questionnaire (lower coverage, the user decided on mandatory); fail closed.

## Raw answers are not stored
- Only the Persona and a completed timestamp are kept. This avoids a new store of behavioral data and avoids a
  retention question. Cost: scoring cannot be re-run after weights change; the user retakes instead.

## One Persona per user, not per Provider
- The profile is keyed by the signed-in portal user id. A user in several Providers gets one home layout source,
  with each Provider's data shown through the normal scope rules.
- Rejected: per-Provider Persona (multiplies questionnaires, no business ask).
- The profile endpoints take the user only from the session. No route or body value can name another user.

## Live reference home page, fork on customize
- The home page renders the Persona's current published View Template, so staff edits reach users without delivery.
  Customizing forks into the user's own Saved Views as home; reset removes the fork and returns to the live reference.
- Rejected: always clone the template (no way to update every user's home); never allow customizing.
- Distinction from `view-templates`: Assignments clone lazily into Saved Views; the home page does not.

## Impersonation
- While an impersonation session is active the questionnaire neither appears nor saves, so staff cannot create or
  change a customer's profile. The server enforces this; the SPA hides it too.

## Not shown on pages
- The Persona name is internal. The user sees their home page, not a label for themselves. Settings may offer a change
  option; open question.

## Risks
- Misconfigured weights can send everyone to one Persona; staff need a way to see the distribution of resulting
  Personas as aggregate counts only (kept out of scope for now).
- Live reference means a bad edit to a published template changes many home pages at once; the publish step and audit are the control.
