# 5. Needs attention: a catalog of checks over claim-level data

Status: accepted

## Context

The dashboard explains movements in cash with aggregate Findings. Customers also need to act on individual claims: denied claims, claims on hold, claims in collections. Those rows are claim-level data and can identify a patient's claim, so AGENTS.md rule 1 applies: a permission, masking or omission, and an audit record for every read.

## Decision

- **A catalog of Attention Checks** (`AttentionCatalog`): code, not a table. Each check has a key, a name and a row predicate. The tabs on the review page come from it. A new check is one entry and one fixed SQL rule; request text never reaches SQL.
- **A new permission, `prism.claims.view`**, not the reserved `prism.phi.view`. It gates the counts too, so a user without it cannot learn how many claims are flagged. The card on the Insights panel therefore appears only for users who hold it.
- **Every list and export read writes an `audit_entry` row** before the response leaves. A failed write fails the request. The row holds who, which Provider, which check, the period and the row count, never claim data.
- **Facility-restricted users are refused** (404), as on every Metric route. Filtering claims by Reporting Division needs a division mapping the warehouse table does not carry; it is a follow-up.
- **Findings stay aggregate.** The claim count and link are not part of the Insights response, which is cached and shared. The web app asks the counts route when the user holds the permission.
- **Send to CSE is a drawer in phase 1 and does nothing.** It lists what was selected. A real send needs a recipient directory, a delivery channel and a PHI review of what the message may contain.
- **The overlay layer sits above page chrome.** If a floating assistant button is added later, it must sit below the overlay layer or hide while a drawer is open, so it never covers the drawer's actions.

## Consequences

- Balance due stands in for the expected payment, and the filing deadline is an estimate from a per-payer-category rule, until the warehouse provides both. The page says so.
- The audit table is not partitioned yet. The DBA standard asks for partitioning of historical tables; the single-column primary key rule conflicts with a partition key, so this needs the DBA team before the table grows.
