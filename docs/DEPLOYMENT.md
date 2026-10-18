# Production demo deployment

The demo needs a backend host that runs .NET 10/Linux containers and a managed
SQL Server database. No hosting account, database, or public demo URL is currently
configured. Application-container execution has not been verified locally.
The separate [SQL integration suite](SQL_INTEGRATION_TESTS.md) exercises disposable
SQL Server containers; it does not verify the application Dockerfile or deployment.

## Container and configuration

Build from the repository root:

```bash
docker build -t rentright-demo .
```

The container runs as the built-in non-root app user and listens on HTTP 8080.
Configure TLS at the host, and set `ASPNETCORE_ENVIRONMENT=Production` and
`ConnectionStrings__DefaultConnection` in the host's secret settings. Use an
encrypted SQL connection with certificate validation; do not enable
`TrustServerCertificate` in production or commit credentials.

Before starting, apply reviewed EF migrations to an isolated demo database.
For a reviewed SQL script, use the repository-matched EF CLI:

```bash
dotnet ef migrations script --idempotent --output deployment-migrations.sql
```

Apply the script through the provider's authenticated database tooling after
reviewing it and making a backup. The application does not migrate on startup.
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
Admin/Landlord/Tenant resource boundaries, and create a property through the Razor
form. It must submit to `/Properties/Create`; JSON clients continue using
`/api/v1/properties` with the existing CSRF and authorization requirements.
The API area separates route values so MVC link generation cannot choose the API
controller just because both controllers are called `PropertiesController`.

Record the deployed commit, test persistence after restart, and keep the previous
deployment plus a database backup for rollback. Add the live URL to the portfolio
only after these checks pass.
