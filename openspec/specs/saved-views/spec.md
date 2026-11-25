## Purpose
A Saved View is a dashboard layout a user arranged and saved: which Metrics, drawn which way, and where on a 12-column grid. It belongs to one user in one Provider and can be shared by emailed Invitation with other users who have access to that Provider. This spec characterizes what is implemented today. Derived from ADR 0004, the Saved Views run guide, the Saved Views UI and accessibility contract, AGENTS.md sections 2, 3 and 4, and the unit, integration and web tests for the capability.

## Requirements

### Requirement: A Saved View stores a layout only
A Saved View SHALL store only a name, an optional description, a density and a list of Widgets, each holding a Metric key, a presentation, options, and a position and size on the grid. It MUST NOT store figures, Labels, filter values, an owner taken from a request or a Provider taken from a request. The stored layout SHALL be written from the validated object, so properties the server does not know never persist.

#### Scenario: Owner and Provider in the body are ignored
- **GIVEN** a signed-in user with the views permission creates a Saved View in a Provider they are assigned to
- **WHEN** the request body also carries an owner or Provider identifier for someone else
- **THEN** the view is created for the session user in the Provider named in the route
- **AND** the identifiers in the body have no effect

#### Scenario: Unknown properties do not persist
- **GIVEN** a layout whose Widgets carry extra properties such as a figure or a filter
- **WHEN** the view is saved and read back
- **THEN** the returned layout contains only the known Widget properties

#### Scenario: Reader sees only data they may see
- **GIVEN** a Saved View shared with another user
- **WHEN** the recipient opens it
- **THEN** each Widget reads its data through the normal Metric routes with the recipient's own access
- **AND** the view itself never widens what the recipient can see

### Requirement: Layout validation against the Metric Catalog
The server SHALL validate every layout on create and update against the Metric Catalog and the grid. A layout MUST have the current version and a density of comfortable or compact (or none). It MUST have at most 24 Widgets on 12 columns and at most 200 rows, with no two Widgets overlapping. Each Widget MUST have a unique id of lower-case letters, digits and hyphens up to 36 characters, and MUST name a Metric that exists in the Metric Catalog and can be placed. Its presentation, size, options and option values MUST be ones the catalog allows for that Metric, with at most 8 options of at most 32 characters each. The serialized layout MUST NOT exceed 32 KB. A planned Metric SHALL NOT be placed or saved.

#### Scenario: Valid layout is saved
- **GIVEN** a layout of placeable Metrics with allowed presentations, sizes and options, no overlap, on 12 columns
- **WHEN** the owner saves it
- **THEN** the save succeeds and the view is returned with its version

#### Scenario: Too many Widgets
- **GIVEN** a layout with more than 24 Widgets
- **WHEN** it is saved
- **THEN** the response is 400 with the code too_many_widgets
- **AND** the server does not check the individual Widgets

#### Scenario: Unknown or planned Metric
- **GIVEN** a Widget naming a Metric key that is not in the catalog, or a Metric that is only planned
- **WHEN** the layout is saved
- **THEN** the response is 400 with the code unknown_metric or metric_not_placeable

#### Scenario: Grid and size faults
- **GIVEN** Widgets that overlap, extend past column 12 or row 200, have a size outside the presentation's bounds, or use a presentation, option or option value the catalog does not allow
- **WHEN** the layout is saved
- **THEN** the response is 400 with the matching codes (overlap, outside_grid, size_out_of_bounds, presentation_not_allowed, option_not_allowed, option_value_invalid)

#### Scenario: Bad ids, version and density
- **GIVEN** a Widget id that is empty, too long, has other characters, or repeats, or a layout with a wrong version or density
- **WHEN** the layout is saved
- **THEN** the response is 400 with bad_widget_id, duplicate_widget_id, bad_version or bad_density

#### Scenario: Layout over the size cap
- **GIVEN** a layout that passes every other check but serializes to more than 32 KB
- **WHEN** it is saved
- **THEN** the response is 400 with the code layout_too_large

