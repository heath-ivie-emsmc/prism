## Purpose
Prism signs users in through the Account Portal's signed login-token flow, then keeps its own HttpOnly cookie session. This capability covers the login handshake, single-use login tokens, the cookie session and its 8 hour lifetime, CSRF, roles mapped to permissions, the signed-in user's profile, Provider scope taken from the route and checked against the user's assignments, Facility scope, session revocation, rate limits keyed on a trusted client address, return-target sanitization, the anonymous route list and named permission policies. It characterizes what is implemented today; known gaps are listed under Open questions.

Sources: `docs/auth-setup.md`, `docs/adr/0002-api-versioning-docs-and-observability.md`, `AGENTS.md` (sections 3, 7, 8), `CONTEXT.md`; tests in `tests/Prism.Tests.Integration/Auth/`, `tests/Prism.Tests/Auth/`, `tests/Prism.Tests.Integration/Metrics/` (Provider and Facility scope).

## Requirements

### Requirement: Sign-in handshake start
The system SHALL start a sign-in by setting a single-use state value in a browser-bound cookie, remembering a safe return target, and redirecting the browser to the Account Portal with the application id and that state. The start route MUST be reachable without a session and MUST be rate limited per client.

#### Scenario: Start redirects to the portal with fresh state
- **GIVEN** an anonymous browser
- **WHEN** it requests the sign-in start route
- **THEN** it is redirected to the Account Portal with the application id and a state value
- **AND** the same state value is set in an HttpOnly state cookie

#### Scenario: Every start issues a different state
- **GIVEN** two sign-in starts
- **THEN** their state values differ

#### Scenario: Unsafe return target is not remembered
- **GIVEN** a start request whose return target is not a safe same-origin path
- **WHEN** the start route runs
- **THEN** no return target is remembered
- **AND** a return target left by an earlier abandoned sign-in is cleared

#### Scenario: Local development return address
- **GIVEN** a Development host with a loopback login return address configured
- **WHEN** a sign-in is started
- **THEN** the portal is asked to send the login token to that address
- **AND** a deployed host with that setting refuses to start

### Requirement: Signed login token validation
The system SHALL accept a login token only when it is a signed token whose signature verifies against the portal's published public key (RSA, PKCS#1 or PSS), whose issuer and audience match the configured values exactly, which carries an expiry and a token id, and which is within the permitted size. The system MUST refuse a token whose remaining lifetime is longer than the configured maximum (10 minutes by default), so a token cannot outlive its replay entry. Expiry SHALL allow two minutes of clock skew.

#### Scenario: Valid token signs in
- **GIVEN** a login token signed by the portal for this application and not expired
- **WHEN** it reaches the callback with matching state
- **THEN** a session cookie is issued and the browser lands on the remembered page

#### Scenario: Token signed by someone else
- **GIVEN** a login token signed with a key the portal did not publish
- **WHEN** it reaches the callback
- **THEN** sign-in fails with `sso_failed` and no session is created

#### Scenario: Expired token
- **GIVEN** an expired login token
- **WHEN** it reaches the callback
- **THEN** the browser is sent to the login page with `sso_expired`

#### Scenario: Oversized or empty token
- **GIVEN** a login token that is empty or longer than the size cap
- **WHEN** it reaches the callback
- **THEN** it is refused with `sso_failed` before any portal call is made

#### Scenario: Token that lives too long
- **GIVEN** a login token that expires later than the maximum permitted lifetime
- **WHEN** it is validated
- **THEN** it is refused

### Requirement: Single-use login tokens
The system SHALL accept each login token id once. The replay entry MUST outlive the token, and the first use MUST be recorded atomically so concurrent uses cannot both win. When the replay record cannot be written or read, sign-in MUST fail closed. Deployed environments SHALL use a replay store shared by every instance; Development and tests MAY use an in-memory store with a hard size cap that refuses new sign-ins when full.

#### Scenario: Replayed token refused
- **GIVEN** a login token that already produced a session
- **WHEN** the same token reaches the callback again, from any browser
- **THEN** sign-in fails with `sso_failed` and no second session is created

