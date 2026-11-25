# 11. Secrets loader with live rotation, and feature flags

Status: accepted

## Context

Prism's first Secrets Manager loader read secrets once at startup, so a rotated database password, signing key or warehouse login took effect only after a restart, and there was no way to switch a feature on for one Provider without a deploy. M6 (NEMSIS) already solved both, and AGENTS.md was adapted from it. We want the same behavior, adapted to Prism's rules.

## Decision

1. **One loader, per-entry options, strict key policy.** Secrets are declared per environment (`Aws:SecretsLoader:Secrets`). A secret may not set the environment name, the loader or flag settings, logging, proxy trust, internal access or the warehouse override. Bundles need an allowlist. Size, key and entry counts are capped.
2. **Hourly refresh with last-known-good.** A failed refresh changes nothing. A successful one replaces the loader's configuration layer once.
3. **Consumers read the current value.** The Prism database string is read per context, the Redis replay store and the signing keys per use, and the warehouse through a connection source that follows the secret.
4. **A rotated warehouse login is proven read-only before use.** It runs the startup write-protection check against the new login, plus the connection-string rules. A login that fails or cannot be checked makes the warehouse unavailable until fixed. The old pool is never kept as a fallback.
5. **Flags come from App Config through the agent sidecar**, or from settings files when the sidecar is off. Targeting is by Provider id and user id allowlists. Anything malformed is off.
6. **The freeform App Config profile is under the same key denylist as a secret.** It outranks the loader in precedence, so it cannot be allowed to set what a secret may not.
7. **The sidecar address may be plain http only to this machine.** The agent is in the same pod. Any other http address is refused at startup, and no `http://` URL is allowed in a deployed settings file.
8. **Break-glass:** `PRISM_SECRET_OVERRIDE__<Key>` environment variables apply last, under the same denylist.

## Trade-offs

- Live rotation means more moving parts than restart-to-rotate: leased resources, an options monitor on signing keys, and a verification step in the query path. The alternative is a restart for every rotation and every flag change, which the team did not accept for the warehouse and sign-in secrets.
- Rotating the session signing key ends every session. That is intended, and documented.
- An invalid rotated session key fails closed instead of keeping the old key. It avoids checking sessions against something that is not the secret, at the cost of an outage until corrected.
- A rotated Prism database string starts a new EF pool and the old one drains idle. Leasing a data source for each context would be tidier but conflicts with the guard that only the shared extension configures Npgsql.

## Consequences

- The App Config sidecar and IAM access for it, and `secretsmanager:GetSecretValue` on the listed ids, are infrastructure work. Until then deployed environments keep both features disabled in settings and nothing changes for them.
- The architecture tests gain two guards: only the secrets reader builds an AWS client, and only the host composition registers the loader and the sidecar.
- Deployed settings files must stay free of `http://` URLs; the sidecar's loopback default lives in code for that reason.
