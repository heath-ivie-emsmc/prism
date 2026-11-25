# Feature flags

Feature flags let Prism ship code switched off, turn it on for chosen Providers or users, and turn it off again without a deploy. The design comes from the M6 (NEMSIS) project. Flags come from AWS App Config, read through the App Config agent that runs beside the app, and from settings files when the agent is not used.

Code: `src/Prism.Application/FeatureManagement/` (what to ask) and `src/Prism.Infrastructure/FeatureManagement/` (where answers come from). Decision record: `docs/adr/0011-secrets-loader-and-feature-flags.md`.

## Asking a flag

Inject `IFeatureValueManager`:

```csharp
bool on = await flags.IsEnabledForProviderAsync("Pilot", providerId, cancellationToken);
bool onForUser = await flags.IsEnabledForUserAsync("Pilot", portalUserId, cancellationToken);
bool both = await flags.IsEnabledAsync("Pilot", providerId, portalUserId, cancellationToken);
```

Pass a Provider id taken from the route after the access check, or the portal user id from the session. Never take either from a body, query string or header. An unknown flag is off.

## Targeting

A flag in the App Config `feature-flags` profile has `enabled` and optional settings:

```json
{ "Pilot": { "enabled": true, "provider_allowlist": [101, 205], "user_allowlist": [88], "targeting_strategy": "or" } }
```

- With no allowlist the flag is on for everyone (when `enabled`).
- `provider_allowlist` and `user_allowlist` are lists of numeric ids. A request with no Provider (or no user) is not on the list.
- With both lists, `targeting_strategy` decides: `or` (default) means either list matches, `and` means both must.
- Anything malformed turns the flag **off for everyone**: a non-array list, a non-number inside it, an unknown strategy.

## Where flags come from

| Setting | Meaning |
|---|---|
| `FeatureManagement:Enabled` | `false` (the default): flags come from `FeatureFlags:<Name>: true/false` in settings. `true`: from App Config through the sidecar. |
| `FeatureManagement:Application`, `:Environment` | The App Config application and environment names. Per-environment settings. |
| `:FeatureConfiguration` | Profile with the flags. Default `feature-flags`. |
| `:AppSettingsConfiguration`, `:EnableDynamicAppSettings` | The freeform profile (default `appsettings`), merged into configuration. See below. |
| `:BaseUrl` | Default `http://localhost:2772`, the agent in the same pod. Only https, or http to this machine, is accepted. |
| `:PollInterval`, `:RequestTimeout`, `:StartupMaxWait` | 30 s, 5 s, 2 min by default. |
| `:RequireNonEmptyFeatureConfig` | Default `true`: an empty flag profile fails startup and `/ready`. |
| `:StartupFailureMode` | `FailClosed` (default) stops the host if the first load fails. `FailOpen` continues with no flags. |

Locally, put flags in user secrets or `appsettings.Development.json`:

```json
"FeatureFlags": { "Pilot": true }
```

Config-only flags are on or off. Allowlists need the sidecar.

## Startup and readiness

With the sidecar on, the agent's port is awaited, the flag profile is loaded (retrying up to `StartupMaxWait`), then flags refresh every `PollInterval`. A failed refresh keeps the last known flags. `/ready` includes feature management: it answers not ready until the first load finished, while the agent still answers on its port, and while the flag profile is not empty. The check is shared between callers and cached for a few seconds like the database check.

## The freeform profile is not a way around review

The freeform `appsettings` profile is merged into configuration, so it is restricted the same way a secret is. A profile that sets `environment`, `Aws:`, `FeatureManagement:`, `FeatureFlags:`, `Serilog:`, `ForwardedHeaders:`, `Logging:`, `AllowedHosts`, `Internal:` or `Warehouse:SkipLoginCheck` is refused whole, never partly applied. Responses over 1 MiB, and profiles with more than 500 entries, are refused too. A missing freeform profile (404) just means no overrides. A missing flag profile is fatal when the sidecar is on.

## Turning it on in an environment

Not yet done for any environment. Before setting `FeatureManagement:Enabled` to `true` in a deployed file:

1. Create the App Config application, environment and a `feature-flags` profile, and optionally an `appsettings` profile.
2. Add the App Config agent as a sidecar (or init container) in Prism's Helm chart, listening on port 2772, with an IAM role allowed to read those profiles.
3. Add `FeatureManagement:Application` and `:Environment` to that environment's `appsettings.<Environment>.json`, and set `Enabled` to `true`.

Do not put `BaseUrl` in a deployed settings file: the default is the loopback address, and the architecture guard rejects `http://` URLs there.
