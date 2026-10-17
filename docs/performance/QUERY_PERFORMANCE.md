# P07-01: measured SQL queries

Measured 2026-10-17 against the implementation after P06-02 (baseline `d91c8a4`). The source queries
already project DTOs, use `AsNoTracking`, bound lists, and order ties by ID. Admin
roles already use a single batched lookup. Historical roadmap statements about
tracked/unbounded lists and role N+1 are not the current implementation.

## Reproduce

Run Docker Desktop with Linux/x86-64 SQL Server support and the pinned .NET SDK.
No application connection settings, user secrets, shared database, or production
data are needed. The existing Testcontainers fixture creates SQL Server 2022 CU14
with a random port/password, and `SqlApplication` creates/deletes a unique database.
There is no fallback when Docker is unavailable. Never substitute another server.

```sh
docker info
dotnet build RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release
RENTRIGHT_MEASURE_QUERIES=1 \
RENTRIGHT_PERF_PROPERTIES=1000 \
RENTRIGHT_PERF_RUNS=5 \
RENTRIGHT_PERF_OUTPUT="$PWD/artifacts/query-performance" \
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj \
  --configuration Release --no-build --no-restore \
  --filter FullyQualifiedName~QueryPerformanceTests
```

Use a fresh output directory to preserve earlier runs. The fixture owns the server;
cache-clearing commands run only inside its disposable SQL container. Do not run
other SQL test suites simultaneously when collecting timings. The measurement
fact is skipped unless explicitly enabled; normal test runs have no timing gates.
Dataset size accepts 100–10,000 properties, warm repetitions 3–20; invalid values
fail instead of silently clamping. Default sizes are 1,000 and five. Maximum size
is a safety bound, not a tested capacity claim. No packages or projects were added.

The harness first migrates to `20261016161901_MaintenanceTriageAudit`, then seeds.
It measures baseline, property candidate, property plus queue candidate, and
finally removes the experimental indexes and applies `MeasuredQueryIndexes`.
All phases use the same data/workload. The committed `measurements.json` records
all samples, SQL templates, per-command IO, actual-plan operator/index/row summaries,
command counts, and index used-page storage. Parameter values and raw plan XML
are deliberately absent. The allow-listed plan extraction omits runtime parameter
values, database names, user IDs, and request contents. Parameters remain in memory
only for replay. Reproduction uses the seed and workload source, not copied data.

## Data and workload

The seed uses deterministic numbered synthetic users/properties, repeated sort keys,
fixed UTC `2026-10-17T12:00:00`, and fixed tenancy dates. Every property is public:
there is no publication/archive column. Ownership is skewed: 50% one landlord,
25% another, and 25% among five synthetic landlords. There is one ended tenancy
per property plus Active rows on 70% of properties; 10% of properties have a
persisted Active row whose end date has passed. The idle tenant has ended history
only. Legacy consent-null rows exercise the existing legacy access predicate.
Pending invitations, future tenancies and accepted invitation audit are covered
by existing correctness suites, not this performance seed.

Every tenancy has 20 requests, with 200 on the busy tenant's active tenancy at the
default size. Each request has a synthetic baseline history event. Status skew is
5% Submitted, 5% InProgress, 20% Resolved, 70% Closed (the busy extra rows follow
the same cycle). Dates repeat over 60 days, creating ordering ties. Text payloads
are 200 repeated characters, not maximum-width or realistic request content.
All synthetic users are Tenants; every tenth also Landlord, every hundred also
Admin. Five normal fixture actors exercise existing role combinations. Repeated
email display values deliberately create admin sorting ties; normalized usernames
remain unique. Synthetic users have no passwords and cannot log in.

Workloads invoke the actual stores/services used by MVC/API, through fresh
contexts/scopes: MVC property list (100), home list (6), API property pages 1/20
(25), substring search plus inclusive rent bounds, busy/idle tenant dashboard
(three queries), owner queue, status/date-filtered owner page two, one-property
queue (100), multi-role Admin status queue, and admin pages of one/50 users.
HTTP authentication, antiforgery and rendering are excluded from measurement;
the complete SQL suite separately exercises those paths with real cookies.

## Measurement interpretation

