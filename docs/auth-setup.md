# Sign-in setup

Prism signs users in through the EMSolutions Account Portal and then keeps its own HttpOnly cookie session. The flow is the
same one M6 uses: the API redirects to the portal, the portal sends back a short-lived signed login token, the API validates
it, exchanges its nonce for the user's profile and roles through the Admin Portal API, and issues a session cookie.

## Routes

| Route | Purpose |
|---|---|
| `GET /api/auth/sso/start?returnUrl=` | Sets a state cookie, remembers a safe return URL, redirects to the portal. |
| `GET /api/auth/sso/callback?loginToken=&state=` | Portal landing. With matching state it signs in. Without echoed state it shows a confirmation page, but only to a browser that holds the state cookie from its own `/start` (when `Auth:AllowIdpInitiated` is on). Otherwise it is refused, so a link carrying someone else's token cannot sign a victim in. |
| `POST /api/auth/sso/confirm` | Completes a confirmed sign-in. Only exists when `Auth:AllowIdpInitiated` is on. |
| `GET /api/auth/sso/logout` | Revokes the session, clears the cookie and redirects to the portal logout. |
| `GET /api/v1/me` | The signed-in user, their Providers (id, name, Facility count) and permissions. 401 without a session. |

Errors come back as `/login?error=<code>` with one of `sso_failed`, `sso_expired`, `access_denied`. The SPA maps codes to messages.

## Configuration (`Auth` section)

Base `appsettings.json` leaves every identifier empty, so a host with no environment file fails startup validation instead of
reusing another environment's portal. Per environment, set:

`ApplicationId`, `AccountPortalBaseUrl`, `AdminPortalApiBaseUrl`, `LoginTokenIssuer`, `AdminPortalAudience`,
`LoginTokenPublicKeyUrl`, `PublicBaseUrl` (the https origin of this app, used for the portal logout return URL) and `AllowIdpInitiated`.
`appsettings.Staging.json`, `.QA.json` and `.Production.json` carry the portal values from M6. They leave `ApplicationId` and
`PublicBaseUrl` out on purpose: whoever registers the application sets them, and until then the host refuses to start.

Other settings and their defaults:

| Setting | Default | Meaning |
|---|---|---|
| `Auth:RolePrefixes` | none | A portal role matches a Prism role only by its exact name or as `<prefix>__<name>`. The application id is always an accepted prefix. A role from another application never matches. |
| `Auth:MaxLoginTokenLifetimeMinutes` | 10 | Login tokens living longer than this are refused, so a token cannot outlive its replay entry. |
| `Auth:RateLimitPerMinute` / `Auth:CallbackRateLimitPerMinute` | 30 / 10 | Per client address. The second covers the routes that consume a login token. |
| `ForwardedHeaders:KnownProxies` / `:KnownNetworks` | none | `X-Forwarded-For` and `-Proto` are trusted only from these (addresses and CIDR ranges). With none listed they are ignored, so a caller cannot pick its own rate-limit key. Set these behind a load balancer. |
| `ConnectionStrings:Redis` | none | Shared login replay protection. Required in deployed environments. Development and tests fall back to an in-memory store. |

Deployed environments (everything except Development and Testing) also require https URLs everywhere, secure cookies, and a `PublicBaseUrl`.

Secrets, never in the repo (user secrets locally, AWS Secrets Manager when deployed):

- `Auth:ApplicationPrivateKeyPem`: the PEM private key whose public half is registered with the portal for this application id.
- `Auth:SessionSigningKeyBase64`: at least 32 random bytes, base64. Rotating it ends every session.

Local development:

```bash
cd src/Prism.Api
dotnet user-secrets init
dotnet user-secrets set "Auth:SessionSigningKeyBase64" "$(openssl rand -base64 32)"
dotnet user-secrets set "Auth:ApplicationPrivateKeyPem" "$(cat prism-localhost.pem)"
dotnet run
```

The API listens on `http://localhost:5080` in Development, which is where the web dev server proxies `/api`.

## Needed from the portal team

- Register the application ids (`prism-localhost`, and one per deployed environment) with the callback URLs and public keys.
- What the portal actually returns (confirmed against the dev portal): `user/user-data` has the user (`id`, `username`, names) and no
  roles. Roles come from `user/rbac-data?user_id=` (`assignments[].item_name`). Providers come from `user/assigned-companies?user_id=`
  (`{ "<companyId>": [facilityIds] }`, the union of direct assignments and group memberships, with RescueNet Facility ids) and names from
  `company/batch?from_id=<id - 1>`. Roles map to permissions through the catalog in `Prism.Application/Auth/Permissions.cs`
  (`ProviderAnalyst`, `ProviderAdmin`, `PlatformSupport`, `SuperUser`). A role the catalog does not know grants nothing, and a user needs `prism.app` to sign in.
