## ADDED Requirements

### Requirement: View Template content
The system SHALL let a user holding the template author permission create and edit a View Template that stores only
Metric keys, presentations and grid positions, and MUST validate it against the Metric Catalog. A View Template
MUST NOT store an owner, a Provider, figures, Labels or filters.

#### Scenario: Valid template saved
- **GIVEN** an Internal User with the template author permission
- **WHEN** the user saves a layout of known Metric keys with valid positions
- **THEN** the View Template is saved

#### Scenario: Unknown Metric key
- **GIVEN** an Internal User with the template author permission
- **WHEN** the user saves a layout containing a Metric key not in the Metric Catalog
- **THEN** the system rejects it with a validation error that names a code, not the submitted text

#### Scenario: Provider or figures in the body
- **GIVEN** a template save request that includes a Provider, an owner or figures
- **WHEN** the system processes it
- **THEN** those values are ignored or rejected and none is stored

#### Scenario: Non-author denied
- **GIVEN** an Internal User without the template author permission
- **WHEN** the user tries to create a View Template
- **THEN** the system returns 403

#### Scenario: Customer denied
- **GIVEN** a customer
- **WHEN** the customer requests a template route
- **THEN** the system returns 403

#### Scenario: Oversized layout
- **GIVEN** a template with more widgets than the allowed maximum
- **WHEN** the author saves it
- **THEN** the system rejects it

### Requirement: Assignment targets
The system SHALL let an authorized Internal User create an Assignment of a View Template to all Providers, to a
specific list of Providers, or to a Persona, and MUST cap the size of the Provider list.

#### Scenario: Assign to specific Providers
- **GIVEN** an authorized Internal User and a View Template
- **WHEN** the user assigns it to two named Providers and confirms
- **THEN** an Assignment exists for exactly those Providers

#### Scenario: Assign to a Persona
- **GIVEN** an existing Persona
- **WHEN** the user assigns a View Template to it and confirms
- **THEN** users of that Persona become eligible for delivery

#### Scenario: Unknown Provider in the list
- **GIVEN** a Provider list containing an identifier that does not exist
- **WHEN** the user submits it
- **THEN** the system rejects the Assignment and creates nothing

#### Scenario: Oversized target list
- **GIVEN** a Provider list above the maximum count
- **WHEN** the user submits it
- **THEN** the system rejects it

#### Scenario: Unauthorized user
- **GIVEN** an Internal User without the permission required to assign
- **WHEN** the user creates an Assignment
- **THEN** the system returns 403

### Requirement: Confirmation before every push
The system MUST show a confirmation with the number of Providers, the number of widgets that apply per Provider and
the template version before an Assignment takes effect, and MUST NOT activate an Assignment without that
confirmation. The counts SHALL be informational and SHALL NOT block the push.

#### Scenario: Counts shown
- **GIVEN** a template and a target
- **WHEN** the user starts a push
- **THEN** the confirmation lists Providers, widgets applying per Provider and the template version

#### Scenario: Unconfirmed push does nothing
- **GIVEN** a started push
- **WHEN** the user leaves without confirming
- **THEN** no Assignment is created

#### Scenario: Non-applying widgets do not block
- **GIVEN** a template with a Metric that does not apply to one target Provider
- **WHEN** the user confirms
- **THEN** the Assignment is created

#### Scenario: Confirmation skipped by a direct call
- **GIVEN** a request to create an Assignment that was not preceded by the confirmation step
- **WHEN** the system receives it
- **THEN** the system rejects it

### Requirement: Audited push and revoke
The system MUST write an audit record for every Assignment created or revoked, with actor, template, target kind and
Provider count, and MUST NOT store request text.

#### Scenario: Push audited
- **GIVEN** a confirmed push
- **WHEN** it completes
- **THEN** one audit record exists for it

#### Scenario: Revoke audited
- **GIVEN** an active Assignment
- **WHEN** an authorized user revokes it
- **THEN** an audit record is written for the revoke

### Requirement: Lazy clone delivery
The system SHALL deliver an Assignment by cloning the View Template into the user's Saved Views for the Provider in
the route on the user's next sign-in, once per Assignment, user and Provider, and MUST bound the number of
deliveries per request. The Provider, owner and source template on the clone MUST be set by the server.

#### Scenario: First sign-in after a push
- **GIVEN** an active Assignment covering the user's Provider
- **WHEN** the user next signs in
- **THEN** the user's Saved Views include a clone of the View Template marked as from EMS

#### Scenario: Idempotent delivery
- **GIVEN** a template already delivered to the user for that Provider
- **WHEN** the user signs in again or two requests race
- **THEN** exactly one clone exists

#### Scenario: Provider not covered
- **GIVEN** an Assignment for Provider A only
- **WHEN** a user of Provider B signs in
- **THEN** nothing is delivered to that user

#### Scenario: Cross-Provider read denied
- **GIVEN** a clone delivered in Provider B
- **WHEN** a user assigned only to Provider A requests Provider B's Saved Views
- **THEN** the system returns 403 or 404 and reveals nothing

#### Scenario: Facility-restricted user
- **GIVEN** a user restricted to Facility 1
- **WHEN** the user requests data for a sibling Facility through a delivered view
- **THEN** the Facility restriction still applies and the sibling Facility data is not returned

#### Scenario: Delivery bound
- **GIVEN** more Assignments than the per-request bound
- **WHEN** the user signs in
- **THEN** at most the bound is delivered now and the rest on later requests

#### Scenario: Client-supplied owner or Provider
- **GIVEN** a Saved View list request that carries an owner or Provider in a header or body
- **WHEN** delivery runs
- **THEN** those values are ignored

### Requirement: Empty-state widgets
The system SHALL deliver every widget of a template, and a widget whose Metric does not apply to the viewer's
Provider, Service Line or Facility scope MUST show the empty state instead of figures.

#### Scenario: Metric not applicable
- **GIVEN** a delivered template with a Metric unavailable to the viewer's Provider
- **WHEN** the viewer opens the view
- **THEN** that widget is present in its position and shows the empty state

### Requirement: Revoke semantics
The system SHALL stop future deliveries when an Assignment is revoked, and MUST leave copies already delivered
unchanged and owned by their users.

#### Scenario: Revoke before delivery
- **GIVEN** an Assignment revoked before a user signs in
- **WHEN** the user signs in
- **THEN** nothing from it is delivered

#### Scenario: Revoke after delivery
- **GIVEN** a delivered copy
- **WHEN** the Assignment is revoked
- **THEN** the copy remains in the user's Saved Views

#### Scenario: Revoke by an unauthorized user
- **GIVEN** an Internal User without the permission to revoke
- **WHEN** the user revokes an Assignment
- **THEN** the system returns 403

### Requirement: Aggregate delivery reports
The system SHALL give authorized Internal Users delivery reports made only of counts by Provider and template, and
MUST NOT include patient or claim detail or per-user figures. Reports MUST be paged and capped.

#### Scenario: Counts only
- **GIVEN** delivered templates
- **WHEN** an authorized user opens the report
- **THEN** it shows counts per Provider and template and no user content

#### Scenario: Unauthorized report access
- **GIVEN** a customer or an Internal User without a tier
- **WHEN** the report is requested
- **THEN** the system returns 403

## Open questions
- Which tier assigns and revokes; draft and publish lifecycle and version numbering.
- Numeric bounds for widgets, targets and deliveries per request.
- Redelivery after a user hides a delivered copy; delivery across multiple Providers.