#### Scenario: Replay store unavailable
- **GIVEN** the shared replay store cannot be reached
- **WHEN** a sign-in is attempted
- **THEN** sign-in fails and no session is issued

#### Scenario: In-memory store full
- **GIVEN** the in-memory replay store is at its size cap with unexpired entries
- **WHEN** a new sign-in arrives
- **THEN** it is refused rather than forgetting ids that could be replayed

#### Scenario: Deployed host without shared store
- **GIVEN** a deployed environment with no replay store configured
- **THEN** the host refuses to start

### Requirement: Callback bound to the initiating browser
The callback SHALL complete a sign-in only when the state echoed by the portal equals the state cookie set by this browser's own start. The state cookie MUST be spent by the first callback whether or not it matches. A callback that carries no echoed state SHALL be allowed only when idp-initiated sign-in is enabled, only for a browser that holds a state cookie from its own start, and only after the user confirms on a page served by the application. A callback with no state cookie MUST always be refused.

#### Scenario: State mismatch
- **GIVEN** a state cookie that differs from the echoed state
- **WHEN** the callback runs
- **THEN** sign-in fails with `sso_failed` and no session is created

#### Scenario: State is single use
- **GIVEN** a callback that already consumed the state cookie
- **WHEN** the same callback is repeated
- **THEN** it is refused

#### Scenario: Link sent to a browser that never started sign-in
- **GIVEN** an attacker's callback link carrying a valid login token, opened by a victim browser with no state cookie
- **WHEN** the callback runs
- **THEN** it is refused with `sso_failed` and the victim is not signed in as the attacker

#### Scenario: Missing state with idp-initiated sign-in off
- **GIVEN** idp-initiated sign-in is disabled and the portal did not echo state
- **WHEN** the callback runs
- **THEN** it is refused, and the confirm route does not exist

#### Scenario: Confirmation page for a browser that started sign-in
- **GIVEN** idp-initiated sign-in is enabled and the browser holds its own state cookie
- **WHEN** a callback arrives without echoed state
- **THEN** a confirmation page is shown and no session is created yet
- **AND** the page cannot be framed, cached or used to run script, and the login token is HTML-encoded in it

#### Scenario: Confirmation completes once
- **GIVEN** the confirmation page and its nonce cookie
- **WHEN** the same browser submits the matching nonce
- **THEN** the user is signed in
- **AND** a second submission, a wrong nonce, or a submission from another browser is refused and the nonce cookie is spent

#### Scenario: Confirm form with undeclared or large body
- **GIVEN** a confirm request with no declared length or a body over the form size cap
- **WHEN** it arrives
- **THEN** it is refused before the body is read

### Requirement: Sign-in outcomes and error codes
The system SHALL report every failed sign-in by redirecting to the login page with one of `sso_failed`, `sso_expired` or `access_denied`, and MUST NOT reveal the cause. A portal outage, a malformed portal response, bad configuration or an oversized session SHALL fail closed as `sso_failed`. A user with no usable identity or with no role that grants permission to sign in SHALL be denied with `access_denied`.

#### Scenario: User with no role for this application
- **GIVEN** a valid login token for a user whose portal roles grant no Prism sign-in permission
- **WHEN** sign-in completes
- **THEN** the user is sent to the login page with `access_denied` and no session is issued

#### Scenario: Roles from another application
- **GIVEN** a user whose only role has the right name under another application's prefix
- **WHEN** sign-in completes
- **THEN** the user is denied

#### Scenario: Portal failure is generic
- **GIVEN** the portal cannot be reached or answers badly during sign-in
- **WHEN** the callback runs
- **THEN** the user sees `sso_failed` and no cause is shown

#### Scenario: Rejected sign-in keeps input out of normal logs
- **GIVEN** a rejected sign-in with a login token and role names from outside
- **THEN** neither the token nor the role names appear in logs at normal levels

#### Scenario: Many Facilities do not block sign-in
- **GIVEN** a user assigned to thousands of Facilities
- **WHEN** they sign in
- **THEN** sign-in succeeds because Providers and Facilities are not stored in the session