- Publish the catalog with the setup utility (below) after registering the application.
- The portal does not echo `state` today. That is why `AllowIdpInitiated` is on in Development and the confirmation page exists.
  Once the portal echoes `state`, turn it off.

## Prism database

Needed to run the API: sign-in checks revoked sessions there. Set the connection string as a secret (never in the repo):

```bash
dotnet user-secrets set "ConnectionStrings:Prism" "Host=127.0.0.1;Port=5432;Database=prism;Username=...;Password=..." --project src/Prism.Api
```

Migrations run as a deliberate step, never at startup. The `dotnet-ef` tool version is pinned in `dotnet-tools.json`:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update --project src/Prism.Infrastructure --startup-project src/Prism.Api --connection "<connection string>"
dotnet tool run dotnet-ef migrations add <Name> --project src/Prism.Infrastructure --startup-project src/Prism.Api --output-dir Persistence/Migrations
```

Integration tests start their own PostgreSQL with Testcontainers (Docker must be running) and apply the migrations to it.
`DbaStandardTests` reads that database's catalog and fails if a table or constraint breaks the EMSMC DBA standard.

## Where state lives

- **Session cookie:** identity, roles and permissions only. Providers and Facilities are not in it: a user can have thousands of Facilities, and a removed assignment should stop working in minutes. `/api/me` reads them from the portal and remembers them for 5 minutes. If the portal cannot be reached the user has no Providers (closed), not an old answer.
- **Prism database:** revoked sessions. If it is down, signed-in requests fail closed.
- **Redis:** login replay protection (`SET NX` with an expiry), shared by every instance. If Redis is down, sign-in fails closed.
- **Memory:** company names (a day), user scope (5 minutes), rate-limit counters.

## Known gaps

- **Roles and permissions are in the cookie for up to 8 hours.** Removing a role in the portal does not take effect until the session ends or is revoked. Providers do refresh every 5 minutes.
- **No "revoke all of a user's sessions"** and no admin action that revokes one.
- **Rate-limit counters are per instance.** They move to Redis when API usage limits are built.
- **At most 2,000 companies per user** are read (a warning is logged when the limit is hit), and only the first 25 get names. A user with company-wide reach needs a Provider search, not a list.
- **The Content-Security-Policy is report-only** until the SPA has been seen clean under it in a browser.
- **Logout is a GET**, because the portal flow redirects through it. SameSite=Strict stops a cross-site page from revoking a session, but a link can still send a user to the portal logout.
- **The Docker image and CI workflow have not been run.** `docker/Dockerfile` needs BuildKit (`buildx`), which is not installed here, and `.github/workflows/ci.yml` has never run on GitHub.

## Tests that guard these rules

`Prism.Tests/Architecture/GuardTests` fails on: a regex with no timeout, a direct `UseNpgsql` call, clock or random-id calls in production code, layering, an unpinned workflow action, `http://` or secrets in deployed settings files, and a missing deployed-environment settings file.
`OpenApiSnapshotTests` fails when the generated OpenAPI document changes, until `Snapshots/openapi.v1.json` is updated on purpose (`PRISM_UPDATE_SNAPSHOTS=1`).
Web tests include an axe check (WCAG 2.1 A and AA, minus colour contrast, which jsdom cannot measure) of the login page, shell and dashboard.

## Publishing permissions and roles to the portal

`tools/Prism.Utility.AdminPortalSetup` posts the permission catalog and the core roles to the Admin Portal API. The request bodies
are built from `Permissions.All` and `Permissions.Roles`, the same lists sign-in uses, so the portal and the API cannot disagree.

```bash
# Look at what would be sent. Sends nothing and needs no key.
dotnet run --project tools/Prism.Utility.AdminPortalSetup -- --dry-run

# Print the public key to paste into the portal when registering the application
dotnet run --project tools/Prism.Utility.AdminPortalSetup -- --key-file ~/prism-localhost.pem --print-public-key

# Publish (permissions first, then roles; stops at the first failure)
dotnet run --project tools/Prism.Utility.AdminPortalSetup -- --key-file ~/prism-localhost.pem

# Check what the portal stored
dotnet run --project tools/Prism.Utility.AdminPortalSetup -- --key-file ~/prism-localhost.pem --get-permissions
```

Use `--environment stage` or `prod` with `--application-id` for deployed environments. The tool never prints the key or its token.
Change the catalog in `Permissions.cs`, then run the tool again: role versions are the time of the run, so each publish is newer.
