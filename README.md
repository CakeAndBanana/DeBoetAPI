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


## The admin account

On startup the API ensures an `Admin` role exists and seeds one administrator:

| | |
|---|---|
| Username | `admin` |
| Password | `deboet` |
| Email | `admin@deboetapi.local` |

Please change the password immediately after first login. 
The admin account is **not** created on every startup, just the first time the database is created. 
The admin account can be used to create other users and assign them the `Admin` role.

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
ssh-keygen -t ed25519 -C "github-actions@deboetapi" -f "$env:USERPROFILE\.ssh\deboetapi_deploy"

When you deploy the first time, you need to copy the the compose file and Caddyfile to the VPS with these commands:

commands to copy the files to the VPS (replace YOUR_VPS_IP with your VPS IP address):
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" compose.prod.yaml deploy@YOUR_VPS_IP:/srv/deboetapi/
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" -r caddy deploy@YOUR_VPS_IP:/srv/deboetapi/
scp -i "$env:USERPROFILE\.ssh\deboetapi_deploy" .env.example deploy@YOUR_VPS_IP:/srv/deboetapi/.env

then run `docker compose up -d` once to create the containers. After that, the GitHub Actions workflow will handle the rest.

**GitHub secrets**
`VPS_HOST`, `VPS_USER`, `VPS_SSH_KEY`, `VPS_APP_DIR`, held in the
Github environment, if you wanna change it. the dockerfile requests the secrets to be in the environment variable named 'OVHCloud'.


### TLS

Caddy terminates HTTPS and reverse-proxies to the API over the private compose network. It
obtains and renews Let's Encrypt certificates automatically.