For each workload/phase the harness checkpoints and clears SQL data/plan caches,
then times one first run and five warm repeats. First-run samples also include
connection establishment, SQL compilation, and sometimes .NET JIT/query compilation;
OS/Docker disk caches are not cold. Warm samples retain SQL data/plan caches but
still include new contexts/connections (pooling is disabled by the test fixture),
TLS/container/network overhead, materialization and service orchestration.
Admin DI scopes also use the normal test-host logging pipeline. These
are local elapsed times, not production endpoint latency or throughput estimates.

After timing, captured commands are replayed with their original in-memory
parameters under `STATISTICS IO` and `STATISTICS XML`. Logical reads sum non-LOB
reads across tables/worktables and all commands; raw IO messages also report LOB
and physical reads. All observed LOB reads are zero. These are instrumented warm
replays, not reads inferred from elapsed time. Plans contain observed operators
and runtime row counts (`null` when a counter is unavailable); plans/reads are not asserted in CI because optimizer
choices can change. Timings are not pass/fail assertions.

The host was macOS x64/.NET 10.0.302, Docker Desktop 29.6.2 with approximately
7.75 GiB assigned to Linux/x86-64 containers. SQL image is pinned by the existing
fixture to `2022-CU14-ubuntu-22.04`. No production load, concurrent writers, large
row widths, tenant-history extremes, or deployment hardware were measured. Five
warm repeats expose variability but do not establish statistical significance.

## Recorded baseline versus final migration

This final run seeded **1,000 properties, 1,700 tenancies, 34,180 requests,
34,180 history events, 1,005 users and 1,117 role assignments**. Every row below
uses the same database/data and workload before and after the actual migration.
Read counts sum all workload commands; elapsed values are milliseconds. Raw five-run
warm samples and per-command evidence are in [measurements.json](measurements.json).

| Workload | Commands (both) | Logical reads before → final | Warm median before → final (ms) | First run before → final (ms) |
|---|---:|---:|---:|---:|
| properties-mvc | 1 | 69 → 9 | 28.51 → 29.41 | 148.06 → 26.07 |
| properties-home | 1 | 3 → 3 | 28.82 → 23.00 | 39.06 → 37.91 |
| properties-page-1 | 2 | 78 → 13 | 54.40 → 55.05 | 82.64 → 73.14 |
| properties-page-20 | 2 | 78 → 44 | 54.67 → 48.90 | 54.51 → 55.11 |
| properties-search-rent | 2 | 138 → 74 | 68.77 → 51.76 | 110.02 → 63.97 |
| tenant-dashboard-hot | 3 | 1234 → 34 | 77.82 → 72.26 | 283.16 → 147.01 |
| tenant-dashboard-idle | 3 | 12 → 12 | 81.38 → 64.75 | 97.09 → 121.62 |
| owner-queue | 2 | 2554 → 692 | 105.73 → 77.02 | 203.13 → 101.20 |
| owner-queue-filter-page-2 | 2 | 4424 → 484 | 98.24 → 56.76 | 284.74 → 73.16 |
| owner-queue-property | 2 | 750 → 750 | 59.00 → 63.94 | 81.24 → 94.32 |
| admin-queue-filter | 2 | 4372 → 104 | 82.47 → 57.13 | 121.03 → 68.59 |
| admin-users-1 | 4 | 60 → 60 | 90.28 → 111.98 | 253.74 → 119.27 |
| admin-users-50 | 4 | 87 → 87 | 131.55 → 96.93 | 123.31 → 116.45 |

The count at admin page sizes one and 50 is **four**, not 1+N: total count, page
projection, one role-assignment join, available roles. The empty-page case omits
the assignment query. `AdministrationService` caps pages at 50 users, so the
role `IN` query is also bounded. No admin query/index rewrite was justified by
these measurements. Timing changes on unchanged home/idle/admin
queries illustrate environment/order effects; do not attribute them to indexes. MVC and page-one warm medians did not improve
in this final run despite fewer reads; no latency improvement is claimed for them.

## Observed plans and retained indexes

- `IX_Properties_Title_Address_Id`, including RentAmount, Description, LandlordId:
  public MVC/API ordering. Baseline page-one items scanned all 1,000 clustered
  property rows and sorted; final items use an ordered covering index scan that
  reads 25 rows with no Sort. The total-count query remains a separate command.
  MVC reads 69→9, page one 78→13, page 20 78→44. Substring/rent search remains
  a scan (138→74 reads); this index does not make contains-search seekable.