#### Scenario: Codes are returned, input is never echoed
- **GIVEN** a request whose name, description or layout holds known marker strings
- **WHEN** the save is refused
- **THEN** the response names the field and a code only
- **AND** neither the response nor captured logs contain the marker strings

### Requirement: Name and description bounds
A Saved View name SHALL be 1 to 80 characters after trimming, with no control characters, and unique among the owner's views in that Provider. A description MUST be at most 300 characters and MAY contain line breaks but no other control characters. An owner MUST have at most 100 views per Provider.

#### Scenario: Invalid name or description
- **GIVEN** an empty or over-long name, a name with a control character, or an over-long description
- **WHEN** the view is saved
- **THEN** the response is 400 with the code invalid_name or invalid_description

#### Scenario: Duplicate name
- **GIVEN** the owner already has a view with that name in the Provider
- **WHEN** they create another with the same name
- **THEN** the response is 409

#### Scenario: View count limit
- **GIVEN** the owner already has 100 views in the Provider
- **WHEN** they create another
- **THEN** the response is 409 and no view is created

### Requirement: Request body bounds
Requests that carry a Saved View, an invitation list or a redeem token SHALL be limited to a 64 KB body, enforced before the body is read and whether or not the client declares a length.

#### Scenario: Declared oversize body
- **GIVEN** a save request whose body is larger than 64 KB
- **WHEN** it is sent
- **THEN** it is refused before it is read

#### Scenario: Undeclared length
- **GIVEN** a request that declares no content length and streams more than 64 KB
- **WHEN** it is sent
- **THEN** it is cut off at the cap

### Requirement: Provider and ownership scope
Every Saved View route SHALL take the Provider from the route and check it against the caller's assignments on every request. A caller restricted to a Facility SHALL be treated as not having the Provider for Saved Views. A view MUST be loaded by Provider and then by ownership or share membership. A view that is missing, in another Provider, or neither owned by nor shared with the caller SHALL return 404, merging "not found" with "not visible".

#### Scenario: Cross-Provider access denied
- **GIVEN** a user assigned only to Provider A
- **WHEN** they list, read, create, update or delete Saved Views under Provider B
- **THEN** every call returns 404 and nothing is created or changed

#### Scenario: Another user's view
- **GIVEN** user X owns a view that is not shared with user Y, both in the same Provider
- **WHEN** Y reads, updates, deletes, or lists the sharing of it
- **THEN** each returns 404

#### Scenario: Facility-restricted caller
- **GIVEN** a user whose assignment is limited to a Facility
- **WHEN** they call any Saved View route for that Provider
- **THEN** the answer is 404, as if the Provider were not assigned

#### Scenario: Session without a portal user id
- **GIVEN** a session that has no portal user id
- **WHEN** it calls a Saved View route
- **THEN** the answer is 404

### Requirement: List, read, create, update and delete
The system SHALL let a caller with the dashboard view permission list and read their own views and views shared with them. A list SHALL return `mine` and `shared` summaries (id, name, description, owner name, ownership flag, widget count, update time) without layouts, each at most 200 and ordered by name. Reading a view SHALL return its layout, an ownership flag and an ETag. Create SHALL return 201 with the ETag. Update and delete SHALL be owner only. Delete SHALL remove the view with its recipients and pending invitations, and recipients lose access at once.

#### Scenario: Owner full cycle
- **GIVEN** an owner with the views permission
- **WHEN** they create, list, read, update and delete a view
- **THEN** each step succeeds and a read after delete returns 404

#### Scenario: List has no layouts
- **GIVEN** an owner with views and views shared with them
- **WHEN** they list
- **THEN** the response has mine and shared summaries and no layout

#### Scenario: Recipient cannot change a view
- **GIVEN** a recipient of a shared view
- **WHEN** they read it
- **THEN** the ownership flag is false
- **AND** an update, delete, invitation, share listing or revocation by them returns 404

