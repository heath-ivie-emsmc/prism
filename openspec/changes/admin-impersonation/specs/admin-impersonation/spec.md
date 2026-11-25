## ADDED Requirements

### Requirement: Start impersonation
The system SHALL let an Internal User holding the customer data viewer permission start an impersonation session for
one specific user in one Provider, and MUST require a reason within its length cap. The session MUST expire at its
time limit.

#### Scenario: Permitted start
- **GIVEN** an Internal User with the customer data viewer permission, a target user and a reason
- **WHEN** the user starts impersonation
- **THEN** a session starts that expires at the time limit

#### Scenario: Missing or oversized reason
- **GIVEN** a start request with no reason or a reason above the cap
- **WHEN** the system processes it
- **THEN** it is rejected and no session starts

#### Scenario: Wrong tier
- **GIVEN** an Internal User without the customer data viewer permission
- **WHEN** the user starts impersonation
- **THEN** the system returns 403

#### Scenario: Customer
- **GIVEN** a user who is not an Internal User
- **WHEN** the user starts impersonation
- **THEN** the system returns 403

#### Scenario: Target not in the Provider
- **GIVEN** a target user with no assignment to the named Provider
- **WHEN** the admin starts impersonation
- **THEN** the system rejects it

### Requirement: Effective access is the target's, read-only
The system MUST limit an impersonation session to the target user's own access intersected with a read-only mask, and
MUST NOT give the session any access the target lacks or any write access.

#### Scenario: Never more than the target
- **GIVEN** an admin with broad permissions impersonating a target with fewer
- **WHEN** the admin requests data
- **THEN** only what the target may read is returned

#### Scenario: Facility-restricted target
- **GIVEN** a target restricted to Facility 1
- **WHEN** the admin requests data for a sibling Facility
- **THEN** the system denies it

#### Scenario: Cross-Provider
- **GIVEN** a target assigned to Provider A only
- **WHEN** the admin requests Provider B's data in the session
- **THEN** the system returns 403 or 404

#### Scenario: Target access lookup fails
- **GIVEN** the portal cannot return the target's access
- **WHEN** the admin starts impersonation
- **THEN** the session does not start

### Requirement: Writes are rejected
The system MUST reject every request that changes data while an impersonation session is active, on every route,
including settings and questionnaire saves.

#### Scenario: Mutating request
- **GIVEN** an active impersonation session
- **WHEN** a request would create, change or delete data
- **THEN** the system rejects it and nothing changes

#### Scenario: New route is safe by default
- **GIVEN** a mutating route added later
- **WHEN** it is called during impersonation
- **THEN** it is rejected without route-specific code

### Requirement: Time limit
The system MUST check the expiry on every request during impersonation and treat an expired session as ended.

#### Scenario: Within the limit
- **GIVEN** a session younger than the limit
- **WHEN** the admin requests data
- **THEN** the request is served

#### Scenario: Past the limit
- **GIVEN** a session past the limit
- **WHEN** the admin requests data
- **THEN** the request is rejected and the admin is returned to their own view

#### Scenario: Admin session revoked
- **GIVEN** an active impersonation session
- **WHEN** the admin's own session is revoked
- **THEN** the impersonation ends

### Requirement: Audit of every request
The system MUST write an audit record for session start and end, with the reason, and for every request in the
session, with actor, target user and Provider, and MUST NOT store request bodies, query values or PHI in it.

#### Scenario: Request audited
- **GIVEN** an active session
- **WHEN** the admin loads a page of data
- **THEN** an audit record names the actor, target user and Provider

#### Scenario: No payload text
- **GIVEN** a request with marker text in the query
- **WHEN** logs and audit records are inspected
- **THEN** none contains the markers

### Requirement: PHI needs a double gate
The system SHALL return PHI during impersonation only if the target user holds the PHI permission AND the admin holds
the separate admin PHI permission, SHALL mask or omit PHI otherwise, and MUST write the PHI-read audit record. Exports
and downloads follow the same rule.

#### Scenario: Both permissions
- **GIVEN** a target with the PHI permission and an admin with the admin PHI permission
- **WHEN** the admin reads a PHI endpoint
- **THEN** PHI fields are present and an audit record is written

#### Scenario: Admin lacks the permission
- **GIVEN** a target with the PHI permission and an admin without the admin PHI permission
- **WHEN** the admin reads a PHI endpoint
- **THEN** no PHI substring appears anywhere in the response body

#### Scenario: Target lacks the permission
- **GIVEN** a target without the PHI permission and an admin with the admin PHI permission
- **WHEN** the admin reads a PHI endpoint
- **THEN** PHI is masked or omitted

#### Scenario: Export
- **GIVEN** a masked session
- **WHEN** the admin exports claim data
- **THEN** the file contains no PHI

### Requirement: No request can set or extend impersonation
The system MUST issue the impersonation state only from the admin start route and MUST ignore or reject any
header, cookie value, query or body field that tries to set, extend or end it.

#### Scenario: Header override
- **GIVEN** a customer or admin request carrying an impersonation header
- **WHEN** the system processes it
- **THEN** the header has no effect

#### Scenario: Extension attempt
- **GIVEN** an active session near expiry
- **WHEN** the request body asks for a later expiry
- **THEN** the expiry is unchanged

#### Scenario: Spoofed forwarded header
- **GIVEN** a request with a spoofed forwarded-client-address header
- **WHEN** the system applies throttling
- **THEN** the header does not alter throttling or authorization

### Requirement: Visible and suppressing behavior in the SPA
The SPA SHALL show a persistent banner during impersonation with the target and remaining time and a way to stop, MUST NOT show or save the onboarding
questionnaire, and SHALL show the target user's Labels and preferences.

#### Scenario: Banner
- **GIVEN** an active session
- **WHEN** any page is shown
- **THEN** the banner is visible and keyboard reachable

#### Scenario: Questionnaire suppressed
- **GIVEN** a target user with no Persona
- **WHEN** the admin opens the home page
- **THEN** no questionnaire is shown

#### Scenario: Stop
- **GIVEN** an active session
- **WHEN** the admin selects stop
- **THEN** the session ends and the admin sees their own view

## Open questions
- Time limit, renewal, concurrency; reason cap; admin PHI permission name.
- Notification of the target; impersonating Internal Users; read-only mask for exports.
- Behavior when the admin's permissions change mid-session.
