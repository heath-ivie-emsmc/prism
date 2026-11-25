# Secrets Manager loader

Prism takes its secrets from AWS Secrets Manager when it runs in a deployed environment, and from user secrets on a developer machine. The loader reads them before the host is built, so options validation sees them, then refreshes them every hour while the host runs. The design comes from the M6 (NEMSIS) project's loader. Where Prism differs, this page says so.

Code: `src/Prism.Infrastructure/Configuration/`. Decision record: `docs/adr/0011-secrets-loader-and-feature-flags.md`.

## Turning it on

Nothing is fetched unless `Aws:SecretsLoader:Enabled` is `true`. Base `appsettings.json` ships it `false`, so local development never contacts AWS. A deployed environment turns it on in its own `appsettings.<Environment>.json`:

```json
"Aws": {
  "SecretsLoader": {
    "Enabled": true,
    "Region": "us-east-1",
    "Secrets": [
      { "SecretId": "prism/<env>/app", "AllowedKeys": [ "Auth:ApplicationPrivateKeyPem", "Auth:SessionSigningKeyBase64" ] },
      { "SecretId": "prism/<env>/database", "Format": "RdsPostgres", "TargetKey": "ConnectionStrings:Prism" },
      { "SecretId": "prism/<env>/warehouse", "Format": "Raw", "TargetKey": "ConnectionStrings:Warehouse" },
      { "SecretId": "prism/<env>/redis", "Format": "Raw", "TargetKey": "ConnectionStrings:Redis" }
    ]
  }
}
```

The ids and region above are placeholders. They are environment settings, so they belong in the per-environment file and never in base `appsettings.json`.

| Setting | Meaning |
|---|---|
| `Enabled` | Master switch. `false` constructs no AWS client at all. |
| `Region` | Required and non-blank when enabled. The loader does not fall back to the machine's ambient region. |
| `ReloadIntervalSeconds` | Refresh interval after startup. Default 3600. |
| `ServiceUrl`, `AccessKey`, `SecretKey` | A local emulator. Honoured only in Development and Testing. |
| `OnlyUseAwsSecrets` | Fetch every entry at startup even if its keys are already configured. Use it to prove a host runs on AWS values before removing another source. |
| `Secrets[]` | At most 20 entries. |

Each entry:

| Field | Meaning |
|---|---|
| `SecretId` | The Secrets Manager id. |
| `Optional` | If `true`, a not-found or access-denied failure is skipped with a warning. Nothing else is. |
| `Format` | `Json` (default, flattened to `A:B:C`), `Raw` (the whole text goes to `TargetKey`), `RdsPostgres` (an RDS credentials JSON becomes an Npgsql connection string at `TargetKey`). |
| `KeyPrefix`, `TargetKey` | Where a payload lands. |
| `Keys` | Explicit configuration key to JSON property map. Wins over the others. |
| `AllowedKeys` | Allowlist of keys the entry may produce. Required when the entry has neither `Keys` nor `TargetKey`, so a bundle nobody has read cannot write the whole configuration namespace. |

## What a secret may not do

A produced key that equals or starts with any of these is refused, and the host does not start: `environment`, `ASPNETCORE_ENVIRONMENT`, `DOTNET_ENVIRONMENT`, `Aws:`, `FeatureManagement:`, `FeatureFlags:`, `Serilog:`, `ForwardedHeaders:`, `Logging:`, `AllowedHosts`, `Internal:`, `Warehouse:SkipLoginCheck`. They change security or operating posture, so they are reviewed as code. The same list guards the break-glass override and the App Config freeform profile.

Bounds (AGENTS.md section 4): a secret over 64 KiB, with more than 200 keys, or a list of more than 20 entries is refused. Up to four fetches run at once, each retried (4 attempts, exponential backoff with jitter, throttling, 5xx and timeouts only) inside one 20-second budget for the whole load. On budget expiry the host fails and names the secrets that had not loaded.

## Failure behavior

