# P07-02: concurrency, integrity and retained history

Implemented from P07-01 merge `80374f5`, preserving the measured indexes, single
ASP.NET Core deployment, SQL Server, existing services, lifecycle locks and
maintenance revision API. No new dependencies or database mutation outside
disposable Testcontainers databases were needed.

## Concurrency contracts

| Mutation | Required precondition | Source and recovery |
| --- | --- | --- |
| Property PUT | JSON `expectedVersion` | Owner/Admin `GET /api/v1/properties/{id}/edit` returns `expectedVersion`; reload after 409. |
| Property DELETE | `X-Expected-Version` header | Same edit resource; public list/detail responses remain unchanged. |
| MVC property edit/delete | Hidden `ExpectedVersion` | Rendered from persisted state; reload the edit/delete page after conflict. |
| Tenancy end/cancel API | `X-Expected-Version` header | Scoped tenancy list/detail `version`; reload after 409. |
| MVC tenancy end/cancel | Hidden `ExpectedVersion` | Rendered on each manager list row. |
| Tenancy acceptance | Existing one-use invitation token | Persisted token hash, matching Tenant, Pending/expiry/date checks inside the property lock. No additional version input. |
| Maintenance triage/MVC status | Existing required `expectedRevision` | Authorized detail/update form; reload after 409. No public contract change. |

Property and tenancy versions are canonical base64 encoding of exactly eight SQL
rowversion bytes. Missing, malformed and noncanonical values are invalid; no
omitted-token fallback exists. A valid but stale or forged value conflicts. Values
are concurrency inputs, never access credentials. Authorization precedes service/
store version validation; normal transport binding can still reject malformed JSON.
API uses existing Problem Details: 400 validation, 409 conflict, 401 anonymous,
403 unsupported role, and concealed 404 for another owner's resource. MVC edit
preserves submitted values with validation/conflict errors; deletion/end conflicts
redirect with a reload message. Invalid delete/end versions return 400.

Properties now use SQL rowversion in EF and in the conditional bulk edit predicate,
including the owner observed during authorization. Delete checks the submitted
version and owner, then EF conditions DELETE on the original rowversion. Ownership
changes also advance rowversion, preventing an authorization/read/write race.
Tenancy retains its rowversion, serializable property lock, single Pending/Active
reservation index, and scoped authorization. End/cancel additionally compares the
submitted version inside that lock. Acceptance consumes an immutable one-use token
under the same lock; an end form read before acceptance now conflicts instead of
silently ending newly accepted occupancy. Expiry replacement is server-controlled,
locked and tracked with rowversion; it retains the expired row.

Maintenance now has SQL rowversion. The service reads it with the request and the
store's conditional update checks both that observed rowversion and the client's
expectedRevision, status and persisted landlord ownership (or Admin override).
Every accepted write increments revision once. A SQL trigger rejects **every**
UPDATE, including ExecuteUpdate/raw SQL, unless revision advances by exactly one;
therefore an out-of-band update cannot change rowversion while leaving the public
revision unchanged. EF rowversion also protects tracked writes. The public revision
is retained to avoid churn in existing clients/forms. Priority-only changes advance
revision without a fictitious status event. Status changes and their actor, UTC time,
previous/new state and note still commit in one transaction. Event failure rolls
back the row's status, priority, revision and stored rowversion; the global SQL
rowversion counter may contain gaps, which have no business meaning.

## Database invariants

The new migration adds property/maintenance rowversion, nonnegative rent/revision,
maintenance update-time >= submission-time, paired truthful acceptance actor/time,
matching acceptance Tenant, no invited Active occupancy without acceptance, no
consent on Pending rows, and no invitation hash outside Pending. Event facts with
known previous state must express an allowed transition and an actor; a legacy
baseline cannot invent actor, previous state or note. Existing pre-triage events
with unknown previous state/actor remain valid. Legacy occupancies with no recorded
invitation retain their null consent/audit fields.

Existing status/priority checks, date range check, string bounds, decimal precision,
enum conversions, restrictive business foreign keys, filtered reservation/hash
uniqueness and P07-01 indexes remain unchanged. Four new restrictive audit foreign
keys and their conventional supporting indexes preserve actor references. No query
index redesign was included. EF SQL OUTPUT is disabled for trigger-bearing tables
so normal generated-value reads continue to work with SQL Server triggers.

## Retention and deletion policy

