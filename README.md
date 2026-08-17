# DeBoetAPI

ASP.NET Core 10 Minimal API backed by PostgreSQL, containerized, with JWT auth built in.

## Stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10 / ASP.NET Core Minimal APIs |
| Database | PostgreSQL 17, EF Core 10 + Npgsql |
| Auth | ASP.NET Identity, JWT access tokens + rotating refresh tokens |
| Docs | Built-in OpenAPI + Scalar (`/scalar/v1`), opt-in outside Development |
| Containers | Docker Compose; chiseled non-root runtime image |
| TLS | Caddy reverse proxy, automatic Let's Encrypt certificates |
| CI/CD | GitHub Actions → GHCR → SSH deploy |

## Prerequisites

- .NET 10 SDK
- Docker Desktop (needs WSL2 on Windows: `wsl --install`, reboot, then install Docker Desktop)
- `dotnet tool install --global dotnet-ef` (v10+)

## Run it

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

All routes are under `/api/v1`. Full schema at `/scalar/v1` when running in Development.

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/auth/register` | — | Create account, returns token pair |
| POST | `/auth/login` | — | Credentials → token pair |
| POST | `/auth/refresh` | — | Rotate refresh token → new pair |
| POST | `/auth/logout` | — | Revoke a refresh token |
| GET | `/auth/me` | Bearer | Current user |
| GET | `/items` | Bearer | Paginated, owner-scoped (`?page=&pageSize=`, max 100) |
| POST | `/items` | Bearer | Create |
| GET/PUT/DELETE | `/items/{id}` | Bearer | Owner-scoped single-item operations |
| GET/PUT | `/items/{id}/categories` | Bearer | Read or replace an item's category assignments |
| GET | `/categories` | Bearer | Flat list (`?parentId=`, `?rootsOnly=`, `?search=`, `?includeInactive=`) |
| GET | `/categories/tree` | Bearer | Nested tree (`?rootId=`, `?maxDepth=`) |
| GET | `/categories/{idOrSlug}` | Bearer | Detail + breadcrumb + children + secondary parents |
| GET | `/categories/{id}/items` | Bearer | `?includeDescendants=true` |
| POST/PUT/DELETE | `/categories`, `/categories/{id}` | **Admin** | Create, edit, delete |
| POST | `/categories/{id}/move` | **Admin** | Re-parent, rewriting the subtree |
| POST/DELETE | `/categories/{id}/links[/{parentId}]` | **Admin** | Secondary placements |

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

`GET /openapi/v1.json` and the Scalar UI at `/scalar/v1`. On by default in Development, off
everywhere else — controlled by `OpenApi:Enabled`, **not** by the environment, so they can be
switched on in Production without also enabling developer exception pages.

The spec declares the JWT bearer scheme and marks which operations need it, so importing it
into Insomnia or Postman gives you working auth rather than 22 bare requests.

```
Import → URL → http://localhost:8080/openapi/v1.json
```

Log in via `POST /api/v1/auth/login` (`admin` / `deboet`), then put the `accessToken` in the
collection's Bearer field. Tokens last 15 minutes, so it is worth storing it as an environment
variable — or using Insomnia's response chaining (`{% response %}` → Login → `$.accessToken`)
so it re-authenticates itself.

### Docs in production

Set `OPENAPI_ENABLED=true` in `.env`. Caddy still refuses to proxy `/openapi*` and `/scalar*`,
so the docs remain reachable only on the API's loopback binding — get at them with a tunnel:

```bash
ssh -L 8080:127.0.0.1:8080 you@your-vps
# then open http://localhost:8080/scalar/v1
```

That keeps the default safe with no credentials to manage. To publish them instead, the
Caddyfile carries a ready-to-use `basic_auth` block — generate the hash with
`docker run --rm caddy:2-alpine caddy hash-password --plaintext 'your-password'`.

## The admin account

On startup the API ensures an `Admin` role exists and seeds one administrator:

| | |
|---|---|
| Username | `admin` |
| Password | `deboet` |
| Email | `admin@deboetapi.local` |

## Logging

Serilog writes to the console **and** to a file that starts a new one each calendar day:

```
logs/deboetapi-20260817.log
logs/deboetapi-20260818.log
```

Thirty days are kept; older files are deleted as new days roll over. A single day is also
capped at 100 MB.

## Tests

```bash
dotnet test
```

Integration tests boot the real application with `WebApplicationFactory` against a real
PostgreSQL instance — only configuration is overridden, so the DI graph, middleware pipeline,
migrations and generated SQL are all the ones production runs. They cover the auth flow
(including refresh rotation and reuse detection), owner isolation on `/items`, paging, health
probes, rate limiting, and that refresh tokens are never persisted in plaintext.

## Migrations

```bash
dotnet ef migrations add <Name> --project src/DeBoetAPI --output-dir Data/Migrations
```

Tables and columns are snake_case (including the Identity tables)

## Deployment

Push to `main` runs tests, publishes `ghcr.io/<owner>/deboetapi-api`, then SSHes to the VPS,
pulls, restarts, and gates the job on `/health/ready` responding.

**VPS setup** — install Docker, create an app directory, and place `compose.prod.yaml`, the
`caddy/` directory, and a `.env` filled in from `.env.example`.

**GitHub secrets** — `VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `VPS_APP_DIR`.

### TLS

Caddy terminates HTTPS and reverse-proxies to the API over the private compose network. It
obtains and renews Let's Encrypt certificates automatically — there is no certbot cron job and
nothing to remember. HTTP is 308-redirected to HTTPS, and HSTS, `X-Content-Type-Options` and
`Referrer-Policy` are set on every response.

Two prerequisites before the first `up`: `DOMAIN` must already resolve to the server's public
IP, and ports **80 and 443** must be open. Caddy proves domain control over both; if either is
blocked, certificate issuance fails and the site will not serve.