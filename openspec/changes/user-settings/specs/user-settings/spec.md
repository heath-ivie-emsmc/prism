## ADDED Requirements

### Requirement: Display preferences
The system SHALL let a signed-in user save a theme, a default Service Line, a default Reporting Division and a
default date range, MUST accept only these keys, and MUST validate each value. A default Service Line or Reporting
Division MUST be honored only if the user's scope still includes it.

#### Scenario: Save and apply
- **GIVEN** a signed-in user
- **WHEN** the user saves a theme and a default date range and reloads
- **THEN** both are applied

#### Scenario: Unknown key
- **GIVEN** a request with a preference key outside the allowed set
- **WHEN** the system processes it
- **THEN** it is rejected

#### Scenario: Division outside scope
- **GIVEN** a user whose Facility scope excludes a Reporting Division
- **WHEN** the user saves it as the default
- **THEN** the default is rejected or not honored and no data for it is returned

#### Scenario: Oversized value
- **GIVEN** a preference value above the size cap
- **WHEN** the user saves it
- **THEN** the system rejects it

### Requirement: Settings belong to the session user
The system MUST read and write settings only for the signed-in user, and MUST ignore any user, owner or Provider
named in a route, header or body.

#### Scenario: Named other user
- **GIVEN** a request that names another user
- **WHEN** the system processes it
- **THEN** only the session user's settings are read or changed

#### Scenario: Anonymous
- **GIVEN** no session
- **WHEN** a settings route is requested
- **THEN** the system returns 401

#### Scenario: Cross-Provider
- **GIVEN** a user assigned to Provider A only
- **WHEN** the user requests Provider B's pushed views through settings
- **THEN** the system returns 403 or 404

### Requirement: Personal Labels
The system SHALL let a user rename renamable terms for themselves only, MUST cap Label length and count, and MUST
NOT let a Label change an API key. Error and log text MUST NOT quote the Label.

#### Scenario: Rename
- **GIVEN** a user
- **WHEN** the user sets a personal Label for Reporting Division
- **THEN** that user sees the new name on screen and other users do not

#### Scenario: API key unchanged
- **GIVEN** a personal Label for a term
- **WHEN** the user calls the API
- **THEN** the key for that term is unchanged

#### Scenario: Too long
- **GIVEN** a Label above the length cap
- **WHEN** the user saves it
- **THEN** the system rejects it without echoing the text in the error

#### Scenario: Not renamable
- **GIVEN** a term that is not renamable
- **WHEN** the user tries to rename it
- **THEN** the system rejects the request

#### Scenario: Payload markers
- **GIVEN** a rejected Label submission with marker text
- **WHEN** logs and stored errors are inspected
- **THEN** none contains the markers

### Requirement: Label precedence
The system SHALL show a term using the user's personal Label first, then the Provider Label, then the Service Line
default.

#### Scenario: Personal wins
- **GIVEN** a personal and a Provider Label for a term
- **WHEN** the user views a screen
- **THEN** the personal Label is shown

#### Scenario: Provider next
- **GIVEN** only a Provider Label
- **WHEN** the user views a screen
- **THEN** the Provider Label is shown

#### Scenario: Default
- **GIVEN** neither
- **WHEN** the user views a screen
- **THEN** the Service Line default is shown

### Requirement: Provider-level Labels need permission
The system MUST allow Provider-level Labels to be changed only by users holding the settings-manage permission.

#### Scenario: Permitted
- **GIVEN** a user with settings-manage in the Provider
- **WHEN** the user changes a Provider Label
- **THEN** it is saved for that Provider

#### Scenario: Denied
- **GIVEN** a user without settings-manage
- **WHEN** the user changes a Provider Label
- **THEN** the system returns 403

#### Scenario: Cross-Provider
- **GIVEN** a user with settings-manage in Provider A
- **WHEN** the user changes Provider B's Label
- **THEN** the system returns 403 or 404

### Requirement: Exports use Provider-level names
The system SHALL use the Provider-level name, not a personal Label, for column and header text in CSV and Excel
exports, and MUST quote cells and neutralize cells starting with `=`, `+`, `-`, `@`, tab or CR.

#### Scenario: Personal Label on screen only
- **GIVEN** a user with a personal Label and an export
- **WHEN** the user exports
- **THEN** the file header shows the Provider-level name

#### Scenario: Formula in a Provider Label
- **GIVEN** a Provider Label beginning with `=` and one with an embedded double quote
- **WHEN** the file is produced
- **THEN** both headers are quoted and neutralized

### Requirement: Manage pushed views
The system SHALL let a user hide and restore views delivered from a View Template, mark them as from EMS, and MUST
NOT redeliver a hidden view from the same Assignment.

#### Scenario: Hide
- **GIVEN** a delivered view
- **WHEN** the user hides it
- **THEN** it leaves the user's list but can be restored

#### Scenario: Restore
- **GIVEN** a hidden view
- **WHEN** the user restores it
- **THEN** it returns to the list

#### Scenario: Not redelivered
- **GIVEN** a hidden view and an unchanged Assignment
- **WHEN** the user signs in
- **THEN** no new copy is delivered

#### Scenario: Another user's view
- **GIVEN** a view delivered to another user
- **WHEN** the user tries to hide it
- **THEN** the system returns 404

### Requirement: Reset home page
The system SHALL let a user reset the home page to the live reference to the Persona's published View Template.

#### Scenario: Reset
- **GIVEN** a user with a customized home page
- **WHEN** the user resets it
- **THEN** the home page follows the Persona's template again

### Requirement: Settings during impersonation
The system MUST reject settings changes while an impersonation session is active, and MUST show the target user's
Labels and preferences to the admin.

#### Scenario: Write rejected
- **GIVEN** an active impersonation session
- **WHEN** a settings change is sent
- **THEN** the system rejects it

#### Scenario: Target's Labels
- **GIVEN** a target user with personal Labels
- **WHEN** the admin views a screen
- **THEN** the target user's Labels are shown

## Open questions
- Renamable term list, Label length and character rules.
- Retake questionnaire option in v1; default date range choices.
- Hiding pushed views versus sharing; export scope for Provider-level names.