### Requirement: Cookie session
The system SHALL issue the session as a signed token held only in an HttpOnly, SameSite=Strict cookie with a lifetime of 480 minutes (8 hours) by default. The session MUST carry identity, roles and permissions only, and MUST NOT carry Providers or Facilities. A session MUST be rejected when its signature, issuer, audience or expiry is invalid (two minutes of clock skew allowed). Browser code SHALL never see the session token, and an Authorization bearer header MUST be ignored for the session. With secure cookies, sensitive cookies SHALL use the host-bound name prefix and the Secure flag; secure cookies default on outside Development.

#### Scenario: Session cookie is not script-readable
- **GIVEN** a signed-in browser
- **THEN** the session cookie is HttpOnly and the anti-forgery cookie is readable by scripts

#### Scenario: Cookie signed with another key
- **GIVEN** a session cookie signed with a key other than the server's
- **WHEN** it is sent to a protected route
- **THEN** the response is 401

#### Scenario: Bearer header ignored
- **GIVEN** a valid session token sent as an Authorization bearer header with no cookie
- **WHEN** a protected route is requested
- **THEN** the response is 401

#### Scenario: Session too large for a cookie
- **GIVEN** a user whose roles and permissions do not fit in a cookie
- **WHEN** sign-in completes
- **THEN** it fails with `sso_failed`

#### Scenario: Secure cookies in deployed hosts
- **GIVEN** secure cookies are enabled
- **THEN** the session, state and confirm cookies carry the host-bound prefix and the Secure flag

### Requirement: CSRF protection
Every response SHALL set an anti-forgery cookie when the request has none, and every state-changing request (any method other than GET, HEAD and OPTIONS) under the API MUST echo it in the anti-forgery header, compared in constant time, or be refused with a 403 Problem Details response before authentication runs. The confirm form is exempt because it is protected by its own nonce cookie. A new session SHALL receive a new anti-forgery token.

#### Scenario: Missing header
- **GIVEN** a signed-in browser
- **WHEN** it sends a state-changing request without the anti-forgery header
- **THEN** the response is a 403 Problem Details

#### Scenario: Wrong header value
- **GIVEN** an anti-forgery header that does not match the cookie
- **WHEN** a state-changing request is sent
- **THEN** it is refused with 403

#### Scenario: Safe methods are not checked
- **GIVEN** a GET request without the header
- **THEN** it is not refused for lack of the header

### Requirement: Roles map to permissions
The system SHALL turn the roles the portal reports into permissions through one catalog of roles and permissions. A reported role SHALL match a Prism role only by its exact name or as `<prefix>__<name>` where the prefix is the application id or a configured prefix, ignoring case and surrounding spaces. A role the catalog does not know MUST grant nothing, and permissions granted by several roles SHALL be the union without duplicates. A user MUST hold the sign-in permission to sign in. ProviderAnalyst grants sign-in, dashboard view, claim-level view and saved-view management; ProviderAdmin adds settings management; PlatformSupport grants sign-in and dashboard view only; SuperUser grants every permission.

#### Scenario: Analyst permissions
- **GIVEN** a user whose only role is ProviderAnalyst
- **THEN** the session permissions are sign-in, dashboard view, claim-level view and saved-view management

#### Scenario: Platform support has no claim-level access
- **GIVEN** a user whose only role is PlatformSupport
- **THEN** they do not hold the claim-level view permission

#### Scenario: Role name matching
- **GIVEN** a reported role differing from a Prism role only in case or surrounding spaces
- **THEN** it matches
- **AND** the same name with an unaccepted prefix grants nothing

#### Scenario: Unknown role
- **GIVEN** a user whose only portal roles are not in the catalog
- **THEN** no permissions are granted and sign-in is denied

### Requirement: Current user endpoint
The system SHALL return the signed-in user at `GET /api/v1/me`: user name, display name, the Providers the user may see (id, name and Facility count) and permissions. Facility ids MUST stay on the server; a Facility count of 0 means all Facilities. Providers SHALL be read from the portal, not from the session, and remembered for 5 minutes per user within a bounded cache. When the portal cannot be read the user MUST have no Providers rather than a stale answer, and the request SHALL still succeed. The route requires a session but no particular permission.

