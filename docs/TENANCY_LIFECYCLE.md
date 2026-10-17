# P05-01 tenancy lifecycle

This records the implemented decision and current manual verification. The Phase 1
manual checklist describes the previous immediate Admin assignment workflow.

## Decisions and compatibility

- Admin or an owning Landlord may invite/end; explicit Admin override wins for
  multi-role accounts. Every resource query is scoped by persisted ownership.
- Immediate Admin assignment is intentionally removed: new occupancy requires
  explicit acceptance by the invited Tenant. Existing Active occupancies retain
  their status and dates; their consent/actors are unknown (null), not backfilled.
  Existing dashboard history remains visible, but unaccepted invitations are excluded.
- MVC `/Tenancies/Create` retains its route and `TenantId` field name, but the field
  now holds an exact login identifier rather than an internal user ID. There is no
  tenant picker. Existing direct-assignment clients must adopt invitations.
- Invite receipts do not confirm account existence/role. Unknown/non-Tenant targets
  reserve the property too, with no account identity in pending/cancelled responses.
  Only the matched account, with Tenant membership at issue and acceptance, can
  consume the token. Receipts and list/detail cannot be used as an account directory.
- One Pending or Active tenancy reserves a property (including future Active rows).
  Ended history permits a subsequent invitation. Expired reservations are atomically
  retained as Ended/Expired on replacement; no background worker is required.
- New fixed audit fields record invitation, acceptance and ending once per row.
  Expiry uses system time with a null actor and explicit Expired reason. Legacy
  rows remain truthful. Repeated ending returns conflict without replacing audit.
- `TenantId` becomes nullable only to represent unmatched invitations; SQL requires
  a non-null tenant for Active rows. Existing foreign keys and calendar-date check
  remain. New indexes reserve Pending/Active property slots and unique token hashes;
  rowversion prevents stale writes. Only 64-character SHA-256 hashes are stored.
- The migration adds nullable audit columns and SQL rowversion without rewriting
  legacy values. Up/Down and generated SQL are reviewed. Down fails before dropping
  columns when lifecycle data exists; recovery is an explicit reviewed export/forward
  fix, not silent audit deletion. No shared database is migrated during verification.
- Every mutation locks the same property row within a serializable transaction,
  rechecks state/ownership and commits once. Database constraints and rowversion are
  independent safeguards. Losing commands return 404/409; there is no automatic
  business retry or silent last-write-wins behavior. P07-02 adds a required opaque
  version to end/cancel forms and API requests; acceptance retains its one-use token.
- The current model has no archive/publication state; existing properties are
  available unless reserved. P05-01 does not introduce that unrelated lifecycle.

## Manual verification

Use disposable SQL Server and synthetic Admin, owner/other Landlord and two Tenant
accounts. Give the owner a property and assign roles through the existing Admin UI.

1. As owner open `/Tenancies/Create`, enter Tenant A's exact login identifier and
   valid dates. Confirm a one-time token receipt, no tenant directory, no email,
   no account existence confirmation, and a Pending manager list entry.
2. Deliver the token privately. As Tenant B submit it at `/Tenancies/Accept` and
   confirm rejection. As Tenant A submit it and confirm explicit activation.
3. Reuse the same token; confirm rejection. Confirm pending/future/ended occupancy
   never grants current date-based tenant access. Active Tenant A may read their
   tenancy through API list/detail and retains existing maintenance authorization.
4. As other Landlord request list/detail/end for the owner's row; confirm exclusion
   or concealed 404. As multi-role Admin confirm override works, but acceptance
   still requires being the invited Tenant, never Admin consent on another's behalf.
5. Try missing/unknown/non-Tenant exact identifiers. Nonempty valid input has the
   same receipt shape/status and private list presentation. No occupancy activates;
   cancel the mistaken reservation and reissue. Blank/overlong input is validation.
6. Reject end-before-start, past end dates, duplicate Pending/Active and missing
   properties. Existing ended history must allow another invitation but still
   prevent property deletion. Cancellation and ending retain all history.
7. Submit invite/accept/end without matching antiforgery; confirm rejection. Try
   forged status/actor/tenant IDs and confirm they cannot override server values.
8. Confirm tokens do not appear in URLs, validation responses, subsequent list/detail,
   saved rows or logs. Only the issuance response and acceptance request contain
   the raw token. OpenAPI intentionally describes those DTO fields without examples.
9. Use SQL tests for expiry, exact-boundary expiry, concurrent invitations,
   accept/accept, end/end, accept/end, invite/accept, invite/end, rowversion,
   migration upgrade preservation and guarded rollback.

