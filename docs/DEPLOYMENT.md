# Production demo deployment

The demo needs a backend host that runs .NET 10/Linux containers and a managed
SQL Server database. No hosting account, database, or public demo URL is currently
configured. The application image is built locally; the image smoke procedure
and its exact verification boundary are recorded in [Container verification](CONTAINER_VERIFICATION.md).
The separate [SQL integration suite](SQL_INTEGRATION_TESTS.md) exercises disposable
SQL Server containers; it does not verify the application Dockerfile or deployment.

## Container and configuration

For a registry release, follow [RELEASES.md](RELEASES.md). Private GHCR publication
is manual, tied to passing protected-main CI and environment approval. Deployment
must select a `verified` release's `ghcr.io/kevinasramoska/rentright@sha256:...`
reference and separately authorize an environment/migration job. The metadata/SBOM
is not deployment approval; no cloud host or database is provisioned by the release
workflow. First live publication/visibility verification requires the merged
workflow and owner approval; the local-registry rehearsal does not establish it.

For local development, use [LOCAL_COMPOSE.md](LOCAL_COMPOSE.md). Its separate
Development-only initialization job migrates only the dedicated Compose database;
it is not a production migration job or a reason to run public hosting in Development.

Build from the repository root:

```bash
docker build --build-arg VERSION=local --build-arg REVISION="$(git rev-parse HEAD)" -t rentright-demo .
```

The container runs as the built-in non-root app user and listens on HTTP 8080.
Its Node 24 stage builds React, its repository-matched .NET SDK stage publishes
Release, and only the published output enters the ASP.NET runtime stage. All
three base manifests are digest-pinned. The runtime uses .NET 10.0.13; this does
not change the SDK or NuGet versions installed on a developer machine.

Normal `dotnet publish` still installs/builds React. Only a pipeline that already
built `wwwroot/app` may use `/p:BuildReactOnPublish=false`; missing `index.html`
fails publication. Never bypass the build with unreviewed or stale assets.

Build arguments are public image metadata, not secret inputs. Supply runtime
configuration through the host's private settings, mounted secret files or
`docker run --env VARIABLE_NAME` after privately exporting that variable. Never
put credentials in build arguments, the Dockerfile, an image layer or a committed
environment file. The build context excludes local environment files, keys,
databases, test/build output and frontend dependencies.

To refresh a base image, inspect its supported tag in the upstream registry,
replace the tag/digest together, rebuild and repeat the smoke and vulnerability
scan. A digest intentionally does not auto-update. Keep the SDK matched to
`global.json`; runtime security patches can move within the .NET 10 line.

For disposable local verification (Docker, the repository SDK/EF CLI, curl and
OpenSSL required):

```bash
bash scripts/verify-container.sh rentright-demo
```

This creates and migrates only a fresh private SQL container, seeds one fictional
listing without a usable login, then checks the read-only application. It cleans
up its containers/network/temp files on exit, including failure. It never uses
the configured local database, a shared database, or an existing Docker volume.
SQL Server's amd64 Linux image requires Docker emulation on Apple Silicon.

The image needs a reachable, already-migrated SQL database at startup because
normal role seeding reads Identity tables. A container build alone is not a
readiness check. No database or migration-on-start behavior is baked into it.

Read-only execution needs writable temporary storage and a writable external key
ring owned by UID/GID 1654. The smoke uses `/tmp` and an ephemeral
`/home/app/.aspnet` tmpfs only. Do not use ephemeral, unencrypted keys for a public
deployment; provision the encrypted persistent key ring below. The runtime has
no Node, npm, .NET SDK or Git. No health endpoint/HEALTHCHECK is invented here;
operational health remains a later phase.

Configure TLS at the host, and set `ASPNETCORE_ENVIRONMENT=Production` and
`ConnectionStrings__DefaultConnection` in the host's secret settings. Use an
encrypted SQL connection with certificate validation; do not enable
`TrustServerCertificate` in production or commit credentials.

Before starting, apply reviewed EF migrations to an isolated demo database using
a separately reviewed migration job and a backup. A deployment migration job is
not supplied by P10-01. The disposable smoke uses EF's migrator, like the SQL
integration tests, against only its newly created container database.

Both raw SQL script variants failed fresh-database execution with invalid-column
compilation errors in historical DDL/backfill batches. Generating a script is not
proof it can execute: do not use those scripts as a verified bootstrap or replay
mechanism. Applied migration history was not rewritten to hide this limitation.
SQL command-line sessions also need quoted identifiers (`sqlcmd -I`) for existing
filtered indexes. Normal web startup does not migrate. The explicit
`--initialize-local` process mode requires Development, an opt-in permission and
the exact Compose SQL/database target; it exits after migrations and the existing
role/development-admin seed, without opening an HTTP listener.
Never copy a real tenancy database into the public demo.

## Identity and proxy requirements

- Provision demo users and their roles privately; production deliberately does
  not honor the development-only `SeedAdmin` settings. Do not run in Development
  merely to create a production administrator.
- Persist ASP.NET Core data-protection keys across deployments/instances so
  authentication cookies and antiforgery tokens survive restarts.
  The opt-in `Security:DataProtection` settings support a pre-existing external
  persistent key directory, stable application name and separately mounted valid
  RSA PFX. Explicit persistent keys in Production/Staging require certificate
  encryption. No settings means platform defaults, not verified durable storage.
  See [Browser security](BROWSER_SECURITY.md) for paths/secrets, permissions,
  backups, initial sign-in reset and the single-certificate rotation limitation.
- Configure trusted forwarded headers for the actual proxy and verify HTTPS
  redirects/cookies. Do not trust arbitrary forwarded headers from the internet.
  Account/write limits use the connection IP or authenticated user, not an
  untrusted forwarding header. Tune the defaults for shared NAT/proxies; per-process
  limits reset on restart and are not distributed or DDoS protection. Verify 429
  wait/retry behavior before promotion. Baseline headers are implemented; a full
  script/resource CSP and real-browser audit remain separate.
- Use synthetic properties, users, and tenancies and define a reset process
  before publishing shared demo credentials.

## Verification and promotion

Run the Release build and all tests. Verify a public property page, login,
Admin/Landlord/Tenant resource boundaries, and create a property through React.
It must submit to `/api/v1/properties`; the retained Razor form must still submit
to `/Properties/Create`. JSON clients continue using
`/api/v1/properties` with the existing CSRF and authorization requirements.
The API area separates route values so MVC link generation cannot choose the API
controller just because both controllers are called `PropertiesController`.

Record the deployed commit, test persistence after restart, and keep the previous
deployment plus a database backup for rollback. Add the live URL to the portfolio
only after these checks pass.
