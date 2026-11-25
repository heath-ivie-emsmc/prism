## MODIFIED Requirements

### Requirement: Roles map to permissions
The system SHALL turn the roles the portal reports into the right to sign in and, for Internal Users, the admin tiers, through one catalog of roles and permissions. A reported role SHALL match a Prism role only by its exact name or as `<prefix>__<name>` where the prefix is the application id or a configured prefix, ignoring case and surrounding spaces. A role the catalog does not know MUST grant nothing. A user MUST hold the sign-in permission to sign in. Customer permissions (dashboard view, claim-level view, PHI view, saved-view management, settings management, user management) MUST NOT come from portal roles: they come from the user's Customer Role in the Provider named in the route (see `customer-access-roles`).

#### Scenario: Analyst permissions
- **GIVEN** a user whose only role is the login-only PrismUser (the old ProviderAnalyst role no longer exists)
- **THEN** the session permissions are the sign-in permission only, with no customer permission

#### Scenario: Platform support has no claim-level access
- **GIVEN** a user whose only role is the removed PlatformSupport, ProviderAnalyst, ProviderAdmin or SuperUser
- **THEN** the role is unknown to the catalog, grants nothing, and the user cannot sign in

#### Scenario: Role name matching
- **GIVEN** a reported role differing from a Prism role only in case or surrounding spaces
- **THEN** it matches
- **AND** the same name with an unaccepted prefix grants nothing

#### Scenario: Unknown role
- **GIVEN** a user whose only portal roles are not in the catalog
- **THEN** no permissions are granted and sign-in is denied