## Operational limitations

Tokens are bearer secrets bound additionally to the intended account. Deliver them
privately over a trusted channel using HTTPS. No email/SMS provider, retry delivery,
resend/recovery, account registration invitation, or token URL is implemented.
Cancel/reissue if a token or identifier is lost/wrong. A successful receipt does not
prove delivery, account existence, Tenant role or future acceptance. Unknown-account
reservations intentionally consume capacity for privacy until cancellation/expiry.
Do not enable request/response body logging or sensitive EF parameter logging on
these routes. Browser no-store controls do not prevent a human from copying a token.
New tenancy audit is retained in SQL; it is not a general immutable event ledger or
maintenance status history (P05-02/P05-03 remain outside scope).

## Verification

Verification date: 2026-10-16. SDK 10.0.302; Docker Desktop server 29.6.2;
SQL Server 2022 CU14 in disposable Testcontainers. Each test case owns a freshly
migrated random database. No shared/developer database is used.

| Check | Result |
| --- | --- |
| Initial targeted SQL invitation actor matrix | 6 passed, 0 failed/skipped. |
| Initial complete SQL suite before final boundary additions | 65 passed, 0 failed/skipped. |
| Final targeted tenancy/domain/service/MVC/HTTP tests | 185 passed, 0 failed/skipped. |
| Separate targeted HTTP contracts | 12 passed, 0 failed/skipped. |
| Full fast suite | 321 passed, 0 failed/skipped. |
| Expanded full SQL suite, independent run 1 | 72 passed, 0 failed/skipped (4.7077 minutes). |
| Expanded full SQL suite, independent run 2 | 72 passed, 0 failed/skipped (4.7566 minutes). |
| Standard production Release build | Passed; 0 warnings, 0 errors. |
| Release solution build | Passed; 0 warnings, 0 errors. |
| EF pending-model check | No changes since the final migration. |
| Incremental idempotent Up SQL and rollback SQL | Generated and reviewed (163 / 116 lines); no shared database application. |
| NuGet transitive vulnerability audits | Web, fast-test and SQL-test projects: no known vulnerable packages from configured sources. |
| Diff/whitespace and changed Markdown local links | Passed. |
| SQL container disposal | Confirmed no SQL Server test containers remain after both expanded runs. |

Executed checks (from repository root):

```bash
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --filter 'FullyQualifiedName~Tenanc'
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --filter FullyQualifiedName~TenanciesApiTests
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --filter FullyQualifiedName~Invite_ActorMatrix
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --logger 'console;verbosity=normal'
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --no-restore --logger 'console;verbosity=normal'
dotnet restore RentRight.Tests/RentRight.Tests.csproj
dotnet build RentRight.slnx --configuration Release
dotnet build RentRight.csproj --no-restore --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --no-restore --configuration Release
dotnet ef migrations has-pending-model-changes --project RentRight.csproj --configuration Release --no-build
dotnet ef migrations script 20260811163855_CompleteCoreRentalDomain --project RentRight.csproj --configuration Release --no-build --idempotent --output /tmp/rentright-tenancy-up.sql
dotnet ef migrations script TenancyInvitationLifecycle CompleteCoreRentalDomain --project RentRight.csproj --configuration Release --no-build --output /tmp/rentright-tenancy-down.sql
dotnet list RentRight.csproj package --vulnerable --include-transitive
dotnet list RentRight.Tests/RentRight.Tests.csproj package --vulnerable --include-transitive
dotnet list RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj package --vulnerable --include-transitive
git diff --check
```

The earlier pre-final fast run had 322 passes; removal of the obsolete account-
existence outcome test gives the final 321. Privacy/no-enumeration behavior is
covered instead by real SQL/HTTP tests. New Pending/Active constraints, legacy
preservation, rollback protection, token handling, cookie/CSRF, scoped access and
all six race combinations passed in both expanded SQL runs. No verification failed;
one migration-tool invocation was cancelled during its build and the new
uncommitted migration was regenerated from the unchanged previous snapshot.
Previously committed migrations were not changed.
Generated SQL and command logs remain outside source control. Manual browser
checks are documented above; automated HTTP tests exercised the real Razor pages.

## Changed-file inventory