#### Scenario: Me after sign-in
- **GIVEN** a user who has signed in
- **WHEN** they request the current user
- **THEN** the response lists their name, Providers and permissions

#### Scenario: No session
- **GIVEN** no session cookie
- **WHEN** the current user is requested
- **THEN** the response is 401 with no server details

#### Scenario: Scope remembered between requests
- **GIVEN** two requests within five minutes
- **THEN** the portal is read once

#### Scenario: Portal unreachable
- **GIVEN** the portal cannot be reached
- **WHEN** the current user is requested
- **THEN** the response is 200 with an empty Provider list

#### Scenario: Only sign-in permission
- **GIVEN** a session holding only the sign-in permission
- **THEN** the current user route still answers 200

### Requirement: Provider scope from the route
The system SHALL take the active Provider only from the route and check it against the user's portal assignments on every request to a Provider-scoped route. The Provider MUST NOT come from the session, a body, a query value or a header. A Provider the user is not assigned to and one that does not exist SHALL both answer 404, and nothing MUST be read from the warehouse or cache for that request. When the assignments cannot be read the check MUST fail closed with the same 404. Warehouse queries SHALL get their Provider filter from the Metric Catalog after this check.

#### Scenario: Assigned Provider
- **GIVEN** a user assigned to Provider A with the dashboard permission
- **WHEN** they request Provider A's data
- **THEN** the request is allowed to proceed

#### Scenario: Cross-Provider request
- **GIVEN** a user assigned to Provider A only
- **WHEN** they request Provider B's data
- **THEN** the response is 404 and nothing is read for Provider B

#### Scenario: Cached data of another Provider
- **GIVEN** Provider B's figures are cached
- **WHEN** a user not assigned to Provider B requests them
- **THEN** the response is 404 and the cache is not asked

#### Scenario: Non-numeric or unknown Provider
- **GIVEN** a Provider id that is not a number or not assigned
- **THEN** the response is 404

#### Scenario: Assignments unreadable
- **GIVEN** the portal cannot be reached when assignments are checked
- **THEN** the response is 404 and no data is returned

#### Scenario: Removed assignment
- **GIVEN** a Provider assignment removed in the portal
- **THEN** access stops once the 5 minute remembered scope expires, without the user signing out

### Requirement: Facility scope
A Facility-limited user's assignment SHALL never be widened by a broad role. A user whose assigned Facilities do not cover every Reporting Division of the Provider MUST receive 404 on that Provider's Provider-level routes, including requests naming a sibling Facility's division. A user whose Facilities cover every Reporting Division is treated as not limited. An unknown or empty division list MUST NOT count as covered.

#### Scenario: Facility-restricted user, sibling Facility
- **GIVEN** a user limited to one Facility of Provider A
- **WHEN** they request Provider A's data or a division belonging to a sibling Facility
- **THEN** the response is 404 and nothing is read

#### Scenario: Facility-restricted user, own Facility
- **GIVEN** the same user requesting their own Facility's division
- **THEN** the response is also 404 (Provider-level routes are not offered to Facility-limited users)

#### Scenario: User covering every division
- **GIVEN** a user whose assigned Facilities include every Reporting Division of Provider A
- **THEN** they are treated as unrestricted for Provider A

#### Scenario: Division key from outside
- **GIVEN** a division value that is too long, has bad characters or is a display name
- **THEN** it is refused and never matched to anything but a key from the Provider's own data

### Requirement: Session revocation
Signing out SHALL revoke the session so that a copied cookie stops working, clear the session cookie, and redirect to the portal logout with a return address built from configuration (the Host header is used only in Development). The revocation MUST be durable, MUST be checked on every authenticated request, and MUST apply only to that one session. Revoking an already revoked session MUST NOT be an error. If the revocation store is unavailable, signed-in requests MUST fail closed.

#### Scenario: Copied cookie after logout
- **GIVEN** a signed-in user who signs out
- **WHEN** the old session cookie is sent again
- **THEN** the response is 401

