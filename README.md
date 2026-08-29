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

### Token handling

Access tokens live 15 minutes, refresh tokens 30 days. Tokens are returned in the response
body — not cookies — so a browser SPA and a native app consume the same endpoints. Stores the
access token in memory (web) or Keychain/Keystore (mobile).

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

Any password an administrator sets at creation or at reset — is temporary. 
The user would have to set up a new password on the initial login

## How a slug is made

The slug is a category's own segment of the route; the full route joins the segments with a
hyphen, so a **derived** slug never contains one.

Derived from the name when the field is left blank:

| Name | Slug |
|---|---|
| `Christmas Light` | `christmas_light` |
| `Christmas Light's` | `christmas_lights` |
| `Kerst & Verlichting!` | `kerst_verlichting` |
| `Café Déjà Vu` | `cafe_deja_vu` |
| `Größe Blåbær` | `grosse_blabaer` |

Spaces become underscores; everything else that is not a letter or digit disappears, so an
apostrophe closes a word up rather than splitting it. Accents are folded to plain ASCII,
because a slug ends up in a URL and `caf%C3%A9_d%C3%A9j%C3%A0_vu` is unreadable, untypeable and
unshareable. Letters with nothing to strip — ß, ø, æ — are spelled out first, since they would
otherwise vanish entirely.

Typed by hand, a slug keeps hyphens **and** underscores and everything else becomes a hyphen —
you are spelling a route deliberately. The underscore has to survive here too: the panel
submits the stored slug on every save, so folding it back to a hyphen would silently move a
category's route each time somebody edited its name.

## Category redirects

A category can forward to another one. It is for a node that exists to organise the tree but
has nothing of its own to show: *Christmas Decorations* groups things, so its address should
take a visitor to *Christmas Trees* rather than a bare listing.

Set `redirectToId` when creating or updating a category. `GET /api/v1/categories/{idOrSlug}`
then returns the destination already resolved, so a site can answer in one request:

```jsonc
{
  "category":   { "name": "Christmas Decorations", "pathSlug": "christmas-christmasdecorations" },
  "redirectTo": { "name": "Christmas Trees",       "pathSlug": "christmas-christmastrees" }
}
```

## Admin panel

A Next.js app in [web/](web/) for managing the category tree, the accounts that may edit it,
and the change history.

```bash
cd web
cp .env.example .env.local     # API_BASE_URL, a 32-char SESSION_SECRET, SITE_URL
npm install
npm run dev                    # needs Node 20+
```

Each category's route in the tree links to its live page on `SITE_URL`
(`https://www.deboet.nl` by default). It is read on the server and passed down, so pointing the
panel at staging is an environment change rather than a rebuild.

### Live updates

Every panel subscribes to a change feed, so an edit made on a phone appears in a browser
without anyone reloading. The API publishes one event per recorded change from
[ChangeStream](src/DeBoetAPI/Common/ChangeStream.cs); the panel proxies that stream through its
own server — `EventSource` cannot send an `Authorization` header, and the token must not reach
the browser anyway — and calls `router.refresh()` when something arrives. Half-typed forms, the
search box and collapsed branches all survive the refresh.

Who hears what:

| Change | Goes to |
|---|---|
| Category or item created, edited, moved, deleted | everyone signed in |
| Password reset, roles changed, account disabled | only that account's own sessions |

The taxonomy is shared work, so a move is everyone's business. An account is not: a password
reset needs to reach the sessions it happened to, and fanning it out would announce to every
open panel whenever anyone's password changed.

Server-sent events rather than websockets: the traffic only ever goes one way, SSE is plain
HTTP so it needs no protocol upgrade, and the browser reconnects on its own. Caddy passes it
through unbuffered and declines to compress it, so no proxy configuration is needed.

**One limit worth knowing.** The fan-out is in-process, so it reaches only the clients
connected to the API container that handled the change. That is every client today — one
container serves this deployment — but a second replica would need Postgres `LISTEN`/`NOTIFY`
or a broker behind `ChangeStream.Publish`.

### Checks

```bash
cd web
npm run typecheck    # tsc --noEmit
npm run lint         # eslint
npm run test         # vitest run
npm run test:watch   # vitest, watching
```

Unit tests cover the panel's pure modules — the tree maths behind search and drag-and-drop,
the JWT claim reading that decides what a session may see, translation lookup, and the parent
picker's flattening. Deliberately not component tests: what these cover goes wrong silently,
whereas a broken button is obvious the moment you open the page.

All three run in CI on every push and pull request, on a GitHub-hosted runner rather than the
self-hosted one, which is set up for .NET and has no Node. Type errors already fail
`next build`; lint and the tests were enforced nowhere before.

### Adding a language

The panel's interface is translated from one file per language in
[web/src/i18n/locales/](web/src/i18n/locales/). To add one:

1. Copy `en.json` to `<code>.json` — `de.json`, `fr.json` — and translate the **values**,
   leaving every key exactly as it is. `{placeholders}` are filled in at render time, so keep
   them where they are.
2. Register it in [web/src/i18n/index.ts](web/src/i18n/index.ts):

   ```ts
   import de from './locales/de.json';

   export const LOCALES = {
     en: { label: 'English', dictionary: en },
     nl: { label: 'Nederlands', dictionary: nl },
     de: { label: 'Deutsch', dictionary: de },
   } as const;
   ```

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