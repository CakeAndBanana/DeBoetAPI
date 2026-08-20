# DeBoetAPI

ASP.NET Core 10 Minimal API backed by PostgreSQL, containerized, with JWT auth built in.

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Minimal APIs |
| Database | PostgreSQL 17, EF Core 10 + Npgsql |
| Auth | ASP.NET Identity, JWT access tokens + rotating refresh tokens |
| Docs | Built-in OpenAPI + Scalar (`/scalar/v1`) |
| Containers | Docker Compose; chiseled non-root runtime image |
| TLS | Caddy reverse proxy, automatic Let's Encrypt certificates |
| Admin UI | Next.js 16 + React 19 + Mantine, server-side token handling |
| CI/CD | GitHub Actions → GHCR → SSH deploy |

## Prerequisites

- .NET 10 SDK
- Docker Desktop (needs WSL2 on Windows: `wsl --install`, reboot, then install Docker Desktop)
- `dotnet tool install --global dotnet-ef` (v10+)

## Run it locally for development

```bash
cp .env.example .env          # fill in POSTGRES_PASSWORD and JWT_SIGNING_KEY
docker compose up -d --build
curl http://localhost:8080/health/ready
```

Migrations are applied automatically on startup (`Database:MigrateOnStartup`).

To run the API on the host against a containerized database instead:

```bash
docker compose up -d db
dotnet user-secrets set "Jwt:SigningKey" "<48+ random chars>" --project src/DeBoetAPI
dotnet run --project src/DeBoetAPI
```

## API

All routes are under `/api/v1`. Full schema at `/scalar/v1` can be checked whenever running the application


### Login

`POST /auth/login` accepts **either an email address or a username** in its `email` field

### Token handling

Access tokens live 15 minutes, refresh tokens 30 days. Tokens are returned in the response
body — not cookies — so a browser SPA and a native app consume the same endpoints. Store the
access token in memory (web) or Keychain/Keystore (mobile).

Refresh is **rotating**: each call revokes the presented token and issues a new one. Replaying
an already-used refresh token is treated as a leak and revokes that user's entire active
chain, forcing re-login.

## API docs

`GET /openapi/v1.json` and the Scalar UI at `/scalar/v1`. On by default in Development;
elsewhere controlled by `OpenApi:Enabled` / `OPENAPI_ENABLED` — deliberately not tied to the
environment, so docs can be on in Production without developer exception pages coming too.


## Users, roles and passwords

There is no self-service password recovery anywhere in this system. An administrator resetting
a password **is** the recovery path, so the admin account matters.

### Roles

| | Admin | Editor |
|---|---|---|
| Read categories and items | yes (anonymous, in fact) | yes |
| Create and edit a category | yes | yes |
| Move, delete, secondary links | yes | yes |
| One category's history | yes | yes |
| The whole change feed (`/audit`) | yes | no |
| Manage users | yes | no |

The `Editor` policy is satisfied by **Editor or Admin**, so an admin is never locked out of
something an editor can do.

### The seeded administrator

On startup the API ensures the `Admin` and `Editor` roles exist and seeds one administrator —
once, when the database is first created, not on every boot:

| | |
|---|---|
| Username | `admin` |
| Password | `deboet` |
| Email | `admin@deboetapi.local` |

Change that password immediately after first login. It is the one account exempt from the
forced change below, because forcing it would lock out every existing deployment on upgrade.

### Creating accounts