#### Scenario: Other sessions untouched
- **GIVEN** the same user has a second live session
- **WHEN** the first is revoked
- **THEN** the second keeps working

#### Scenario: Logout without a session
- **GIVEN** a request to log out with no session
- **THEN** nothing is revoked and the browser is still sent to the portal logout

#### Scenario: Session without an id
- **GIVEN** a validly signed session token with no session id
- **THEN** it is rejected

### Requirement: Rate limits and trusted client address
The system SHALL rate limit the routes that start a sign-in or sign out at 30 per minute and the routes that consume a login token at 10 per minute, per client address, answering 429 beyond that. The client address MUST come only from forwarded headers sent by a configured trusted proxy or network (one hop); with none configured, forwarded headers MUST be ignored. IPv4-mapped IPv6 addresses SHALL be keyed as IPv4, and IPv6 clients SHALL be keyed by their /64. No request header may buy a new budget.

#### Scenario: Start is limited
- **GIVEN** a client that has used its start budget
- **WHEN** it starts another sign-in in the same minute
- **THEN** the response is 429

#### Scenario: Token routes have a tighter budget
- **GIVEN** the callback and confirm routes
- **THEN** they share a lower per-minute limit than the start route

#### Scenario: Spoofed forwarded address with no trusted proxy
- **GIVEN** no trusted proxy is configured
- **WHEN** a client rotates the forwarded-for header
- **THEN** it keeps the same budget

#### Scenario: Forwarded address from an untrusted sender
- **GIVEN** a forwarded header arriving from an address that is not a trusted proxy
- **THEN** it is ignored

#### Scenario: Behind a trusted proxy
- **GIVEN** a trusted proxy forwarding two distinct clients
- **THEN** each client has its own budget

#### Scenario: IPv6 rotation
- **GIVEN** a client rotating addresses inside one /64
- **THEN** all its requests share one budget

### Requirement: Return target sanitization
The system SHALL accept a post-login return target only when it is a same-origin, root-relative path of at most 2048 characters. It MUST reject empty values, absolute URLs, `//` prefixes, backslashes, `@`, control, whitespace and invisible format characters (which browsers strip), and any path that is the login page or beneath it. A rejected target SHALL fall back to the configured default landing path, then to the root.

#### Scenario: Safe path accepted
- **GIVEN** a return target `/dashboard?period=2026-10`
- **THEN** it is accepted

#### Scenario: Open redirect strings rejected
- **GIVEN** return targets `https://evil.com`, `//evil.com`, `/\evil`, `/\\evil`, `@evil`, `/@evil.com`, `javascript:alert(1)` or `relative/path`
- **THEN** each is rejected

#### Scenario: Browser-normalized characters
- **GIVEN** a target containing tab, line feed, carriage return, NUL, DEL, NEL, zero-width space, line separator or byte order mark after the leading slash
- **THEN** it is rejected

#### Scenario: Login loop
- **GIVEN** a target of `/login` or `/login/sso`
- **THEN** it is rejected
- **AND** `/loginx` is accepted

### Requirement: Anonymous and protected routes
The API route group SHALL require an authenticated user by default. The only anonymous API routes MUST be the sign-in handshake routes (start, callback, confirm, logout), each with a reason for being public; the health probes `/up` and `/ready` SHALL stay anonymous. The API description and reference pages MUST require a session. Any other API route MUST answer an anonymous caller with 401 or 403. Unknown paths under the API MUST stay a plain 404 and never fall back to HTML. The handshake is not versioned; the JSON API lives under `/api/v1`.

#### Scenario: Anonymous list matches
- **GIVEN** every route discovered at runtime under the API
- **THEN** the routes marked anonymous are exactly the documented handshake routes

#### Scenario: Protected route refuses anonymous callers
- **GIVEN** any API route not on the anonymous list
- **WHEN** an anonymous caller uses each of its methods
- **THEN** the response is 401 or 403

#### Scenario: Health probes anonymous
- **WHEN** an anonymous caller requests `/up` and `/ready`
- **THEN** both answer 200 when healthy

