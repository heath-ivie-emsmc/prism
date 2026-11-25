## ADDED Requirements

### Requirement: Customer Roles are a fixed catalog
The system SHALL define Customer Roles in code as sets of permissions and MUST NOT allow a role to be created or changed at runtime. A role that carries claim or PHI access SHALL be restricted.

#### Scenario: Catalog
- **GIVEN** the running system
- **WHEN** the role catalog is read
- **THEN** it holds Viewer, Analyst, Claims reviewer, PHI reviewer and Provider admin, and only the last three carry claim or PHI access or user management as defined

#### Scenario: Unknown role
- **GIVEN** a request that assigns a role not in the catalog
- **WHEN** the server processes it
- **THEN** the system rejects it and nothing changes

### Requirement: Permissions come from Prism, per Provider
The system SHALL give a customer user the permissions of their Customer Role in the Provider named in the route, and MUST NOT take customer permissions from portal roles beyond the right to sign in.

#### Scenario: Different roles in two Providers
- **GIVEN** a user who is Provider admin in Provider A and Viewer in Provider B
- **WHEN** the user reads claims in each
- **THEN** the request in A is allowed if the role carries claims, and the request in B returns 403

#### Scenario: Portal role ignored
- **GIVEN** a user whose portal role used to grant claims access and who has the Viewer role in Prism
- **WHEN** the user requests claims
- **THEN** the system returns 403

#### Scenario: Role cannot be read
- **GIVEN** the role store is unavailable
- **WHEN** a customer user makes a request that needs a permission
- **THEN** the system denies it

### Requirement: Users are recorded at sign-in
The system SHALL record each customer user in each assigned Provider at sign-in, give a new user the Provider's default role, and MUST NOT record an Internal User as a customer user.

#### Scenario: First sign-in
- **GIVEN** a user assigned to a Provider who has never signed in
- **WHEN** the user signs in
- **THEN** a Provider user exists with the Provider's default role

#### Scenario: Internal User
- **GIVEN** a member of the internal group
- **WHEN** the user signs in
- **THEN** no customer Provider user is created

### Requirement: Provider admins manage their own Provider's users
The system SHALL let a user with the users-manage permission list the users of their own Provider and change their roles, and MUST NOT let them see or change another Provider's users.

#### Scenario: Change a role
- **GIVEN** a Provider admin and a Viewer in the same Provider
- **WHEN** the admin assigns the Analyst role
- **THEN** the role changes and the change is audited

#### Scenario: Another Provider
- **GIVEN** a Provider admin of A
- **WHEN** the admin lists or changes users in B
- **THEN** the system returns 404

#### Scenario: Not an admin
- **GIVEN** a user without the users-manage permission
- **WHEN** the user calls a user-management route
- **THEN** the system returns 403

#### Scenario: Own role
- **GIVEN** a Provider admin
- **WHEN** the admin changes their own role
- **THEN** the system rejects it

#### Scenario: Last admin
- **GIVEN** a Provider with one Provider admin
- **WHEN** that admin is demoted by a Provider admin
- **THEN** the system rejects it

### Requirement: Restricted roles need staff
The system SHALL let only EMS staff assign a restricted role unless staff have enabled restricted grants for that Provider, in which case a Provider admin may assign it.

#### Scenario: Admin grants claims role by default
- **GIVEN** a Provider with restricted grants off
- **WHEN** a Provider admin assigns Claims reviewer
- **THEN** the system rejects it

#### Scenario: Delegation enabled
- **GIVEN** staff enabled restricted grants for the Provider
- **WHEN** a Provider admin assigns Claims reviewer
- **THEN** the role changes and the change is audited

#### Scenario: Request-sent delegation flag
- **GIVEN** a request body that sets the delegation switch
- **WHEN** a Provider admin sends it
- **THEN** the value is ignored and the setting is unchanged

### Requirement: Staff manage customer roles
The system SHALL let an Internal User with the admin manager tier list and change the roles of users in any Provider they name, set the default role and the restricted-grant switch, and MUST audit every change.

#### Scenario: First admin
- **GIVEN** a Provider with no Provider admin and an admin manager
- **WHEN** staff assign Provider admin to a listed user
- **THEN** the role changes and is audited

#### Scenario: Staff without the tier
- **GIVEN** an Internal User without the admin manager tier
- **WHEN** the user calls a staff role route
- **THEN** the system returns 403

#### Scenario: Customer calls a staff route
- **GIVEN** a customer user, even a Provider admin
- **WHEN** the user calls a staff role route
- **THEN** the system returns 403

### Requirement: Role changes are audited and bounded
The system SHALL write an audit entry in the same transaction as each role change and setting change, recording actor, Provider, target user id and old and new role, and MUST cap list sizes and request bodies.

#### Scenario: Audit failure
- **GIVEN** an audit entry cannot be written
- **WHEN** a role change is made
- **THEN** the change does not happen

#### Scenario: Oversize list
- **GIVEN** a Provider with more users than the page size
- **WHEN** an admin lists users
- **THEN** the system returns one bounded page and says there is more
