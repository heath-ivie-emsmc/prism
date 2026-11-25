# Design: View Templates and Assignments

## A template is a layout, not data
- A View Template reuses the Saved View layout shape and the same validation against the Metric Catalog. It stores no
  owner, Provider, figure, Label or filter, matching ADR 0004. Reusing the validation means one definition of a
  valid layout.
- Rejected: a separate template schema (two validators to keep in step); storing a Provider on a template (breaks the
  "one definition for all Providers" goal and widens tenant risk).

## Lazy clone delivery
- Delivery happens when a user signs in or lists Saved Views, as a clone into their own Saved Views for the Provider
  in the route. It is idempotent per Assignment, user and Provider, and bounded per request.
- Why lazy: no fan-out job over all users, no user directory needed, and a user only receives what their own
  assignments let them see.
- Rejected: eager push to every user (needs a user list the portal does not yet provide and a large write burst);
  live reference for Saved Views (a customer could not own, reorder or delete it).
- Consequence: delivered copies are the user's. Revoking stops future deliveries only. Changing a template later does
  not rewrite delivered copies. This is the plan's decision, stated as a risk below.

## Empty-state widgets, informational report
- A Widget whose Metric does not apply to the viewer's Provider, Service Line or Facility scope is delivered anyway
  and renders the existing empty state. The pre-push report lists where widgets will not apply but never blocks.
- Why: availability varies per Provider and per user; blocking would make "all Providers" pushes unusable and
  filtering at delivery would silently hide the layout the author intended.
- Rejected: drop non-applying widgets at delivery (layout surprises, positions change); block the push.

## Confirmation counts, no second approver
- Each push shows Providers affected, widgets applying per Provider and the template version, then asks to confirm.
  The counts are aggregate. Audit records the actor, template, target kind and count.
- Rejected: second approver (the user decided against it); no confirmation (a mis-targeted push reaches every customer).

## Tenant and trust boundaries
- Delivery reads the Provider from the route and checks it against the user's assignments; it never trusts a Provider
  or owner in a body. The source-template reference is server-set.
- Reports are aggregate-only: counts by Provider and template, never user lists with patient or claim detail.

## Risks
- A bad template pushed to all Providers reaches every user on next sign-in and cannot be recalled from delivered
  copies. Mitigation is the confirmation counts and audit; recall is out of scope.
- Delivery on the sign-in path adds latency; the per-request bound and idempotence limit it.
- Per-Provider differences in Metric availability can produce many empty-state widgets; the informational report exists to make that visible.