#### Scenario: Unknown API path
- **GIVEN** an unknown path under the API
- **THEN** the response is 404 and not the SPA page

### Requirement: Named permission policies
Routes gated by a permission SHALL use a named policy that requires that permission in the session. Policies exist for dashboard view, claim-level view and saved-view management. A permission claim for a different permission MUST NOT satisfy a policy: dashboard view does not satisfy claim-level view. A signed-in user without the permission SHALL receive 403 or 404 on every route that names the policy.

#### Scenario: Policy needs its permission
- **GIVEN** a principal with, or without, the dashboard view permission
- **WHEN** the dashboard view policy is evaluated
- **THEN** it succeeds only with the permission

#### Scenario: Other permission does not substitute
- **GIVEN** a principal holding only the patient-level view permission or only dashboard view
- **WHEN** the dashboard view or claim-level view policy is evaluated respectively
- **THEN** it fails

#### Scenario: Signed in without permission
- **GIVEN** a user holding only the sign-in permission
- **WHEN** they call a route that names a policy
- **THEN** the response is 403 or 404

### Requirement: Deployed-environment auth configuration
A host SHALL refuse to start unless sign-in configuration is present and valid. Base configuration leaves resource identifiers empty, so a host with no environment file MUST fail. Deployed environments (everything except Development and Testing) MUST use https URLs, secure cookies and a public origin, MUST have a shared replay store, and MUST NOT set the local login return address. The session signing key MUST be at least 32 random bytes and secrets MUST NOT be in the repository.

#### Scenario: Missing configuration
- **GIVEN** no sign-in configuration
- **THEN** the host refuses to start

#### Scenario: Deployed host with http or insecure cookies
- **GIVEN** a deployed environment with an http URL, insecure cookies or no public origin
- **THEN** options validation fails

#### Scenario: Fully https deployed configuration
- **GIVEN** a deployed environment with https everywhere, secure cookies, a public origin and shared replay store
- **THEN** options validation passes

#### Scenario: Development loopback
- **GIVEN** the Development environment
- **THEN** http loopback URLs are allowed

## Open questions
- Roles and permissions live in the session for up to 8 hours, so a role removed in the portal keeps working until the session ends or is revoked (documented known gap). Providers refresh every 5 minutes; permissions do not.
- There is no "revoke all of a user's sessions" and no admin action that revokes one (documented known gap).
- Rate-limit counters are per instance, not shared across instances (documented known gap). Requests with an unresolvable client address all share one `unknown` bucket.
- At most 2,000 companies per user are read and only the first 25 get names (documented known gap); behavior for a user beyond that is unspecified.
- The Content-Security-Policy is report-only until the SPA has been seen clean under it (documented known gap).
- Logout is a GET because the portal flow redirects through it; SameSite=Strict stops a cross-site page revoking a session but a link can still send a user to the portal logout. It is anonymous and exempt from the CSRF check by being a GET.
- The portal does not echo `state` today, so idp-initiated confirmation is enabled in Development; the target is to turn it off once the portal echoes state. Which deployed environments run with it on is not stated in the sources.
- AGENTS.md section 2 asks for Facility scope where a user has one. Today a Facility-limited user gets 404 on every Provider-level route, including their own Facility, which is stricter than limiting them to their divisions. Whether that is the intended end state is not recorded.
- The permissions for settings management and patient-level view exist in the catalog (patient-level view is marked reserved), but no route names a policy for them. Policies for them, and any route-level Facility check beyond Providers, are not yet implemented.
- AGENTS.md section 8 requires a handler test per new policy. Policies are claim-based and tested through the authorization service rather than a handler. The documentation also still refers to `/api/me` and the matrix test carries a comment that no route names a policy, both out of date.
- AGENTS.md section 7 asks that unknown-user and known-user login paths take the same time; Prism has no password login, so this has no implemented behavior or test.
- Logout without a session still redirects to the portal logout, and unauthenticated failed-auth budgets apply only through the per-address rate limits; there is no per-account lockout.
- Session claim-level read rate limits per signed-in user (default 20 per minute) are configured under auth options but belong to the claims-attention capability.