| Record | Behavior |
| --- | --- |
| Property with no tenancy history | Owner/Admin may hard-delete with a current version. |
| Property with any tenancy history | Retain; service rejects deletion and SQL foreign key prevents bypass. |
| Tenancy, including unmatched/expired/cancelled invitations and ended occupancies | Retain; SQL DELETE trigger also protects rows without dependents. End/cancel remains the supported lifecycle operation. |
| Maintenance request, including one without events | Retain; SQL DELETE trigger protects it independently of history foreign keys. |
| Maintenance status event | Append-only. EF rejects tracked changes/deletes; SQL trigger rejects UPDATE/DELETE including bulk operations. |
| User referenced as landlord, tenant, invitation/acceptance/end actor or maintenance-event actor | Restrictive foreign keys deny hard deletion. Null unknown legacy actors remain unknown; no relabeling, cascade or invented actor. |
| Unreferenced user | Existing Identity deletion semantics; no new account lifecycle feature. |

No purge schedule, legal retention period, anonymization, archive flag/UI or
background deletion job is invented. A future product retention/privacy policy
must explicitly authorize any destructive change and preserve audit truth.
Database guards protect normal DML, not a privileged operator disabling triggers,
using schema/partition operations or dropping tables. Arbitrary privileged SQL is
not an alternative supported application workflow; callers must preserve atomic
status/event writes and server-derived actors. No new SQL permissions/Identity
policy architecture is introduced here.

## Migration and deployment

Migration: `20261017134027_IntegrityConcurrencyRetention`. Older applied migrations
are unchanged. SQL Server generates rowversions for existing rows; it does not
backfill invented business timestamps, consent or actor IDs. Existing priorities,
revisions and events are preserved. Before any DDL, preflight throws 51003 on
incompatible rows or orphaned audit references. Constraint/FK creation validates
existing rows too. Investigate failed rows and prepare a reviewed, evidence-based
repair separately; do not truncate, discard history, fabricate users or disable
checks to force deployment.

The checked-in [idempotent incremental SQL](migrations/P07-02-integrity-concurrency-retention.sql)
assumes the P07-01 migration is already applied. It can be rerun safely and does not
replace an empty-database/full-history deployment script. Dynamic CREATE TRIGGER
DDL works inside EF's idempotent IF blocks. Deployment should stop writers and
coordinate the application/schema/client contract upgrade: old property/end clients
will receive validation failures; the old application also cannot rely on EF OUTPUT
against the new triggers. Preflight scans, constraint validation, rowversion column
population and index creation can take schema locks, log space and significant time
on larger databases. Plan a measured maintenance window, backup and capacity check;
no production-size duration claim is made from these small disposable fixtures.

Reproduce scripts without connecting to an application database:

```bash
dotnet ef migrations script 20261017130600_MeasuredQueryIndexes 20261017134027_IntegrityConcurrencyRetention --configuration Release --idempotent --output docs/migrations/P07-02-integrity-concurrency-retention.sql
dotnet ef migrations script 20261017130600_MeasuredQueryIndexes 20261017134027_IntegrityConcurrencyRetention --configuration Release --output /tmp/p0702-forward.sql
dotnet ef migrations script 20261017134027_IntegrityConcurrencyRetention 20261017130600_MeasuredQueryIndexes --configuration Release --output /tmp/p0702-rollback.sql
dotnet ef migrations has-pending-model-changes --configuration Release
```

Up/Down, snapshot, forward (94 lines), rollback (63 lines), and incremental
idempotent SQL (237 lines) were inspected. Forward contains no business UPDATE,
DELETE, truncation or data removal. Down removes only this phase's guards,
constraints, supporting indexes and property/maintenance rowversion columns;
**business/audit columns and records are preserved**. Stop writers before rollback,
coordinate the older application, and discard all cached version values. Reapply
generates new rowversions. Earlier migrations retain their own data-loss guards;
this Down does not authorize rolling back recorded lifecycle/triage history.
A forward repair is preferable once clients rely on these protections.

## Verification

Final verification on 2026-10-17: Release solution build passed with zero warnings/errors;
complete fast .NET suite: **402 passed, 0 failed, 0 skipped**. Node 24 frontend
lint, type-check/build passed; the complete frontend suite passed **45/45** with
`npm test -- --maxWorkers=1`. An earlier concurrent run had one existing overview
loading timeout (44 passed/1 failed); that test passed in isolation, then all 45
passed without any frontend code/test changes. EF pending-model check reports no
drift. An additional targeted SQL run passed **20/20**, including MVC token
round trips, concurrent HTTP edit/delete, event-failure rowversion rollback,
ineligible tenant journeys, lifecycle races and query-index migration regression.
The complete Docker SQL suite passed **165 tests, 0 failed, 1 skipped** out of
166 tests (18.0211 minutes). The only skip is the existing opt-in query timing
benchmark; all integration regression/race/migration tests ran. Containers were
removed at suite completion; unrelated user containers were left running.

Final commands and results:

