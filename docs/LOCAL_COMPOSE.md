# P10-02 — local Compose runbook

## What this provides

One command after private setup builds and starts the existing React/.NET app,
waits for SQL Server, runs a separate initialization job and serves local HTTPS.
The user story is: Identity sign-in → authorized React/API workflows → SQL data →
the same saved data and usable cookie after app/container recreation.

This is a **local Development environment**, not production hosting. Use only
fictional data. No cloud account, external identity provider, geocoding key or
email delivery is provisioned. SQL uses the existing integration-suite SQL Server
2022 CU14 image, now pinned to its upstream manifest. It is a compatibility
baseline for local work, not a production patch recommendation.

## Prerequisites and first start

- Docker Desktop with Linux containers, or Docker Engine with Compose. Lifecycle
  verification uses Compose's health/completion conditions and `up --wait`.
- The SDK selected by `global.json`, Git, OpenSSL, curl and Bash. Node/EF CLI are
  **not** required on the host to start Compose: the image builds React and the
  one-shot runtime job uses EF's migrator directly.
- Allow roughly 6 GB for Docker plus sufficient disk for SDK/SQL build layers.
  SQL has a 3 GB container limit and 2 GB engine limit; app/job each have a 768 MB
  limit. SQL Server's Linux image is amd64; Apple Silicon needs Docker emulation.
  Windows users need a suitable Linux/WSL shell; native Windows is not verified.

From the repository root:

```bash
bash scripts/local-dev.sh init
bash scripts/local-dev.sh up
```

`init` exports the SDK development certificate without modifying trust, generates
independent random SQL/admin passwords, and writes private settings beneath
`.local/compose/rentright-local/`. The `.env` is mode 0600 and its parent is 0700.
The certificate/key are exported privately; the certificate subdirectory alone
is then mounted read-only for the non-root container. Its files are readable in
that mount, but other host users cannot traverse the private parent directory.
These are unencrypted **development** keys; never reuse them for production.
An existing `.env` is never overwritten; retry may reuse a complete certificate
pair from an interrupted export. Nothing sources/evaluates the environment file.

Open the printed `https://localhost:7443/app/properties` URL. Read the generated
`.env` locally for the `admin@example.test` login and random password; do not paste
its contents into a chat/issue or screenshot. Keep the generated password format
when changing settings (no connection-string delimiters/quotes). `.env.example`
is documentation with intentionally empty secrets, not a runnable default.

If the browser does not already trust the SDK certificate, explicitly run:

```bash
dotnet dev-certs https --trust
```

You must handle any OS trust prompt yourself. Do not bypass a browser certificate
warning or disable TLS verification. The helper's HTTPS readiness check trusts
only the exported certificate and validates the localhost hostname. It reports
ready only after that host-side check succeeds.

## Normal lifecycle

```bash
bash scripts/local-dev.sh status
bash scripts/local-dev.sh logs
bash scripts/local-dev.sh restart
bash scripts/local-dev.sh stop
bash scripts/local-dev.sh up
```

`stop` removes this project's containers/networks but keeps its SQL and
data-protection volumes. `restart` restarts the app without running migrations.
Use `up` to build/apply changed application code. Initialization completes before
the web container starts; it is a distinct process, not migration-on-web-startup.
Normal role checks remain in web startup; the Compose web service receives no
admin seed password or initialization permission. The initial seed is only the
existing three roles and one development admin, not a fabricated tenancy dataset.

For an explicit migration/admin-initialization run:

```bash
bash scripts/local-dev.sh migrate
```

This builds the current initialization image, starts/waits for local SQL and runs
the job once. Stop the app before schema-changing updates, then use `up` after
the job; do not point it at shared data. The process rejects Production/Staging/
Testing, missing permission, any SQL host other than `sql,1433`, and any database
other than `RentRightLocal` before making database calls. Normal processes do not
run EF migrations merely because a configuration flag is present: the explicit
`--initialize-local` command is required too. Existing applied migrations remain
unchanged; the EF migrator avoids the historical raw-script compilation issue
recorded in [DEPLOYMENT.md](DEPLOYMENT.md).

## Persistence, isolation and limits

- Project-scoped `sql-data` and `data-protection` named volumes survive normal
  restarts/down/up. Persistent local keys keep Identity/antiforgery protection
  usable across app recreation. Development deliberately permits unencrypted
  local key rings; production still requires the existing encrypted external
  key-store configuration.
- SQL and the initializer use an internal network; SQL has **no host port**. The
  app also joins a separate web network so its HTTPS port can be published only
  on `127.0.0.1`. It can make outbound requests on that web network; geocoding is
  explicitly off. Loopback binding is not a production firewall or multi-user
  security boundary.
- App/job run as UID 1654 with a read-only root, writable `/tmp`, all Linux
  capabilities dropped and no-new-privileges. The key-ring volume is owned by
  that UID. SQL retains only `NET_BIND_SERVICE`, required by the upstream
  executable's file capability, and otherwise drops capabilities.
- SQL health runs a real `SELECT 1`. The app healthcheck checks its startup TLS
  socket; the helper separately verifies the HTTPS session API. These are not
  ongoing HTTP probes. Separate `/health/live` and `/health/ready` endpoints are
  available and exercised by the lifecycle smoke, but the Compose health command
  still checks only the TLS socket. See [RUNBOOK.md](RUNBOOK.md) for the distinction
  and the remaining probe-wiring work.
- The local web connection uses SA and `TrustServerCertificate=True` on the
  isolated SQL network. Do not copy this configuration into production; provision
  least-privilege runtime credentials and certificate validation there.
- Do not print resolved `docker compose config` output or container environments:
  they contain local secrets. Use `config --quiet` for validation.

