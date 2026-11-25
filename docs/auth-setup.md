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

`Auth:LoginReturnUrl` is for local development only. The portal sends the login token to the application's registered Login Redirect URL, which for a deployed stage app is the stage host.
To sign in from your own machine, list `http://localhost:4200/api/auth/sso/callback` under the application's Development Redirect URLs in the portal and set `LoginReturnUrl` to the same text.
The start endpoint then sends the user to the portal's `/login?appId=&returnUrl=&state=`. It must be an `http` loopback address, and a deployed host refuses to start with it set.

Other settings and their defaults:

| Setting | Default | Meaning |
|---|---|---|
| `Auth:RolePrefixes` | none | A portal role matches a Prism role only by its exact name or as `<prefix>__<name>`. The application id is always an accepted prefix. A role from another application never matches. |
| `Auth:MaxLoginTokenLifetimeMinutes` | 10 | Login tokens living longer than this are refused, so a token cannot outlive its replay entry. |
| `Auth:RateLimitPerMinute` / `Auth:CallbackRateLimitPerMinute` | 30 / 10 | Per client address. The second covers the routes that consume a login token. |
| `ForwardedHeaders:KnownProxies` / `:KnownNetworks` | none | `X-Forwarded-For` and `-Proto` are trusted only from these (addresses and CIDR ranges). With none listed they are ignored, so a caller cannot pick its own rate-limit key. Set these behind a load balancer. |
| `ConnectionStrings:Redis` | none | Shared login replay protection. Required in deployed environments. Development and tests fall back to an in-memory store. |

Deployed environments (everything except Development and Testing) also require https URLs everywhere, secure cookies, and a `PublicBaseUrl`.

Secrets, never in the repo (user secrets locally, AWS Secrets Manager when deployed; see [secrets.md](secrets.md) for how the loader is configured and what rotation reaches without a restart):

- `Auth:ApplicationPrivateKeyPem`: the PEM private key whose public half is registered with the portal for this application id.
- `Auth:SessionSigningKeyBase64`: at least 32 random bytes, base64. Rotating it ends every session, and with the loader enabled it takes effect without a restart.

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
  (`PrismUser` for sign-in, `InternalUser` and the four internal admin tier roles below). A role the catalog does not know grants nothing, and a user needs `prism.app` to sign in.
- Publish the catalog with the setup utility (below) after registering the application.
- **For the internal admin area (ADR 0009):** neither portal exposes group names (checked in the Admin-Portal-2020 and Account-Portal sources: `user/user-data` returns id, name, username, title, department and location; `user/rbac-data` returns roles, including roles a group grants; `user/assigned-companies` returns companies, including group-granted ones). So an employee is marked by a role: `InternalUser` (permission `prism.internal`). A portal admin attaches that role to the internal group in the portal's own screens, so every member receives it. An optional `Internal:GroupName` is still honoured if a portal ever reports groups. Later phases also need a per-Provider user list and a lookup of any user's access by id (for impersonation).
- **Internal Users and companies:** `user/assigned-companies` includes group memberships, and the internal group has every company. So Prism ignores the company list for anyone in the internal group (ADR 0009, decision 6). Staff start in `/admin`, and see customer figures only through the audited preview.
- The portal does not echo `state` today. That is why `AllowIdpInitiated` is on in Development and the confirmation page exists.
  Once the portal echoes `state`, turn it off.

## Internal admin access (`Internal` section)

An Internal User holds the `InternalUser` role (permission `prism.internal`), which a portal admin attaches to the internal group. `Internal:GroupName` is optional and used only if the portal reports group names. The result is resolved at sign-in into a server-set `internal` claim, so a request header, query string or body can never grant it.

Two checks are both required for `/api/v1/admin/*`: the internal marker, and an admin tier permission (`prism.admin.templates.author`, `.templates.publish`, `.customers.view`, `prism.admin.manage`, carried by the portal roles `InternalTemplateAuthor`, `InternalTemplatePublisher`, `InternalCustomerDataViewer`, `InternalAdminManager`). A tier without the group is dropped at sign-in and refused by the policy; the group without a tier opens only `GET /api/v1/admin/session`. No customer role carries a tier. An Internal User with `prism.admin.customers.view` may also read any Provider's aggregate Metric routes by Provider number, to lay out a View Template: it is audited once per session and Provider, and claim-level and Saved View routes ignore it. Like roles, group membership lives in the session for up to 8 hours.

## Customer Roles

The portal decides who may sign in and which Providers a user belongs to. What a customer user may do inside a Provider is a Customer Role held in Prism (ADR 0010).

- **Catalog:** fixed in `Prism.Application/Customers/CustomerRoles.cs`: `viewer`, `analyst`, `claims_reviewer`, `phi_reviewer`, `provider_admin`, `claims_admin`. Roles with claim or PHI access are restricted.
- **Session cookie:** carries `prism.app` and the admin tiers only. Customer permissions are looked up per request from the Provider in the route (cached 30 seconds, cleared on change). No role record, or an unreadable store, means no permission. `/api/v1/me` lists the permissions held in each Provider.
- **First sign-in:** the user is recorded in `provider_user` for each of their Providers. The role is the Provider's default role (`viewer` unless staff change it). Internal Users are never recorded.
- **Provider admins** (`prism.users.manage`) list and change the roles of their own Provider's users at `/settings/users`. They cannot change their own role, cannot grant a restricted role unless staff allow it, and cannot demote the last user who can manage users.
- **Staff** (admin manager tier) manage any Provider at `/admin/customers`: change roles, make the first admin, set the default role and the restricted-grant switch.
- **Audit:** each role change is written in the same transaction as the change (`customer_role.changed`, `customer_settings.changed`).
- **Login-only role:** `PrismUser` carries just `prism.app`. Give it to every customer user in the portal. The old `ProviderAnalyst`, `ProviderAdmin`, `PlatformSupport` and `SuperUser` roles are no longer in the catalog, so the next publish deletes them in the portal together with every assignment to them. A user whose only role was one of those cannot sign in until they hold `PrismUser`.
- **Portal publishing:** `AdminPortalSetup` publishes only the permissions a portal role carries (sign-in, the internal marker and the admin tiers), and the roles `PrismUser`, `InternalUser` and the four tier roles. Customer permissions are not published: they live in Prism.

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

## SAML (deferred)

Prism signs in through the Account Portal's signed login-token flow. SAML is not used and is not planned for the first release. What we know, so it
does not have to be rediscovered:

- The Account Portal is a SAML **identity provider** for outside applications, through the `Emsmc\Sdk\Saml` classes in
  [Admin-Portal-Integration-Module](https://github.com/EMS-Management-Consultants/Admin-Portal-Integration-Module) (`php/src/Sdk/Saml/`). It signs a
  response and posts it to an application's assertion consumer URL, either when the portal starts the sign-in or when the application does. It also
  serves IdP metadata. Each application is a database record (`external_application.provider_settings`) with an IdP entity id, SP entity id, ACS URL
  and NameID field; the signing certificate and key come from AWS Secrets Manager.
- The assertion carries only NameID, email (the username), first name and last name. It carries **no roles and no company or facility assignments**,
  so even with SAML Prism would still call `user/rbac-data` and `user/assigned-companies` for those.
- It does not help with letting a customer's own identity provider (Entra, Okta, a state system) sign users in. That makes Prism the service provider,
  which the portal does not do. If a customer asks, it plugs in behind the auth provider seam described in AGENTS.md and needs its own ADR.
