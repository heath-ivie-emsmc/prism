## ADDED Requirements

### Requirement: Internal User membership
The system SHALL treat a signed-in user as an Internal User only when the portal reports the user as a member of the
one configured internal group at sign-in. The group name MUST come from a single per-environment setting and MUST be
required in every deployed environment.

#### Scenario: Member of the internal group
- **GIVEN** a user whose portal data lists the configured internal group
- **WHEN** the user signs in
- **THEN** the session marks the user as an Internal User

#### Scenario: Not a member
- **GIVEN** a user whose portal data does not list the configured group
- **WHEN** the user signs in
- **THEN** the session does not mark the user as an Internal User

#### Scenario: Group data missing fails closed
- **GIVEN** the portal response carries no group information
- **WHEN** the user signs in
- **THEN** the user is not an Internal User and sign-in otherwise proceeds as for a customer

#### Scenario: Near-match group name
- **GIVEN** a user in a group whose name differs from the configured name by case, spacing or a suffix
- **WHEN** the user signs in
- **THEN** the user is not an Internal User

#### Scenario: Missing configuration blocks start
- **GIVEN** a deployed environment with no group name configured
- **WHEN** the host starts
- **THEN** startup fails options validation

### Requirement: Internal User status is set by the server only
The system MUST set Internal User status and admin permissions from server-side sign-in data only, and MUST ignore
or reject any such flag arriving in a request header, cookie value, query or body.

#### Scenario: Client-sent flag ignored
- **GIVEN** a signed-in customer
- **WHEN** the customer sends a request carrying an internal-user flag in a header, cookie or body
- **THEN** the flag has no effect and admin routes still return 403

#### Scenario: Test-mode header in a deployed environment
- **GIVEN** a deployed environment
- **WHEN** a request carries a header that would grant admin access in a test host
- **THEN** the request is treated as an ordinary customer request

### Requirement: Admin access needs group and tier
The system SHALL allow a request to an admin route only when the user is an Internal User AND holds the tier
permission the route requires. The tiers SHALL be four independent permissions: template author, template
publisher, customer data viewer and admin manager. No tier SHALL imply another.

#### Scenario: Internal User with the right tier
- **GIVEN** an Internal User holding the template author permission
- **WHEN** the user requests a route that requires template author
- **THEN** the request is allowed

#### Scenario: Internal User with the wrong tier
- **GIVEN** an Internal User holding only the template author permission
- **WHEN** the user requests a route that requires admin manager
- **THEN** the system returns 403

#### Scenario: Customer holding an admin permission
- **GIVEN** a user who is not an Internal User but holds an admin tier permission
- **WHEN** the user requests an admin route
- **THEN** the system returns 403

#### Scenario: Internal User with no tier
- **GIVEN** an Internal User with no tier permission
- **WHEN** the user requests any admin route
- **THEN** the system returns 403

#### Scenario: Anonymous request
- **GIVEN** no session
- **WHEN** a request reaches an admin route
- **THEN** the system returns 401 or 403

### Requirement: Admin routes are a separate group
The system MUST serve admin functions only from a dedicated admin route group that requires authentication by
default and has its own named policies. Customer routes SHALL NOT change behavior for Internal Users.

#### Scenario: Every admin route is covered
- **GIVEN** the set of routes discovered at runtime
- **WHEN** the authorization matrix runs
- **THEN** every admin route denies anonymous callers and authenticated callers without the required permission

#### Scenario: Internal User on a customer route
- **GIVEN** an Internal User with no assignment to a Provider
- **WHEN** the user requests a Provider route as a customer would
- **THEN** the system applies the same Provider assignment check as for any user and denies the request

### Requirement: Current-user response exposes admin capability
The system SHALL tell the signed-in user, in the current-user response, whether they are an Internal User and which
admin permissions they hold, and the SPA SHALL show the admin area only to such users. The SPA check is advisory;
the server decides.

#### Scenario: Internal User sees the admin entry
- **GIVEN** an Internal User with at least one tier
- **WHEN** the SPA loads the current user
- **THEN** the admin area is offered

#### Scenario: Customer sees no admin entry
- **GIVEN** a customer
- **WHEN** the SPA loads the current user
- **THEN** no admin entry is offered and a typed admin URL is refused by the server with 403

### Requirement: Admin actions are audited
The system MUST write an audit record for every admin action, containing the actor, an action code, the target and
the Provider where one applies, and MUST NOT store request bodies or free text copied from input. Audit text MUST
have capped lengths.

#### Scenario: Action is recorded
- **GIVEN** an Internal User performs an admin action
- **WHEN** the action completes
- **THEN** one audit record exists with actor, action code and target

#### Scenario: Denied action is not recorded as performed
- **GIVEN** an Internal User without the tier
- **WHEN** the request is denied
- **THEN** no record claims the action happened

#### Scenario: Payload markers stay out of logs and audit
- **GIVEN** an admin request whose body contains known marker strings
- **WHEN** the request fails validation
- **THEN** captured logs, stored errors and audit records do not contain the markers

### Requirement: Admin lists are bounded
The system MUST page every admin list and cap page size, and MUST cap the length of any submitted name or text.

#### Scenario: Oversized page request
- **GIVEN** an Internal User with a tier
- **WHEN** the user requests a page size above the cap
- **THEN** the system returns at most the cap or rejects the request with 400

## Open questions
- Exact group name per environment and portal permission names.
- Whether a portal group change reaches live sessions before the next sign-in.
- Audit retention period.
