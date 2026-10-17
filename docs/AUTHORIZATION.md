# P08-01 — authorization foundation (in progress)

This is the first, deliberately bounded P08-01 change, based on the merged
P07-02 implementation. **P08-01 and Phase 8 are not complete.** Identity settings,
onboarding, account-state invalidation, sensitive-action reauthentication and
concurrent usable-last-admin protection still require implementation and tests.
OIDC/browser-boundary work belongs to P08-02 and has not started.

## Implemented boundary

`AppRoles` defines the existing case-sensitive Admin, Landlord and Tenant names;
it does not add roles or assign them. Named endpoint policies replace role strings
on the property MVC/API controllers, MVC maintenance controller and Admin controller.
These audience checks preserve the previous role requirements and cookie challenge/
forbid behavior. A manager audience policy alone never proves ownership.

`RentalAccess` supplies the resource decisions shared by the application services
and framework handler. Property edit/delete/presentation and maintenance detail/
transition authorization use these shared rules; EF query scopes, persisted owner
predicates, conditional writes, rowversions, tenancy checks and antiforgery remain
in place. No service constructor, route, form, public DTO, database model, migration
or package dependency changes.

| Policy | Permitted access |
| --- | --- |
| `Rental.Manager` | Authenticated Admin or Landlord; endpoint audience only. |
| `Rental.Tenant` | Authenticated Tenant; Admin alone does not gain tenant actions. |
| `Rental.Participant` | Authenticated Admin, Landlord or Tenant; endpoint audience only. |
| `Rental.AdministerRoles` | Authenticated Admin; existing service role validation and last-admin checks remain. |
| `Rental.ManageProperty` | Identified Admin, or identified Landlord matching the persisted owner. |
| `Rental.ParticipateInTenancy` | Same manager rule, or identified Tenant matching a currently active tenancy. Read participation only, not consent for someone else. |
| `Rental.ReadMaintenance` | Same manager rule, or identified Tenant matching the request's currently active tenancy. |
| `Rental.TransitionMaintenance` | Same manager rule, never Tenant-only access. Transition validity is still a domain rule. |

Resource policies require the correct typed resource. Null/wrong resources fail
closed even for Admin. Resource checks also reject anonymous actors and blank/missing
user IDs. Ownership comparisons are ordinal and case-sensitive. Admin overrides
landlord ownership; a Landlord+Tenant retains the union of legitimate capabilities,
not permission to mutate another landlord's request. Owners/Admin retain history
access; tenant read access does not survive an ended/ineligible tenancy.

The framework resource handler is registered for imperative `IAuthorizationService`
calls with **server-loaded facts**, never raw submitted ownership/active flags.
Do not put these resource policies on an attribute expecting MVC to load the entity.
Production property/maintenance services currently call the same shared decision
functions directly. Tenancy-store query scoping and invitation acceptance are
unchanged; the tenancy resource handler is available/tested but does not replace
those locked lifecycle checks. This follows the framework's distinction between
endpoint and [resource-based authorization](https://learn.microsoft.com/en-us/aspnet/core/mvc/security/authorization/resource-based?view=aspnetcore-10.0).

## Remaining P08-01 work

- Incremental policy/role-constant adoption in the remaining tenancy, dashboard,
  tenant API and landlord queue surfaces; retain database scoping throughout.
- Explicit environment-specific password, lockout, cookie/security-stamp and
  account-state behavior, including existing-session invalidation tests.
- A production-safe onboarding choice (verified email path or disabled production
  self-registration), without silently promoting new accounts or external claims.
- Serialized usable-last-admin protection, including SQL deadlock/failure and
  concurrent HTTP tests; invalidate role-changed sessions deliberately.
- Recent authentication for sensitive role/account changes, with usable recovery.
- Complete disabled/locked-user, cookie-expiry, registration privilege-escalation
  and full sensitive-action HTTP matrices before declaring P08-01 complete.

## Verification

Targeted policy/property/maintenance/API suite: 165 passed, including 28 new
framework authorization tests. Release build: zero warnings/errors.

An initial full fast run had 429 passed/1 failed: the existing portal smoke test
ran before the fresh worktree's React assets finished building and got an empty
host response. Building assets first and rebuilding the host made that isolated
smoke test pass without changing its assertions or application code.

The subsequent full fast suite passed **430/430** and EF reports no pending model
changes. Node 24 frontend install, lint, type-check/build and all **45 frontend
tests** passed; the clean install reported zero dependency vulnerabilities.
The focused disposable SQL run passed **45/45**, including the new six-actor Admin
policy matrix, existing property create/edit/delete ownership matrices, maintenance
history ownership and antiforgery regressions, migration/model checks and the
existing last-admin regression. This is not the complete SQL suite or evidence
that concurrent usable-last-admin hardening is finished. No shared/developer
database migration was performed.

Commands (2026-10-17):

```bash
# Build ClientApp first so the host smoke test has its static entry point.
cd ClientApp
npm ci
npm run lint
npm run build
npm test -- --maxWorkers=1
cd ..
dotnet build RentRight.slnx --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --configuration Release --no-build
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --filter 'FullyQualifiedName~SqlPipelineTests|FullyQualifiedName~Property_EditResource_OnlyOwnerOrAdminReceivesPersistedVersion|FullyQualifiedName~Detail_EndedTenancy_EnforcesOwnerAndAdminMatrix|FullyQualifiedName~Update_WithoutAntiforgery_RejectsApiAndRazor'
```

Use the installed Node 24 runtime for the frontend commands; do not change the
global Node installation. `git diff --check` passed. All 17 changed files are
listed below; no generated frontend/build assets are included.

## Changed-file inventory

| File | Purpose |
| --- | --- |
| `Authorization/AppRoles.cs` | Exact existing role constants. |
| `Authorization/RentalAccess.cs` | Trusted resource snapshots and shared, fail-closed permission rules. |
| `Authorization/RentalAuthorization.cs` | Named audience/resource policies, requirement, handler and registration. |
| `Program.cs` | Register the authorization foundation. |
| `Controllers/AdminController.cs` | Use the named role-administration audience policy. |
| `Controllers/PropertiesController.cs` | Use the named manager audience on existing protected MVC actions. |
| `Controllers/Api/V1/PropertiesController.cs` | Use the same manager audience on API mutations/edit reads. |
| `Controllers/MaintenanceRequestController.cs` | Use named participant/Tenant/manager audiences without changing actions. |
| `Features/Properties/PropertyService.cs` | Reuse shared ownership/Admin checks and role names. |
| `Features/Maintenance/MaintenanceCommandService.cs` | Reuse shared transition ownership checks and role names. |
| `Features/Maintenance/MaintenanceQueryService.cs` | Reuse shared maintenance read/transition checks and role names. |
| `RentRight.Tests/Authorization/RentalAuthorizationTests.cs` | Framework policy matrix, missing identity/resource, tenant eligibility and multi-role regressions. |
| `RentRight.Tests/SqlIntegration/SqlPipelineTests.cs` | Real-cookie Admin policy matrix, including MVC login/access-denied redirects. |
| `README.md` | Link this implementation boundary and outstanding work. |
| `docs/ARCHITECTURE.md` | Record current shared authorization boundary without marking planned hardening complete. |
| `docs/TASKS.md` | Mark P08-01 as in progress, not complete. |
| `docs/AUTHORIZATION.md` | Policies, scope, outstanding work, verification and this inventory. |
