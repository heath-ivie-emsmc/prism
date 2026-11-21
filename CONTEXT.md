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
The date through which a Provider's data in the warehouse is complete, recorded as a load watermark per Provider. It is shown with every Metric, along with when the data was last updated.
_Avoid_: As-of date, last refresh, freshness

**Service Line**:
The kind of transport service, either air or ground. Each Transport has one, and a Provider can have several enabled. It sets the default labels a Provider sees.
_Avoid_: Mode, business unit, product

**Transport**:
One patient movement by a Provider, recorded under exactly one Service Line and one Reporting Division. It is the main unit counted on the dashboard.
_Avoid_: Trip, run, call, mission

**Label**:
The display name a Provider sees for a renamable term, such as Reporting Division. It is set per Provider, with defaults by Service Line, and never changes the term's API key.
_Avoid_: Alias, custom field name, translation