| Check | Command | Result |
| --- | --- | --- |
| Release solution | `dotnet build RentRight.slnx --configuration Release --nologo` | Passed; 0 warnings, 0 errors. |
| Complete fast suite | `dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --no-restore --nologo` | 402 passed, 0 failed, 0 skipped. |
| Complete SQL suite | `dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --no-restore --logger 'console;verbosity=normal' --nologo` | 165 passed, 0 failed; 1 opt-in benchmark skipped. |
| Model drift | `dotnet ef migrations has-pending-model-changes --configuration Release --no-build` | No model changes since last migration. |
| Frontend | Node 24 `npm ci`, `npm run lint`, `npm run build`, `npm test -- --maxWorkers=1` in ClientApp | Install/lint/type-check/build passed; all 45 tests passed. |
| Diff/source review | `git diff --cached --check`, changed-file inventory and credential/private-key pattern scan | Clean; no matching credentials/private keys, configuration changes, build outputs or unrelated files. |

No configured developer/shared database was migrated. The checked-in idempotent
script is a reviewed deployment artifact, not evidence of a production deployment.
Targeted evidence: 128 existing fast property/tenancy tests passed; 36 focused
property/version unit tests passed; 7 SQL smoke/ownership tests passed; the initial
39 new integrity SQL tests passed. These include two-context writes, stale edit/
delete, missing/malformed/forged versions, IDOR with valid tokens, antiforgery,
stale end-after-accept, maintenance token/revision coherence, direct constraints,
retention without dependents, audit-only user references, previous-schema upgrade,
rollback/reapply, idempotent SQL twice, and preflight failure without data repair.
Additional targeted checks cover rendered MVC tokens and a concurrent HTTP
property edit/delete. Existing lifecycle and maintenance suites preserve role,
active-tenancy, over-posting, transition, event atomicity, uniqueness and race coverage.
Container-owning classes are serialized to bound memory; internal race tests remain
concurrent. The performance fixture now uses update times >= submission times and
accepted tenancy fixtures record their real synthetic actor. Legacy null-actor
records are still exercised. Migration comparison fixtures exclude transient new
rowversion values while still comparing every business/audit field.

The React tenant portal does not edit/delete properties, end tenancies or triage
maintenance; its consumers and contracts are unchanged. Its assets were built with
the installed Node 24 runtime for HTTP tests, without changing global Node setup.
The opt-in P07-01 timing benchmark is not rerun: no measured index changed and SQL
query/migration regression fixtures remain part of the complete suite.

## Changed files by purpose

The following inventory explains every scoped file; paths are repository-relative.

### Application and contracts

| File | Purpose |
| --- | --- |
| `Application/ExpectedVersion.cs` | Validate canonical opaque SQL versions. |
| `Features/Maintenance/EfMaintenanceStore.cs` | Carry the observed SQL rowversion into the existing atomic revision/ownership conditional write. |
| `Features/Maintenance/IMaintenanceStore.cs` | Carry the observed SQL rowversion into the existing atomic revision/ownership conditional write. |
| `Features/Maintenance/MaintenanceCommandService.cs` | Carry the observed SQL rowversion into the existing atomic revision/ownership conditional write. |
| `Features/Properties/Contracts/PropertyContracts.cs` | Require and propagate property versions; condition edits/deletes on persisted state and ownership. |
| `Features/Properties/EfPropertyStore.cs` | Require and propagate property versions; condition edits/deletes on persisted state and ownership. |
| `Features/Properties/IPropertyService.cs` | Require and propagate property versions; condition edits/deletes on persisted state and ownership. |
| `Features/Properties/IPropertyStore.cs` | Require and propagate property versions; condition edits/deletes on persisted state and ownership. |
| `Features/Properties/PropertyService.cs` | Require and propagate property versions; condition edits/deletes on persisted state and ownership. |
| `Features/Tenancies/Contracts/TenancyContracts.cs` | Project and propagate tenancy versions; validate end/cancel under the existing property lock. |
| `Features/Tenancies/EfTenancyStore.cs` | Project and propagate tenancy versions; validate end/cancel under the existing property lock. |
| `Features/Tenancies/ITenancyService.cs` | Project and propagate tenancy versions; validate end/cancel under the existing property lock. |
| `Features/Tenancies/ITenancyStore.cs` | Project and propagate tenancy versions; validate end/cancel under the existing property lock. |
| `Features/Tenancies/TenancyService.cs` | Project and propagate tenancy versions; validate end/cancel under the existing property lock. |

### HTTP and Razor

