# Prism

Prism is the revenue cycle management (RCM) business intelligence dashboard for ambulance and fire personnel. It serves air medical and ground transport customers from a shared data warehouse.

## Language

**Provider**:
The customer organization whose data Prism shows. It is the tenant and the boundary for data isolation and API access. It is the same thing as a billing organization in the Account Portal.
_Avoid_: Company, agency, client, customer, billing organization

**Reporting Division**:
The operational unit within a Provider that transports roll up to, and the dimension users filter by. Air Providers call it a base. Each Provider can set its own display label, but the concept and its API key stay the same.
_Avoid_: Base, base location, reporting unit, station

**Metric**:
A named, defined measure such as net cash collected or loaded miles, with a unit and the dimensions it can be broken down by. Metrics are defined once in the metric catalog, and dashboard cards and the public API both read from it.
_Avoid_: KPI, measure, statistic, report column

**Metric Catalog**:
The single set of Metric definitions owned by the Prism API. It applies Provider scoping to every query.
_Avoid_: Semantic layer, dashboard config

**Data Through**:
The load date of a Provider's data in the warehouse: the one stamp shown with every Metric. It is a date, not a time of day.
_Avoid_: As-of date, last refresh, freshness, last updated

**Service Line**:
The kind of transport service, either air or ground. Each Transport has one, and a Provider can have several enabled. It sets the default labels a Provider sees.
_Avoid_: Mode, business unit, product

**Transport**:
One patient movement by a Provider, recorded under exactly one Service Line and one Reporting Division. It is the main unit counted on the dashboard.
_Avoid_: Trip, run, call, mission

**Finding**:
One computed statement about why a Metric moved, such as unusually high aged payments or a shift in payer mix. It has a dollar impact, a basis (posted cash or expected effect) and the counts or shares behind it. A Finding is built from Metrics and never carries patient or claim detail.
_Avoid_: Alert, anomaly, recommendation

**Driver**:
A Finding whose dollar impact counts toward explaining a change in posted cash. Drivers plus the remainder add up to the change. A Finding on another basis, such as the expected effect of a payer mix shift, is shown beside the Drivers and is not added to them.
_Avoid_: Cause, factor, reason

**Insight**:
The written summary of the Findings for one view and period: a headline, the ranked Findings and what is left unexplained. It is shown on the dashboard only.
_Avoid_: AI summary, analysis, report

**Attention Check**:
One named rule over a Provider's claims that flags the ones needing customer action, such as Denied (a denial code and money still owed) or Needs attention (denied, on hold or in collections). The checks are the tabs on the review page. A count of flagged claims is an aggregate; the claims behind it are claim-level data that need the claims permission and leave an audit record.
_Avoid_: Alert, exception, work queue

**Drill**:
A link from one element of a Metric, such as a payer or a month, to a more specific Metric view. It opens a new page and never changes the view the user is on.
_Avoid_: Drill-through, detail link, zoom

**Context Badge**:
The scope a Metric actually applied, shown beside its figure: the Service Line and the Reporting Division. It says so when a filter the user chose was not applied.
_Avoid_: Filter chip, scope label, tag

**Label**:
The display name a user sees for a renamable term, such as Reporting Division. A Provider sets it for everyone, a user can override it for themselves, and the defaults come from Service Line. A user's own Label wins over the Provider's, which wins over the Service Line default. Exports use the Provider's name. A Label never changes the term's API key.
_Avoid_: Alias, custom field name, translation

**Saved View**:
A dashboard layout a user arranged and saved: which Metrics, drawn which way, and where on a 12-column grid. It belongs to one user in one Provider and can be shared by emailed invitation with other users who have access to that Provider. It stores Metric keys and positions only, never figures, Labels or filter values, so it always shows the data its viewer is allowed to see.
_Avoid_: Dashboard, report, layout (as a name for the thing a user keeps). A layout that internal staff author is a View Template, not a Saved View.

**Widget**:
One Metric placed on a Saved View, with its presentation (line, column, table, tile and so on), its options and its place on the grid. The same Metric can appear twice.
_Avoid_: Card, tile (a tile is one presentation of a Widget), panel

**Invitation**:
An emailed, time-limited, single-use link that gives one signed-in user read access to a Saved View. Only a hash of its token is stored.
_Avoid_: Share link, invite code

**Internal User**:
A person marked internal by the `InternalUser` portal role (attached to the portal's internal group) who may enter the admin area. What they can do there depends on a separate tier permission (template author, template publisher, customer data viewer, admin manager). The group alone grants nothing, and a tier without the group grants nothing.
_Avoid_: Admin, staff, employee, super user

**View Template**:
A layout of Widgets authored by Internal Users: Metric keys, presentations and grid positions, with no owner, no Provider and no figures. It reaches customers by an Assignment, either as a clone in a user's Saved Views or as a Persona's home page.
_Avoid_: Saved View (that is a user's own), starter view, default dashboard

**Assignment**:
A decision to push a View Template to all Providers, to specific Providers, or to a Persona. It delivers a clone to each matching user the next time they sign in. Revoking it stops future deliveries and never changes copies already delivered.
_Avoid_: Push, publication, rollout

**Persona**:
The kind of user someone is, such as air chief or finance, worked out from a short first-login questionnaire and stored once per user. It selects a home page by pointing to a View Template. Users are not shown their Persona on pages.
_Avoid_: Role (that is a portal term), segment, user type

**Customer Role**:
A named set of permissions a customer user holds in one Provider, kept in Prism. The portal only decides who may sign in and which Providers a user belongs to. Roles that carry claim or PHI access are restricted: staff grant them, or a Provider admin may once staff allow it.
_Avoid_: Portal role (that is a portal term), group, access level
