# OpenSpec: Prism

Behavioral specifications for Prism, written as a characterization baseline of what is implemented today, plus
proposed changes as deltas. They are the input for the product requirements document (PRD).

Conventions follow the M6 (Nemsis) project so the two repositories read alike.

## Layout

| Path | Role |
|------|------|
| `specs/<capability>/spec.md` | Canonical behavior of an implemented capability |
| `changes/<change-id>/` | A proposed change: `proposal.md`, `design.md`, `tasks.md`, delta specs under `specs/` |
| `COMPARISON.md` | Capability to source docs, ADRs and test areas, with a coverage mark |
| `config.yaml` | Project context and per-artifact rules |

## Spec format

Each `spec.md` contains `## Purpose`, `## Requirements` and a final `## Open questions` section.

```markdown
### Requirement: <Stable name>
The system SHALL ...

#### Scenario: <Name>
- **GIVEN** ...
- **WHEN** ...
- **THEN** ...
```

Rules: every requirement uses SHALL or MUST and has at least one scenario; denied, cross-Provider and adversarial
cases are scenarios too; requirement names are identity keys (rename with a RENAMED delta). Specs describe
behavior in CONTEXT.md vocabulary and carry no class names, SQL or file paths.

## Change workflow

1. Propose under `changes/<id>/` with delta ops `ADDED`, `MODIFIED` (copy the whole block, then edit), `REMOVED`, `RENAMED`.
2. Implement with tests in the same change (AGENTS.md definition of done).
3. Archive: merge the deltas into `specs/`.

If code changes without a change folder, either fix the code to match the spec or open a reconcile change.

## Capability index

Baseline (implemented):

| Spec | Focus |
|------|-------|
| `auth-and-sessions` | Portal login token, cookie session, permissions, Provider scope, revocation, rate limits |
| `metric-catalog` | Catalog, Metrics, Context Badge, Data Through, Drills, caching, bounds |
| `saved-views` | Layouts, validation, concurrency, sharing by Invitation |
| `claims-attention` | Attention Checks, claim-level reads, audit, paging, CSV export |
| `warehouse-access` | Read-only warehouse rules, SQL policy, startup check |

Proposed (see `changes/`): `internal-admin-access`, `view-templates`, `persona-onboarding`, `user-settings`,
`admin-impersonation`.
