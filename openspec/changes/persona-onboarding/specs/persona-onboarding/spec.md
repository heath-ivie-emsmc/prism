## ADDED Requirements

### Requirement: Persona configuration
The system SHALL let authorized Internal Users manage Personas, onboarding questions (two to three), options and
scoring weights, and each Persona MUST point to one View Template. Only published configuration SHALL be shown to
users. The system MUST cap counts and weight ranges.

#### Scenario: Persona created
- **GIVEN** an authorized Internal User and an existing View Template
- **WHEN** the user creates a Persona that points to it
- **THEN** the Persona exists in an unpublished state

#### Scenario: Missing template
- **GIVEN** a Persona that points to a View Template that does not exist
- **WHEN** the user saves it
- **THEN** the system rejects it

#### Scenario: Fourth question
- **GIVEN** three published questions
- **WHEN** the user publishes a fourth
- **THEN** the system rejects the publish

#### Scenario: Fewer than two questions published
- **GIVEN** fewer than two published questions
- **WHEN** a new user signs in
- **THEN** onboarding is not required and the user is not asked

#### Scenario: Publishing needs a published template
- **GIVEN** a Persona whose View Template is a draft
- **WHEN** the user publishes the Persona
- **THEN** the system rejects the publish

#### Scenario: Persona in use
- **GIVEN** a Persona that a user has, or that a question option weighs
- **WHEN** the user deletes it
- **THEN** the system rejects the deletion and the Persona stays

#### Scenario: Unpublished configuration hidden
- **GIVEN** questions saved but not published
- **WHEN** a customer opens the app
- **THEN** the questions are not shown

#### Scenario: Authoring and publishing tiers
- **GIVEN** an Internal User with only the template author tier
- **WHEN** the user publishes a Persona or a question
- **THEN** the system returns 403, and the template publisher tier is required to publish

#### Scenario: Unauthorized management
- **GIVEN** a customer or an Internal User without the required tier
- **WHEN** the user calls a Persona or question route
- **THEN** the system returns 403

#### Scenario: Oversized configuration
- **GIVEN** more options or Personas than the allowed maximum, or a weight outside the allowed range
- **WHEN** the user saves
- **THEN** the system rejects it

### Requirement: Mandatory questionnaire at first login
The system SHALL require a user with no Persona to complete the questionnaire at first login before reaching the
home page, and SHALL tell the SPA that onboarding is required through the current-user response. The server MUST set
that value; a value sent by the client MUST be ignored.

#### Scenario: New user
- **GIVEN** a customer user with no Persona and published questions
- **WHEN** the user signs in
- **THEN** onboarding is required and the questionnaire is shown before the home page

#### Scenario: Completed user
- **GIVEN** a user with a Persona
- **WHEN** the user signs in
- **THEN** the questionnaire is not shown

#### Scenario: Client claims completion
- **GIVEN** a request that carries an onboarding-completed or Persona value
- **WHEN** the server processes it
- **THEN** the value is ignored and the stored state is unchanged

### Requirement: Fail open to the Service Line default
The system MUST NOT block a user when no questions or Personas are published or when loading them fails, and SHALL
show the Service Line default home page instead.

#### Scenario: Nothing published
- **GIVEN** no published questions or Personas
- **WHEN** a new user signs in
- **THEN** the user reaches the Service Line default home page

#### Scenario: Load failure
- **GIVEN** loading the questionnaire fails
- **WHEN** a new user signs in
- **THEN** the user reaches the default home page and the failure text is not logged with submitted values

### Requirement: Scoring selects one Persona
The system SHALL sum the weights of the chosen options per Persona, select the Persona with the highest total, and
break ties by the lowest sort order. Only options that belong to the published questions SHALL be accepted.

#### Scenario: Highest weight wins
- **GIVEN** answers whose weights total more for Persona A than Persona B
- **WHEN** the user submits
- **THEN** Persona A is stored for the user

#### Scenario: Tie
- **GIVEN** answers that tie Persona A and Persona B, and A has the lower sort order
- **WHEN** the user submits
- **THEN** Persona A is stored

#### Scenario: Unknown option
- **GIVEN** a submission naming an option that is not in the published questions
- **WHEN** the user submits
- **THEN** the system rejects it and stores nothing

#### Scenario: Oversized submission
- **GIVEN** more answers than the questions can hold in total (one per single-choice question and one per option on a multiple-choice question), or a body above the size cap
- **WHEN** the user submits
- **THEN** the system rejects it