`POST /auth/register` is **admin-only** — there is no public sign-up. In practice accounts are
created through `POST /api/v1/users` (or the panel's **Users** screen), which takes the roles
and an initial password and hands it to the new user directly.

### The forced password change

Any password an administrator sets — at creation or at reset — is temporary. The account
carries `MustChangePassword` until the user replaces it via `POST /auth/change-password`.

This is enforced by the **API**, not just the panel: while the flag is set the access token
carries a `must_change_password` claim, and both the `Admin` and `Editor` policies refuse it.
Such a token can do exactly three things — log in, read `/auth/me`, and change the password.

Changing or resetting a password revokes every refresh token for that account, so sessions
elsewhere die.

> **Known limit.** Revoking refresh tokens does not invalidate an access token that has
> already been issued, so a reset (or a role change) takes up to `Jwt:AccessTokenMinutes` —
> 15 by default — to be felt everywhere. Shorten that value if the window matters to you.

### Locking someone out

`POST /api/v1/users/{id}/disable` and `/enable` use Identity's lockout, so a departing editor
can be cut off without deleting an account the audit trail still points at. An admin cannot
disable themselves or strip their own `Admin` role — on a single-admin install that would lock
everyone out.

Every one of these actions is written to the change history with who did it and when. Passwords
are never recorded, not even in the summary.

## Admin panel

A Next.js app in [web/](web/) for managing the category tree, the accounts that may edit it,
and the change history.
Runs as its own container beside the API; Caddy serves it on `ADMIN_DOMAIN`.

```bash
cd web
cp .env.example .env.local     # API_BASE_URL + a 32-char SESSION_SECRET
npm install
npm run dev                    # needs Node 20+
```

### Tokens never reach the browser

The panel is a **backend-for-frontend**. Signing in posts to the panel's own server, which
exchanges credentials with the API and seals the JWT pair into an encrypted, `httpOnly`
cookie. Every API call is then made server-side, from a Server Component or a Server Action.
The browser only ever holds an opaque cookie, so a script injected into the page has nothing
to steal — and since the container reaches the API at `http://api:8080` over the compose
network, CORS never applies and requests never leave the host.

**Refreshing is serialised, and must stay that way.** Access tokens last 15 minutes and
refresh tokens rotate; presenting a consumed refresh token makes the API revoke the user's
entire chain. A dashboard issuing several requests at once would otherwise refresh several
times, replay a spent token, and sign the admin out at random. `refreshTokens()` in
[web/src/lib/api.ts](web/src/lib/api.ts) therefore shares one in-flight promise per token,
and keeps it briefly after settling so a request still carrying the old cookie joins it
rather than redeeming the spent token itself.

Refreshing happens in [the proxy](web/src/proxy.ts), not during render: Next.js only
allows writing cookies from the proxy, Route Handlers and Server Actions, so a refresh at
render time would rotate the token with nowhere to store it.

Non-admins are refused at sign-in rather than being let in to hit a 403 on every page. That
is a UX gate only — the API enforces the real check.

### Types come from the API

```bash
npm run gen:api    # regenerates src/lib/api-types.ts from /openapi/v1.json
```

Re-run it whenever the API changes; hand-written types drift silently.

### Working on Mantine inside Server Components

Two traps, both of which fail with `Element type is invalid` or `Functions cannot be passed
directly to Client Components`:

- **No dot notation.** Use `AppShellHeader`, `TableThead` and friends, not `AppShell.Header`
  or `Table.Thead` — a Server Component holds only a *reference* to a client component, so
  its sub-components are `undefined`.
- **No function props.** `component={Link}` has to live in a `'use client'` file.

## Logging

Serilog writes to the console **and** to a file that starts a new one each calendar day:

```
logs/deboetapi-20260817.log
logs/deboetapi-20260818.log
```

Thirty days are kept; older files are deleted as new days roll over. A single day is also
capped at 100 MB.


## Migrations

```bash
dotnet ef migrations add <Name> --project src/DeBoetAPI --output-dir Data/Migrations
```

Tables and columns are snake_case (including the Identity tables)

## Deployment

Push to `master` or a manual **Run workflow** — runs tests, publishes
`ghcr.io/<owner>/deboetapi-api`, then SSHes to the VPS, pulls, restarts, and gates the job on
`/health/ready` responding. Pull requests run the tests only.

The manual run would only trigger the tests, not the publishes, so you can run the tests on a PR without having to merge it first.

**VPS setup**
Install docker, create the app directory and fill in the env file with the correct values.

You would need to create a deploy user and add the public key to the `~/.ssh/authorized_keys` file of that user. 
private key should be added to the GitHub secrets as `VPS_SSH_KEY`.

Command to generate the key pair:
```
ssh-keygen -t ed25519 -C "github-actions@deboetapi" -f "$env:USERPROFILE\.ssh\deboetapi_deploy"
```

When you deploy the first time, you need to copy the the compose file and Caddyfile to the VPS with these commands:

commands to copy the files to the VPS (replace YOUR_VPS_IP with your VPS IP address):
```
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" compose.prod.yaml deploy@YOUR_VPS_IP:/srv/deboetapi/
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" -r caddy deploy@YOUR_VPS_IP:/srv/deboetapi/
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" .env.example deploy@YOUR_VPS_IP:/srv/deboetapi/.env
```

then run `docker compose up -d` once to create the containers. After that, the GitHub Actions workflow will handle the rest.

**GitHub secrets**
`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `VPS_APP_DIR`, held in the
Github environment, if you wanna change it. the dockerfile requests the secrets to be in the environment variable named 'OVHCloud'.


### TLS

Caddy terminates HTTPS and reverse-proxies to the API over the private compose network. It
obtains and renews Let's Encrypt certificates automatically.