| File | Purpose |
| --- | --- |
| `Controllers/Api/V1/PropertiesController.cs` | Bind/provide expected versions and map invalid/conflict outcomes while retaining authorization. |
| `Controllers/Api/V1/PropertyApiContracts.cs` | Bind/provide expected versions and map invalid/conflict outcomes while retaining authorization. |
| `Controllers/Api/V1/TenanciesController.cs` | Bind/provide expected versions and map invalid/conflict outcomes while retaining authorization. |
| `Controllers/PropertiesController.cs` | Bind/provide expected versions and map invalid/conflict outcomes while retaining authorization. |
| `Controllers/TenanciesController.cs` | Bind/provide expected versions and map invalid/conflict outcomes while retaining authorization. |
| `ViewModels/TenancyViewModels.cs` | Render/carry persisted versions in the existing edit, delete or end forms. |
| `Views/Properties/Delete.cshtml` | Render/carry persisted versions in the existing edit, delete or end forms. |
| `Views/Properties/Edit.cshtml` | Render/carry persisted versions in the existing edit, delete or end forms. |
| `Views/Tenancies/Index.cshtml` | Render/carry persisted versions in the existing edit, delete or end forms. |

### Schema and models

| File | Purpose |
| --- | --- |
| `Data/ApplicationDbContext.cs` | Map rowversions, integrity checks, restrictive audit references and trigger-compatible writes. |
| `Migrations/20261017134027_IntegrityConcurrencyRetention.Designer.cs` | Generated migration model metadata. |
| `Migrations/20261017134027_IntegrityConcurrencyRetention.cs` | Preflight, rowversions, checks, audit foreign keys, retention/revision triggers and reversible schema changes. |
| `Migrations/ApplicationDbContextModelSnapshot.cs` | Generated current model snapshot. |
| `Models/MaintenanceRequest.cs` | Add persisted SQL rowversion to the mutable aggregate. |
| `Models/Property.cs` | Add persisted SQL rowversion to the mutable aggregate. |

### Documentation and deployment SQL

| File | Purpose |
| --- | --- |
| `README.md` | Document current contracts, retention, migration limits and verification. |
| `docs/API.md` | Document current contracts, retention, migration limits and verification. |
| `docs/ARCHITECTURE.md` | Document current contracts, retention, migration limits and verification. |
| `docs/CONCURRENCY_RETENTION.md` | Document current contracts, retention, migration limits and verification. |
| `docs/SQL_INTEGRATION_TESTS.md` | Document current contracts, retention, migration limits and verification. |
| `docs/TASKS.md` | Document current contracts, retention, migration limits and verification. |
| `docs/TENANCY_LIFECYCLE.md` | Document current contracts, retention, migration limits and verification. |
| `docs/migrations/P07-02-integrity-concurrency-retention.sql` | Reviewed generated incremental idempotent deployment SQL; no connection values. |

### Tests and fixtures

| File | Purpose |
| --- | --- |
| `RentRight.Tests/Api/PropertiesApiTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
| `RentRight.Tests/Api/TenanciesApiTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
| `RentRight.Tests/Models/ExpectedVersionTests.cs` | Canonical version parsing and malformed/missing value tests. |
| `RentRight.Tests/Properties/HomeControllerTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
| `RentRight.Tests/Properties/PropertiesControllerTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
| `RentRight.Tests/Properties/PropertyServiceTests.cs` | Version validation/no-write regressions, persisted version output and updated valid mutation fixtures. |
| `RentRight.Tests/SqlIntegration/IntegrityConcurrencyRetentionSqlTests.cs` | New real-SQL concurrency, HTTP/MVC version, constraints, retention and deployment regression tests. |
| `RentRight.Tests/SqlIntegration/LandlordMaintenanceSqlTests.cs` | Use persisted versions and truthful acceptance/revision fixtures; verify stale lifecycle races and atomic rowversion rollback. |
| `RentRight.Tests/SqlIntegration/Performance/QueryIndexMigrationTests.cs` | Compare all business data across migrations independently of transient rowversions. |
| `RentRight.Tests/SqlIntegration/Performance/SyntheticQueryData.cs` | Keep measured synthetic data consistent with maintenance timestamp integrity. |
| `RentRight.Tests/SqlIntegration/SqlPipelineTests.cs` | Use persisted versions and truthful acceptance/revision fixtures; verify stale lifecycle races and atomic rowversion rollback. |
| `RentRight.Tests/SqlIntegration/SqlServerFixture.cs` | Serialize container-owning classes while preserving explicit concurrent race tests. |
| `RentRight.Tests/SqlIntegration/TenancyLifecycleSqlTests.cs` | Use persisted versions and truthful acceptance/revision fixtures; verify stale lifecycle races and atomic rowversion rollback. |
| `RentRight.Tests/SqlIntegration/TenantMaintenanceSqlTests.cs` | Use persisted versions and truthful acceptance/revision fixtures; verify stale lifecycle races and atomic rowversion rollback. |
| `RentRight.Tests/Tenancies/TenanciesControllerTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
| `RentRight.Tests/Tenancies/TenancyServiceTests.cs` | Adapt existing contract doubles and valid request fixtures to the required versions. |