- Startup: a required entry that cannot be fetched, is malformed, has no current `SecretString`, or breaks the key policy stops the host. The line carries the fixed marker `AWS Secrets Manager loader: FAILED`, so one log query finds every failure. Messages name secret ids, key names and AWS error codes, never a value or SDK message text.
- Refresh: a failed refresh keeps the complete last-known-good set. The next tick tries again. There is no restart.
- A secret that exists but has no `AWSCURRENT` version (what a half-finished rotation leaves) fails even when the entry is `Optional`.

## Precedence

Settings files, then environment variables, then the loader, then the App Config freeform profile, then the break-glass override. Later wins. At startup an entry whose keys are all already configured is skipped (unless `OnlyUseAwsSecrets`), but stays in the refresh plan, and the first refresh whose AWS value differs takes over and logs one line.

**Break-glass override.** Set environment variables named `PRISM_SECRET_OVERRIDE__<Key>` (`__` stands for `:`), for example `PRISM_SECRET_OVERRIDE__ConnectionStrings__Prism`. They are applied last, so they outrank the loader and App Config. They obey the same key denylist. Use them for an emergency, then fix the secret and remove them.

## What rotation reaches without a restart

The refresh replaces the loader's configuration layer once, and signals a change only if a value differs. These consumers read the current value on every use:

| Consumer | Behavior on rotation |
|---|---|
| Prism database (`ConnectionStrings:Prism`) | Each new `DbContext` uses the current string. Contexts already running finish on their own connection. EF keeps one pool per distinct string, so a rotation starts a new one and the old one drains idle. |
| Warehouse (`ConnectionStrings:Warehouse`) | A changed login is **proven read-only before any query uses it**, with the same catalog checks as startup, plus the connection-string rules (encrypted in a deployed environment). A login that can write, create or own anything, or cannot be checked, is refused and every query answers "warehouse unavailable" until the secret is fixed. The old pool never stands in. Queries running during the change finish on the pool they began on, which is closed when its last query ends. |
| Redis replay store (`ConnectionStrings:Redis`) | New sign-ins use the current connection. A missing value fails the sign-in instead of skipping replay protection. It never falls back to memory. |
| Session signing key (`Auth:SessionSigningKeyBase64`) | Read per token. **Rotating it signs every user out**, which is its purpose. An invalid new value fails closed: nothing is accepted until it is corrected. |
| Portal application key (`Auth:ApplicationPrivateKeyPem`) | Read for each token sent to the portal. |

At most two generations of a pooled resource stay alive. If a third rotation arrives while both are still in use, new work fails until one drains. A rotation must point at the same logical database, Redis store or warehouse: replay history and transactions cannot follow a switch to a different one. Keep the old credentials valid long enough for running work to finish.

## Local development

Leave the loader disabled and use user secrets, as `docs/auth-setup.md` and `docs/warehouse.md` describe. Tests never contact AWS: they use fake readers, and a local HTTP listener for the SDK wire tests.

## Differences from M6

- Section name and break-glass prefix are Prism's (`Aws:SecretsLoader` is the same; the prefix is `PRISM_SECRET_OVERRIDE__`).
- M6's worker-mode filter (`Modes`, `ModeKey`) and the warning about leftover chart variables were dropped: Prism has one host and no earlier loader to retire.
- Caps on secret size, key count and entry count were added (M6 had none in the new loader).
- Rotation covers what Prism holds (database, warehouse, Redis, signing keys). M6's EmSmart, Php forward and ruleset consumers do not exist here.
- The warehouse rotation re-runs the write-protection check, which M6 has no equivalent of.

## IAM

The task or pod role needs `secretsmanager:GetSecretValue` on the listed ids, and `kms:Decrypt` on the key if the secrets use a customer-managed key. Nothing else. It does not need `ListSecrets`.

## Rollout

1. Create the secrets and grant the role read access.
2. Add the `Aws:SecretsLoader` block to the environment file with `Enabled: true`.
3. Set `OnlyUseAwsSecrets: true` once and restart. Read the startup line for secret ids and key names, and the replaced-values line, to confirm AWS is the source.
4. Remove the other source (an injected variable or user secret) and set `OnlyUseAwsSecrets` back.

Rollback is setting `Enabled` to `false` in the environment file, or using a break-glass override while the secret is repaired.