#### Scenario: Delete removes access
- **GIVEN** a view shared with a recipient
- **WHEN** the owner deletes it
- **THEN** the recipient's read returns 404

### Requirement: Optimistic concurrency
An update SHALL require the ETag of the version being replaced in the If-Match header. A save without it MUST return 428. A header that is not a version the API issued MUST return 400. A stale version MUST return 412 and change nothing.

#### Scenario: Missing version
- **GIVEN** an owner saves a view
- **WHEN** the request has no If-Match header
- **THEN** the response is 428

#### Scenario: Stale version
- **GIVEN** the view was saved by another session after the owner loaded it
- **WHEN** the owner saves with the old ETag
- **THEN** the response is 412 and the stored view is unchanged

#### Scenario: Made-up version
- **GIVEN** an If-Match value that is not a number this API issued
- **WHEN** the owner saves
- **THEN** the response is 400 and nothing changes

#### Scenario: Current version succeeds
- **GIVEN** the owner's ETag is current
- **WHEN** they save
- **THEN** the response is 200 with a new ETag

### Requirement: Permissions
Creating, updating, deleting and sharing a Saved View SHALL require the views manage permission. Listing, reading shared views and redeeming an Invitation SHALL require only the dashboard view permission. All routes SHALL require an authenticated user.

#### Scenario: Anonymous refused
- **GIVEN** no session
- **WHEN** any Saved View route is called
- **THEN** the response is 401 or 403

#### Scenario: Viewer without manage
- **GIVEN** a user with the dashboard view permission but not views manage
- **WHEN** they create, update, delete or share a view
- **THEN** the response is 403
- **AND** they can still list and read views shared with them

#### Scenario: User without either permission
- **GIVEN** an authenticated user with neither permission
- **WHEN** they call any Saved View route
- **THEN** the response is 403

### Requirement: Sharing by emailed Invitation
An owner SHALL share a view by emailing Invitations to addresses they enter, at most 10 per request and one live Invitation per address per view. A new one for the same address replaces the old, whose link stops working. Each link MUST carry a random 32-byte token that works once and expires (7 days by default, 1 to 30 configurable). Only a hash of the token MAY be stored. The link MUST be built from the configured public https address, never from the request, and the email names the view and the sender only. A view MUST have at most 50 recipients and pending Invitations, and an owner MAY send at most 30 Invitations per hour. Invitation addresses MUST be validated and normalized, and a malformed address or list outside 1 to 10 MUST be refused with 400 and nothing sent.

#### Scenario: Owner invites
- **GIVEN** an owner of a view on a host with invitations enabled
- **WHEN** they send valid addresses
- **THEN** each address gets an email with a one-time link
- **AND** the response lists the Invitations sent with their expiry

#### Scenario: Re-inviting replaces the old link
- **GIVEN** an address with a pending Invitation
- **WHEN** the owner invites that address again
- **THEN** a fresh link is sent and the old link no longer works

#### Scenario: Bad invitation requests
- **GIVEN** an empty list, more than 10 addresses, or an invalid address
- **WHEN** the owner sends it
- **THEN** the response is 400 and no email is sent

#### Scenario: Recipient and rate limits
- **GIVEN** a view already at 50 recipients and pending Invitations, or an owner who sent 30 in the last hour
- **WHEN** the owner invites more
- **THEN** the response is 409 or 429 respectively

#### Scenario: Not the owner
- **GIVEN** a caller who is not the view's owner, or a Provider they are not assigned to
- **WHEN** they invite
- **THEN** the response is 404

#### Scenario: Email cannot be sent
- **GIVEN** the mail server is unavailable for an address
- **WHEN** the owner invites it
- **THEN** its Invitation is revoked at once and the address is returned as failed
- **AND** when no email could be sent the response is 503 and no live token remains

### Requirement: Invitations are off by default
Invitations SHALL be disabled unless a host enables them in its own environment configuration. While disabled, sending an invitation MUST return 503. Start-up MUST fail when invitations are enabled and the public address is not https outside Development, the mail host or sender address is missing, or the expiry is outside 1 to 30 days. Mail credentials MUST come from the secret store, and TLS to the mail server MUST always be on.