| File | Purpose |
| --- | --- |
| `Models/Tenancy.cs` | Pending invitation data, nullable target, lifecycle audit, rowversion and centralized accept/end rules. |
| `Models/TenancyStatus.cs` | Adds Pending without changing existing enum values. |
| `Data/ApplicationDbContext.cs` | Hash/audit mapping, optional invitation target, reservation/hash indexes, lifecycle checks and concurrency mapping. |
| `Features/Tenancies/Contracts/TenancyContracts.cs` | One-time receipt, acceptance, detail/page outcomes; removes directory/account-existence contracts. |
| `Features/Tenancies/ITenancyService.cs` | Adds shared acceptance and scoped list/detail use cases. |
| `Features/Tenancies/TenancyService.cs` | Actor capability checks, validation, secure issuance, clock and outcome orchestration. |
| `Features/Tenancies/ITenancyStore.cs` | Derived actor scope and atomic lifecycle persistence contracts. |
| `Features/Tenancies/EfTenancyStore.cs` | Scoped SQL projections, exact Identity lookup, transactions/locks, role recheck, audit, expiry replacement and conflicts. |
| `Features/Dashboards/EfDashboardDataStore.cs` | Excludes unaccepted invitations while preserving accepted/legacy dashboard history. |
| `Features/Maintenance/IMaintenanceStore.cs` | Internal nullable tenant references accurately represent unaccepted invitations; ownership checks remain unchanged. |
| `Controllers/Api/V1/ApiV1ControllerBase.cs` | Makes existing Problem Details helpers available to the tenancy adapter. |
| `Controllers/Api/V1/TenanciesController.cs` | Minimal dedicated lifecycle DTOs and cookie/antiforgery API endpoints with safe projections and no-store responses. |
| `Controllers/TenanciesController.cs` | Manager invitation/cancellation and Tenant acceptance adapters, one-time receipt without TempData token storage. |
| `ViewModels/TenancyViewModels.cs` | Exact identifier input and dedicated acceptance model; removes tenant picker. |
| `Views/Tenancies/Create.cshtml` | Invitation form with exact identifier and validation. |
| `Views/Tenancies/Index.cshtml` | Invitation navigation and Pending cancellation. |
| `Views/Tenancies/Invitation.cshtml` | One-time token receipt and manual-delivery/privacy explanation. |
| `Views/Tenancies/Accept.cshtml` | Explicit cookie/antiforgery-protected token acceptance form. |
| `Views/Dashboard/LandlordDashboard.cshtml` | Links owners to tenancy management. |
| `Views/Dashboard/TenantDashboard.cshtml` | Links tenants to invitation acceptance. |
| `Migrations/20261016115250_TenancyInvitationLifecycle.cs` | Incremental non-destructive upgrade and audit-preserving rollback guard. |
| `Migrations/20261016115250_TenancyInvitationLifecycle.Designer.cs` | Generated target model for this migration. |
| `Migrations/ApplicationDbContextModelSnapshot.cs` | Updated tenancy schema and current EF metadata; no unrelated entity changes. |
| `RentRight.Tests/Tenancies/TenancyLifecycleTests.cs` | Deterministic domain transition, identity, expiry and audit checks. |
| `RentRight.Tests/Tenancies/TenancyServiceTests.cs` | Service actor/date/outcome/hash/cancellation tests replacing direct-assignment expectations. |
| `RentRight.Tests/Tenancies/TenanciesControllerTests.cs` | Razor receipt and updated picker-free contracts; existing outcome mapping coverage retained. |
| `RentRight.Tests/Api/TenanciesApiTests.cs` | Fast HTTP contract, Problem Details, safe projection, paging and OpenAPI checks. |
| `RentRight.Tests/Api/PropertiesApiTests.cs` | Narrows property-schema exclusion assertion to its own schema now that tenancy routes exist. |
| `RentRight.Tests/SqlIntegration/SqlApplication.cs` | Captures actual test-host logs for secret exclusion assertions. |
| `RentRight.Tests/SqlIntegration/TenancyLifecycleSqlTests.cs` | Real SQL migrations, constraints, cookies, CSRF, actor/privacy/consent matrix, history and race verification. |
| `README.md` | Current invitation setup, consent change and manual delivery limitations. |
| `docs/API.md` | Routes/DTOs, status/error/privacy/expiry and concurrency contracts. |
| `docs/ARCHITECTURE.md` | Implemented lifecycle decision, schema/transaction boundaries and current-model limits. |
| `docs/PHASE1_MANUAL_VERIFICATION.md` | Marks historical direct-assignment instructions as superseded. |
| `docs/SQL_INTEGRATION_TESTS.md` | Describes new lifecycle SQL and log-capture coverage. |
| `docs/TASKS.md` | Records P05-01 completion only after final SQL verification. |
| `docs/TENANCY_LIFECYCLE.md` | Decisions, compatibility, manual checks, operational limits, verification and this inventory. |