- `IX_MaintenanceRequests_Status_DateSubmitted_Id`, Status ascending, date/ID
  descending, including TenancyId, Priority, Revision: status-filtered queues.
  Baseline filtered Admin queries scanned the wide clustered maintenance index;
  final status queries seek the covering index. Admin workload reads 4,372→104.
  Ownership still joins persisted tenancy/property; no denormalized landlord ID
  or relaxed authorization predicate was introduced. The owner filtered count
  still needs joins and page two still orders/skips rows; its total is 4,424→484.
- Existing `IX_MaintenanceRequests_TenancyId_DateSubmitted`, now including Status:
  tenant dashboard counts/recent projection. The baseline busy tenant plan sought
  200 maintenance index entries then made 200 clustered seeks per maintenance
  query to fetch Status. Final plans use covered tenancy/date seeks and eliminate
  those maintenance clustered lookups: dashboard total reads 1,234→34. The queue
  candidate alone did not fix this tenant workload, which justified the small
  additional include. Tenancy/date keys and existing ordering remain unchanged.

Intermediate phases in the artifact isolate the property and queue candidates.
No extra tenancy, history, role/email, rent-only or publication index was retained.
The home query already uses the clustered property key and stays at three reads.
The one-property queue remains 750 reads; its existing path is left unchanged.
It is bounded at 100 results. Adding Priority/Revision to a second covering index
would duplicate frequently updated triage fields; that additional write/read
tradeoff was not measured, so it was not retained speculatively.
The idle dashboard stays at 12 reads. Deep offset pages still read skipped rows;
keyset pagination or new search semantics would change the API and are outside
this scoped index work. Existing history-order indexes were not changed.

## Storage, writes, and migrations

Observed `sys.dm_db_partition_stats.used_page_count * 8` (KiB):

| Index | Baseline | Final | Added KiB |
|---|---:|---:|---:|
| Properties Title/Address/Id | absent | 544 | 544 |
| Maintenance Status/Date/Id | absent | 2,184 | 2,184 |
| Maintenance Tenancy/Date with Status include | 1,240 | 1,360 | 120 |
| **Total added used pages** | | | **2,848** |

Used pages include index structures, not just payload bytes; this is not database
file growth or a production storage estimate. Property text changes now maintain
an additional wide covering leaf; title/address changes also move index keys.
Every request insert maintains an additional queue index. Status transitions move
its status key and update the existing index's new included Status; priority and
revision changes update queue leaves. This adds write/log/cache cost. Write latency,
log volume, fragmentation, and concurrent-writer impact were **not benchmarked**.
Maximum-length text increases property covering-index storage beyond this seed.

Generated migration `20261017130600_MeasuredQueryIndexes` creates the two indexes
and rebuilds the existing tenancy/date index with Status included. Down removes
the additions and restores the old index. Model/snapshot diffs contain only these
changes. Property sort key maximum width is 1,404 bytes, within SQL Server's
1,700-byte nonclustered-key limit. Existing SQL Server enum conversion, decimal
precision, foreign keys, unique/filtered tenancy reservation and invitation indexes
are unchanged. Index creation/rebuild can block writes; no online deployment or
large-table maintenance-time claim is made.

Review scripts without touching a database (Release matters with `--no-build`):

```sh
DOTNET_ENVIRONMENT=Testing ASPNETCORE_ENVIRONMENT=Testing dotnet ef migrations has-pending-model-changes --project RentRight.csproj --configuration Release --no-build
DOTNET_ENVIRONMENT=Testing ASPNETCORE_ENVIRONMENT=Testing dotnet ef migrations script 20261016161901_MaintenanceTriageAudit 20261017130600_MeasuredQueryIndexes   --project RentRight.csproj --configuration Release --no-build --output /tmp/rentright-indexes-up.sql
DOTNET_ENVIRONMENT=Testing ASPNETCORE_ENVIRONMENT=Testing dotnet ef migrations script 20261017130600_MeasuredQueryIndexes 20261016161901_MaintenanceTriageAudit   --project RentRight.csproj --configuration Release --no-build --output /tmp/rentright-indexes-down.sql
```