#### Scenario: Disabled host
- **GIVEN** a host that has not enabled invitations
- **WHEN** an owner sends an invitation
- **THEN** the response is 503 with a calm message

#### Scenario: Invalid enabled configuration
- **GIVEN** invitations are enabled with an http public address in a deployed environment, a missing sender or an expiry of 0 or 31 days
- **WHEN** the host starts
- **THEN** options validation fails and the host does not start

### Requirement: Redeeming an Invitation
Redeeming SHALL require a signed-in user with the dashboard view permission who is assigned to the view's Provider. A token MUST work once, until it expires, and not after the owner revokes it, and it MUST be consumed in one conditional update so two people racing for one link cannot both succeed. A successful redeem SHALL give read access to that view and return the Provider and view to open. Every failure (malformed, unknown, used, expired, revoked, out of scope, or a missing portal user id) SHALL return the same 404, and a cheap shape check MUST come before any database work. The redeem route MUST be rate limited. The session carries no email claim, so the invited address is not matched to the person who opens the link: the link is the credential.

#### Scenario: Whole journey
- **GIVEN** an owner sends an Invitation and a colleague in the same Provider opens the link
- **WHEN** the colleague redeems the token
- **THEN** they can read the view but not change it
- **AND** the owner's share list shows them as a recipient

#### Scenario: Token works once
- **GIVEN** a token already redeemed
- **WHEN** it is redeemed again by anyone
- **THEN** the response is 404

#### Scenario: Expired or revoked token
- **GIVEN** a token past its expiry or cancelled by the owner
- **WHEN** it is redeemed
- **THEN** the response is 404

#### Scenario: User outside the Provider does not burn the link
- **GIVEN** a signed-in user not assigned to the view's Provider
- **WHEN** they redeem the token
- **THEN** the response is 404
- **AND** the token is still usable by an eligible user

#### Scenario: Every failure looks the same
- **GIVEN** a malformed token, an unknown token, a used token, and a body carrying extra view or Provider ids
- **WHEN** each is redeemed
- **THEN** all return the same 404 and the extra ids have no effect

#### Scenario: Redeem needs authentication
- **GIVEN** no session
- **WHEN** a token is redeemed
- **THEN** the response is 401 or 403

#### Scenario: Concurrent redemption
- **GIVEN** two users redeeming one token at the same moment
- **WHEN** both are processed
- **THEN** exactly one succeeds

### Requirement: Listing and revoking shares and Invitations
The owner SHALL be able to list who can open a view, as recipients (with the address the Invitation went to and the share time) and pending Invitations (with expiry and whether it has expired). The owner MUST be able to remove a recipient, which ends their access, and cancel a pending Invitation, which makes its link stop working. Used, revoked and unknown identifiers return 404. Unused Invitations that expired or were revoked more than 30 days ago SHALL be removed whenever the owner sends a new Invitation.

#### Scenario: Owner lists sharing
- **GIVEN** a view with one recipient and one pending Invitation
- **WHEN** the owner lists shares
- **THEN** recipients and pending are returned

#### Scenario: Remove a recipient
- **GIVEN** a recipient of a view
- **WHEN** the owner removes them
- **THEN** the response is 204 and the recipient's read returns 404

#### Scenario: Cancel a pending Invitation
- **GIVEN** a pending Invitation
- **WHEN** the owner cancels it
- **THEN** the response is 204 and redeeming its token returns 404

#### Scenario: Identifiers from another view or Provider
- **GIVEN** a share or Invitation id that belongs to a different view, a different owner or another Provider
- **WHEN** the owner or another user tries to revoke it
- **THEN** the response is 404 and nothing changes