### Requirement: Multiple-choice questions
The system SHALL let an Internal User mark a question as multiple choice ("choose all that apply"), such as which lines of service
a Provider supports. A user MUST choose at least one option on every question, MUST choose exactly one on a single-choice question, and
MUST NOT choose the same option twice. The weights of every chosen option SHALL be added together before the Persona is picked.

#### Scenario: Several options chosen
- **GIVEN** a multiple-choice question with options for air, ground, interfacility and fire billing
- **WHEN** the user chooses air, interfacility and fire billing and answers the other questions
- **THEN** the weights of all three options count toward the Persona

#### Scenario: Nothing chosen
- **GIVEN** a multiple-choice question
- **WHEN** the user submits with none of its options chosen
- **THEN** the system rejects it and stores nothing

#### Scenario: Several options on a single-choice question
- **GIVEN** a question that is not multiple choice
- **WHEN** the user submits two options for it
- **THEN** the system rejects it and stores nothing

#### Scenario: Same option twice
- **GIVEN** a multiple-choice question
- **WHEN** the user submits the same option twice
- **THEN** the system rejects it and stores nothing

### Requirement: Only the outcome is stored
The system MUST store only the resulting Persona and a completed timestamp, and MUST NOT store or log raw answers.

#### Scenario: Answers not retained
- **GIVEN** a completed questionnaire with known marker answers
- **WHEN** the stored data, logs and error records are inspected
- **THEN** none contains the raw answers

### Requirement: One Persona per user
The system SHALL keep one Persona per user, keyed by the signed-in user, and MUST NOT take the user from a route,
header or body.

#### Scenario: Same Persona across Providers
- **GIVEN** a user assigned to two Providers
- **WHEN** the user opens the home page in either
- **THEN** the same Persona's template is used, with each Provider's data scoped normally

#### Scenario: Another user's profile
- **GIVEN** a request that names a different user
- **WHEN** the server processes it
- **THEN** the named user is ignored and only the session user's profile is read or written

### Requirement: No questionnaire during impersonation
The system MUST NOT show the questionnaire and MUST reject any save of a Persona or completed state while an
impersonation session is active.

#### Scenario: Impersonating a new user
- **GIVEN** an admin impersonating a user with no Persona
- **WHEN** the admin opens the home page
- **THEN** no questionnaire is shown and the default home page is used

#### Scenario: Save attempt during impersonation
- **GIVEN** an active impersonation session
- **WHEN** a questionnaire submission is sent
- **THEN** the system rejects it and nothing is stored

### Requirement: Home page is a live reference
The system SHALL render the user's home page from the Persona's current published View Template, so a published edit
reaches the user without delivery. A user without a usable Persona or template SHALL see the Service Line default.

#### Scenario: Template edited
- **GIVEN** a user whose Persona points to a template
- **WHEN** staff publish an edit and the user reloads
- **THEN** the home page shows the edited layout

#### Scenario: Metric not applicable
- **GIVEN** a template widget whose Metric does not apply to the user's Provider or scope
- **WHEN** the home page renders
- **THEN** the widget shows the empty state

### Requirement: Fork and reset
The system SHALL let a user customize the home page, which forks the template into the user's own Saved Views as the
home page, and SHALL let the user reset, which removes the fork and returns the home page to the live reference.

#### Scenario: Customize
- **GIVEN** a user on the live-reference home page
- **WHEN** the user customizes it
- **THEN** a Saved View is created for that user as the home page and later template edits no longer change it

#### Scenario: Reset
- **GIVEN** a user with a forked home page
- **WHEN** the user resets
- **THEN** the home page follows the Persona's current published template again

#### Scenario: Fork in another Provider
- **GIVEN** a fork made in Provider A
- **WHEN** a user assigned only to Provider B requests it
- **THEN** it is not visible to them

### Requirement: Retake
The system SHALL let a user who submits the questionnaire again replace their Persona, and MUST keep a home page copy the user already made.

#### Scenario: Answering again
- **GIVEN** a user with a Persona and a forked home page
- **WHEN** the user submits new answers that select another Persona
- **THEN** the stored Persona changes and the forked home page is unchanged

### Requirement: Unusable Persona falls back
The system SHALL show the Service Line default when the user's Persona or its View Template is not published, and MUST NOT delete either.

#### Scenario: Persona unpublished
- **GIVEN** a user whose Persona is later unpublished
- **WHEN** the user opens the home page
- **THEN** the standard dashboard is shown

## Open questions
- Service Line default for multi-Service-Line users.
- Impersonation suppression (see the requirement above) is not built until `admin-impersonation`.