No `database update` command targets a developer/shared database. The measurement
run itself upgrades seeded data from the previous migration. The normal SQL
migration regression additionally upgrades, rolls back, and reapplies, comparing
full synthetic domain/history and role-assignment state and checking preservation
of filtered-unique constraints. Query regressions verify tied paging, maximum
sizes, inclusive date/rent boundaries, exact role results, owner/non-owner/Tenant
scope, multi-role Admin override and constant admin command count.

## Changed files

Schema:

- `Data/ApplicationDbContext.cs`: configures the two new indexes and Status include.
- `Migrations/20261017130600_MeasuredQueryIndexes.cs`: generated reversible index changes.
- `Migrations/20261017130600_MeasuredQueryIndexes.Designer.cs`: generated migration target model.
- `Migrations/ApplicationDbContextModelSnapshot.cs`: records only the intended index differences.

Measurement and regression tests:

- `RentRight.Tests/SqlIntegration/SqlApplication.cs`: optional command interceptors and migration target; defaults preserve existing test setup.
- `RentRight.Tests/SqlIntegration/Performance/SyntheticQueryData.cs`: bounded deterministic SQL seed, isolated from application startup.
- `RentRight.Tests/SqlIntegration/Performance/QueryCapture.cs`: in-memory command capture, safe SQL/IO/actual-plan extraction.
- `RentRight.Tests/SqlIntegration/Performance/QueryPerformanceTests.cs`: opt-in phase comparisons, repeat timings, storage and artifact output.
- `RentRight.Tests/SqlIntegration/Performance/QueryRegressionTests.cs`: real-SQL paging, role batching, filtering, projection and authorization checks.
- `RentRight.Tests/SqlIntegration/Performance/QueryIndexMigrationTests.cs`: previous-schema upgrade, rollback/reapply and data/constraint preservation.

Documentation/evidence:

- `docs/performance/QUERY_PERFORMANCE.md`: reproduction, results, tradeoffs, limitations and verification.
- `docs/performance/measurements.json`: synthetic measurement evidence, without parameter values or raw plans.
- `docs/TASKS.md`: P07-01 implementation checkpoint linked to evidence.
- `README.md`: discovery link to the opt-in workflow.

## Verification (2026-10-17)

- Targeted real-SQL query and migration regressions: **4 passed, 0 failed, 0 skipped**.
- Final opt-in measurement: **1 passed**, 52 workload/phase records, five warm samples per record; all commands have IO and actual-plan evidence. Timing thresholds were not asserted.
- Release solution build: **succeeded, 0 warnings, 0 errors**.
- Complete fast .NET suite: **388 passed, 0 failed, 0 skipped**.
- One complete final real-SQL suite: **118 passed, 0 failed, 1 intentional opt-in benchmark skip** (119 total), **15 m 40 s**. All disposable SQL containers were removed after completion.
- SQL test-project Release build: **succeeded, 0 warnings, 0 errors**.
- Installed Node **24.19.0**: `npm ci --prefix ClientApp` and `npm run build --prefix ClientApp` passed, supplying real frontend assets for HTTP smoke tests. No frontend source changed; other frontend gates were not run.
- Release EF pending-model check: **no changes since the last migration**. Generated Up/Down SQL reviewed; seeded upgrade/rollback/reapply passed against SQL Server.
- Diff whitespace/scope checks and artifact field/IO-total checks passed. No connection settings, secrets, runtime parameter values, request contents, raw plans or build output are committed.

Verification commands (with installed Node 24 selected for the asset build):

```sh
npm ci --prefix ClientApp
npm run build --prefix ClientApp
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --filter 'FullyQualifiedName~QueryRegressionTests|FullyQualifiedName~QueryIndexMigrationTests'
dotnet build RentRight.slnx --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --no-restore
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --no-build --no-restore -- xUnit.ParallelizeTestCollections=false
```

The complete SQL invocation serializes test collections to reduce local Docker
memory pressure. Internal concurrency/race tests still run their competing tasks.
The benchmark's successful opt-in invocation is separate from the ordinary suite;
its skip in that suite is intentional, not missing verification.