### Requirement: View builder and unsaved changes
The web app SHALL let a user build a view by adding Metrics from a palette of the Metric Catalog (planned Metrics listed as coming soon and not placeable), arranging Widgets on the 12-column grid, and setting each Widget's presentation, the options the catalog lists, size and position. Page filters SHALL be shared by all Widgets and apply on read, never saved. A recipient SHALL see a shared view as read only with a message naming the owner and an option to save their own copy. The app MUST ask before leaving a view with unsaved changes, including on browser unload. On a 412 the app MUST open a conflict dialog that starts on saving a copy. The invitation link landing page MUST remove the token from the address bar before using it and MUST check its shape before calling the server.

#### Scenario: Leave with unsaved changes
- **GIVEN** a view with unsaved edits
- **WHEN** the user navigates away
- **THEN** they are asked to confirm
- **AND** a clean view or any other page leaves without a prompt

#### Scenario: Stale save in the browser
- **GIVEN** the server answers 412 to a save
- **WHEN** the conflict is shown
- **THEN** a dialog offers choices with "Save as a copy" focused first and an assertive live-region announcement is made

#### Scenario: Recipient sees read-only view
- **GIVEN** a recipient opens a shared view
- **WHEN** it loads
- **THEN** the view shows who shared it, offers save a copy, and offers no edit of the original

#### Scenario: Token removed from the address bar
- **GIVEN** a user opens an emailed link
- **WHEN** the landing page loads
- **THEN** the token is removed from the address bar at once
- **AND** a token of the wrong shape never reaches the server

### Requirement: Accessible and responsive builder
The builder SHALL meet WCAG 2.2 AA. Every Widget MUST be placeable and sizable four ways without dragging: Add, pointer drag, keyboard Move mode, and a settings pane with steppers and nudge buttons. Keyboard Move mode MUST never trap focus (Tab or leaving the button cancels). Every change MUST be announced through live regions, with one message when filters make Metrics update and one when done. Focus MUST move predictably on add, remove, settings and save. Every chart MUST have a label of 250 characters or fewer and the same numbers in a table one control away. The page MUST work from 320 px to 1920 px, light and dark, at 200 percent text, 400 percent zoom, in forced colors and with reduced motion, with no sideways page scroll, one column below 56rem of canvas width in reading order, and targets of at least 24 px.

#### Scenario: Move without a pointer
- **GIVEN** a keyboard-only user on the canvas
- **WHEN** they enter Move mode and use arrow keys, then Enter or Escape
- **THEN** the Widget is dropped or returned, the ghost says in words where it will land, and Tab never traps

#### Scenario: Palette docks by width
- **GIVEN** a viewport of 75rem or wider
- **WHEN** the builder is shown
- **THEN** the palette is docked, and below that width the same palette opens in a dialog that stays open for several adds

#### Scenario: Chart has a table alternative
- **GIVEN** a Widget drawn as a chart
- **WHEN** a screen reader user reaches it
- **THEN** it has a label of at most 250 characters and a data table with caption and column headers

#### Scenario: Automated checks at every size
- **GIVEN** the builder, list and share pages
- **WHEN** the browser accessibility, layout and keyboard tests run at every size
- **THEN** there are no axe violations, overlaps, cut-off text or keyboard traps

## Open questions
- The invited address is not matched to the person who redeems a link, because the portal session has no email claim. Should redemption bind to the invited address once the portal supplies one?
- Invitations are purged only when the owner sends a new one. Is a scheduled purge needed?
- A Label never changes an API key, but per-viewer Label overrides in a shared view are not built. What Reporting Division wording should a recipient see?
- Reporting Division filtering reaches only some Metrics, and a Widget on another Metric says the filter was not applied. Should a Saved View ever store a division?
- Screen reader behavior of keyboard Move mode (NVDA, JAWS, VoiceOver, TalkBack) and touch long-press drag are not automated and still need manual passes.
- Not configurable in v1: per-Widget filters, custom titles, colours, auto-refresh, export and per-user defaults. Which are wanted?
- The first load of the shared UI library from this route grew the initial bundle toward its error budget. Should the library be split by entry point?
