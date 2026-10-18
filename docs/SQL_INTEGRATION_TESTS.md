# SQL Server integration tests

The explicitly invoked project `RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj`
exercises the real ASP.NET Core host, SQL Server EF stores, checked-in migrations,
Identity login cookies, authorization and global antiforgery filters. The existing
`RentRight.Tests/RentRight.Tests.csproj` remains the fast unit/fake-store HTTP
contract suite. The SQL project is intentionally outside the default solution so
ordinary local checks do not require Docker. It must be invoked separately in CI.
No tests silently skip when Docker is unavailable. The test-only dependency is
the [Testcontainers SQL Server module](https://dotnet.testcontainers.org/modules/mssql/),
using its built-in readiness checks and resource lifecycle rather than custom
Docker orchestration. Direct package versions are pinned, following the existing
projects. All three projects now have committed NuGet lock files; CI uses
`RentRight.CI.slnx` to restore/build them together with locked mode and transitive
auditing. The normal solution remains Docker-independent. See [CI quality gate](CI.md).

## Local execution

Install the repository-pinned .NET SDK and run Docker with Linux containers.
SQL Server requires x86-64 support, several GB of free memory and permission to
pull Microsoft SQL Server and Testcontainers resource-reaper images. On Apple
Silicon, configure Docker Desktop's x86-64 emulation. Testcontainers needs access
to the Docker socket; `docker info` must succeed from the same shell.

From the repository root:

```bash
docker info
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --filter FullyQualifiedName~Migrate_EmptyDatabase
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --no-restore
```

The first pull can take several minutes. The fixture reports an explicit Docker
requirement on startup failure; an unavailable daemon is a failed run. Do not use
a developer database, local secrets, EF InMemory or SQLite as a fallback.

## Isolation and lifecycle

Each suite run creates a digest-pinned SQL Server 2022 CU14 container with a random password and
random mapped port. Each test case creates a randomly named database and applies
all real migrations before starting `WebApplicationFactory<Program>`; normal role
seeding therefore uses SQL Server too. There is no production migration startup
hook. Every actor is persisted through `UserManager`, then signs in through the
real Identity UI using its login antiforgery token. Anonymous clients have no
cookie. Authenticated unsafe requests obtain an Identity Manage page token.
There is no alternate authentication handler or production test endpoint.

A database per case prevents cross-test state leakage and permits independent
processes to run concurrently. Container-owning test classes run serially to bound
Docker memory; explicit concurrent request/two-context race tests remain parallel. Host disposal is followed by database deletion in
a finally block; connection pooling is disabled for these databases. Fixture
disposal removes SQL Server, and Testcontainers' resource reaper handles abandoned
resources on process termination. Do not disable the reaper or enable reuse.
The migration smoke test checks that each fresh test database has no properties;
two full invocations verify repeatability with independent containers/databases.

## CI execution

Use a .NET 10 runner with a running Linux/x86-64 Docker daemon (for example a
Linux hosted runner), sufficient memory, and registry network access. Run the
three test commands above in addition to the fast suite/build checks; propagate
nonzero exits as failures. No SQL connection secret or pre-created service
container is required. Run the SQL project vulnerability audit when dependencies
change:

```bash
dotnet list RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj package --vulnerable --include-transitive
```

Coverage includes anonymous/Tenant/owner/other landlord/Admin/multi-role Admin
create, update and delete; public projection/paging; cookie 401/403 and ownership
404; CSRF rejection/acceptance; validation 400; history 409; last-admin protection;
and SQL Server foreign-key, date-check and filtered-unique violations. Tests
cover current behavior. P05-01 adds tenancy invite/accept/end, scoped list/detail,
privacy/token handling, fixed lifecycle audit, reservation/rowversion constraints,
serialized lifecycle races and migration preservation/guarded rollback. It does not
introduce property publication/archive state or maintenance status history.

## Verification record (2026-10-16)

- Empty-database migration smoke: 1 passed, 0 failed, 0 skipped.
- Two complete independent SQL runs: each 26 passed, 0 failed, 0 skipped;
  each run created a new container, and each case a freshly migrated database.
  SQL Server containers were absent after suite disposal.
- Fast-suite restore and production Release build succeeded (0 warnings/errors);
  fast tests: 286 passed, 0 failed, 0 skipped.
- EF pending-model check: no changes since the last migration.
- Solution and separate SQL-project transitive vulnerability audits: no known
  vulnerable packages reported by the configured NuGet source.
- Docker Desktop server 29.6.2, Linux/x86-64. The initial unavailable-daemon
  blocker was resolved before the successful SQL runs. Application-image
  build/deployment validation remains outside P04-02.

## P05-01 lifecycle suite

`TenancyLifecycleSqlTests` adds real-cookie actor matrices, no-enumeration receipts,
exact normalized identifiers, persisted role rechecks, token expiry/user/reuse
checks, future occupancy, Razor workflows, dashboard consent visibility and existing
maintenance authorization regression. Independent contexts/HTTP clients race
invite/invite, accept/accept, end/end, accept/end, invite/accept and invite/end.
Relational checks cover Pending/Active reservation collisions, active tenant and
pending expiry requirements, unique token hashes and stale rowversion writes.
Legacy-schema upgrade verifies untouched Active/Ended rows and unknown legacy
consent/audit fields; rollback with lifecycle audit must fail before data loss.
Test-host logging is captured to assert that raw tokens never reach the logs.

The final verification counts are recorded in [TENANCY_LIFECYCLE.md](TENANCY_LIFECYCLE.md).
