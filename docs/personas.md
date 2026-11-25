# Personas, the first-login questionnaire and the home page

Spec: `openspec/changes/persona-onboarding`. Decision record: `docs/adr/0009-internal-access-and-impersonation.md` (decision 3).

## What it does

A new customer user answers two or three questions the first time they sign in. The answers pick one **Persona** (for example "air chief" or "finance"), and the Persona points at a published **View Template**. That template becomes the user's home page. Users are never shown their Persona's name.

## How it behaves

- **Who is asked.** `GET /api/v1/me` returns `onboardingRequired`, set only by the server. It is true when the user has no Persona and a usable questionnaire is published. The SPA sends such a user to `/welcome` before any other page.
- **Usable questionnaire.** Two or three published questions, each with at least two options, and at least one published Persona whose View Template is published. Anything less, or any failure reading it, means nobody is asked: the user gets the standard dashboard. This fails open on purpose, so a configuration gap never locks customers out.
- **Scoring.** Each option carries a weight (0 to 100) per Persona. The Persona with the highest total wins. A tie goes to the lower sort order, then the lower id.
- **Multiple choice.** A question can be marked "Let people choose more than one answer" (`allowsMultiple`), for example "Which lines of service do you support?" with air, ground, interfacility and fire billing. The user then ticks all that apply, at least one, and the weights of every ticked option are added together. A single-choice question takes exactly one answer. The submission has one `{questionId, optionId}` entry per option chosen; an option chosen twice, a question left unanswered, or an option from another question is refused.
- **What is stored.** Only the Persona and the completion time, in `user_profile`, one row per portal user (not per Provider). The answers are scored and thrown away, and are never logged.
- **Home page.** `GET /api/v1/providers/{providerId}/home` says where the page comes from: `persona` (the Persona's current published template, followed live), `custom` (the user's own copy) or `default` (the standard dashboard). The Provider is the route's, checked against the user's assignments like every Provider route.
- **Customize and reset.** `POST .../home/customize` copies the template into the user's Saved Views as "Home" and records it in `user_home_view`. `DELETE .../home/customize` deletes the copy so the page follows the template again. Deleting the copy from the Saved Views screen does the same.
- **Unpublishing.** Unpublishing a Persona, or its template, sends the users who have it back to the standard dashboard. Nothing is deleted. A Persona that a user has, or that an option weighs, cannot be deleted.
- **Answering again** replaces the Persona and keeps any copy the user made.

## Admin

`/admin/personas` (template author tier to edit, template publisher tier to publish). Routes are under `/api/v1/admin/personas` and `/api/v1/admin/onboarding-questions`. Every change is audited in the same transaction. Limits: 20 Personas, 10 questions (3 published), 2 to 8 options per question, request body 16 KB.

## Settings (what exists)

`/settings` (account menu): reset a customized home page, answer the questions again (`/settings/questionnaire`, offered only while a questionnaire is published), and hide or restore the views the Prism team pushed. A hidden view stays in the database with `is_hidden`, so it is not delivered again. Pushed views carry a "From EMS" marker in the Saved views list. Routes: `/api/v1/providers/{providerId}/settings/pushed-views` (list, `{id}/hide`, `{id}/restore`), always the session user's own views in a Provider they are assigned to.

## Not built yet

- Not showing the questionnaire during impersonation depends on `admin-impersonation`.
- Display preferences, personal Labels and export headers (the rest of `user-settings`).