## Ports and parallel worktrees

Choose a different HTTPS port without changing tracked files:

```bash
RENTRIGHT_HTTPS_PORT=7444 bash scripts/local-dev.sh up
```

Use the same port override on subsequent commands. Port `0` requests a random
loopback port (used by the automated smoke). Different worktrees must choose
different project names and ports, for example `RENTRIGHT_COMPOSE_PROJECT=rentright-ui`
and `RENTRIGHT_HTTPS_PORT=7444`. Set these consistently on every command; each
project gets separate private settings, images, networks and volumes. The helper
requires the `rentright-` prefix. Browser cookies are host-scoped, not port-scoped;
use separate browser profiles for simultaneous logins to different local projects.

`RENTRIGHT_LOCAL_CONFIG_DIR` may select an absolute private configuration directory
(also used by tests); otherwise settings live beneath this checkout. Never choose
a shared/public directory. Preserve settings and volumes together: generating a
new SQL password does not change a password already stored in an existing SQL
volume. Back up the private configuration securely; `init` does not rotate it.
If the certificate expires, re-export the SDK certificate into this same private
certificate directory and preserve credentials/permissions; trust changes remain
an explicit user action.

## Explicit destructive reset

After backing up anything you want to keep, confirm the exact project:

```bash
bash scripts/local-dev.sh reset --confirm rentright-local
```

Without that exact confirmation, nothing is deleted. The helper verifies volume
project/type labels, then removes only that project's containers/networks and its
SQL/authentication-key volumes. This permanently removes local listings, users,
history and authentication keys; recovery requires your own backup. It does not
remove private settings/certificates, unrelated Docker resources, a host database
or another project's data. `up` after reset creates a fresh database/admin from
the retained private settings. No broad Docker prune/reset command is used.

## Repeatable verification

With Node 24 in addition to the start prerequisites:

```bash
node scripts/verify-compose.mjs
```

The script creates a random, previously absent `rentright-verify-*` project and a
private temporary configuration directory. It tests missing secrets, private
file permissions, refusal to overwrite credentials, no SQL host port, clean
startup/migration/admin seeding, real HTTPS/Identity login, Secure cookies,
antiforgery rejection and an authenticated API property save. It checks the saved
listing and existing cookie after app restart and full down/up, repeatable
migration/seed, graceful service shutdown, refused unconfirmed/wrong-project
reset and confirmed removal of only its test volumes. Secrets stay in memory or
private files, never test output. Cleanup owns only the generated test project
and directory; it refuses to adopt pre-existing resources. CI runs the same smoke
after the normal .NET/frontend checks, with a bounded step timeout.

Set `RENTRIGHT_COMPOSE_VERIFY_PAUSE=1` for a 60-second real HTTPS browser preview
before cleanup. No response interception or authentication bypass is installed.
Production deployment and ARM64/native Windows execution are not claimed by a
local amd64/macOS result; CI supplies the separate Linux result.

Locally verified on 2026-10-18 with Docker Compose 5.6.0/Linux amd64 containers
on macOS: the complete lifecycle smoke exited successfully. Release build had
zero warnings/errors, all **596 backend tests** passed (including 13 new guard
cases), and EF reported no pending model changes. No frontend source/packages or
schema changed. A background browser used the real HTTPS Identity page to sign
in with the generated local-only admin, created a fictional listing through
React, and displayed its saved manager detail. It then signed out; its tab closed
and the temporary viewport override was reset. No certificate warning was
bypassed, and no response fixtures were installed. An ignored local screenshot
records the successful save; the earlier timed preview cleaned itself up during
a detail check, so the completed browser journey used the separate stable test
instance instead.

Verification exposed and resolved a quoted-tmpfs YAML issue, the upstream SQL
binary's required file capability, and host-port publication on an internal-only
app network. Host-side HTTPS readiness now prevents a socket-only healthy state
from being reported as a usable local URL. Certificate export initially failed
but succeeded on diagnostic retry; setup keeps export permissions private and
can reuse a complete pair without replacing credentials. Applied migration
history and production security defaults were not changed.

## Files changed

| File | Purpose |
| --- | --- |
| `compose.yml` | SQL readiness, one-shot initialization, non-root HTTPS app, private networking and persistent volumes. |
| `.env.example` | Empty-secret configuration contract, never working default credentials. |
| `scripts/local-dev.sh` | Private local setup, validated HTTPS startup, lifecycle and exact-project reset confirmation. |
| `scripts/verify-compose.mjs` | Isolated real TLS/auth/API/SQL lifecycle, persistence and reset checks. |
| `Data/LocalDatabaseInitializer.cs` | Fail-closed Development/permission/database-target validation. |
| `Program.cs` | Explicit initialization mode, migrations before existing seed, exit without HTTP; ordinary startup unchanged. |
| `RentRight.Tests/Authorization/LocalDatabaseInitializerTests.cs` | Thirteen deterministic initialization safety regressions. |
| `Dockerfile` | Prepare the UID-1654-owned external key-ring mount point. |
| `.gitignore` | Exclude local private setup/env files while tracking the empty example. |
| `.dockerignore` | Exclude private setup and local Compose configuration from image context. |
| `.github/workflows/ci.yml` | Run the isolated Compose lifecycle smoke with a timeout. |
| `README.md` | Current local-start entry and honest remaining roadmap work. |
| `docs/DEPLOYMENT.md` | Distinguish guarded local initialization from production migration/deployment. |
| `docs/TASKS.md` | P10-02 implementation scope and verification. |
| `docs/LOCAL_COMPOSE.md` | Lifecycle/safety runbook, reproduction and complete file inventory. |
