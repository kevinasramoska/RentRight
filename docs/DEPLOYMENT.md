# Production demo deployment

The demo needs a backend host that runs .NET 10/Linux containers and a managed
SQL Server database. No hosting account, database, or public demo URL is currently
configured. Container execution has not been verified locally because the Docker
daemon is unavailable; the .NET Release publish and test suite can run without it.

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
- Configure trusted forwarded headers for the actual proxy and verify HTTPS
  redirects/cookies. Do not trust arbitrary forwarded headers from the internet.
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
