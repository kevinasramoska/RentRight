# P08-01 — authorization and account security

Extends the policy foundation on merged P07-02. Implementation is in place; final
SQL regression verification is in progress. P08-02 (OIDC, headers, rate limiting
and durable data-protection deployment configuration) remains separate. No package,
schema, migration, API DTO or rental-domain rule changes were needed. No developer/
shared database was used or migrated.

## Implemented boundary

`AppRoles` defines the existing case-sensitive Admin, Landlord and Tenant names;
it does not add roles. All role-protected controllers use named audience policies,
requiring authentication, a nonblank local user ID and the specified local role.
Other stored roles cannot be offered or assigned through administration. These
audiences preserve role capabilities and cookie challenge/forbid behavior. A
manager audience alone never proves ownership.

`RentalAccess` supplies the resource decisions shared by the application services
and framework handler. Property edit/delete/presentation and maintenance detail/
transition authorization use these shared rules; EF query scopes, persisted owner
predicates, conditional writes, rowversions, tenancy checks and antiforgery remain
in place. Identity routes are preserved with focused local account-page overrides.

| Policy | Permitted access |
| --- | --- |
| `Rental.Manager` | Authenticated Admin or Landlord; endpoint audience only. |
| `Rental.Landlord` | Dedicated Landlord dashboard audience; Admin alone does not gain this capability. |
| `Rental.Tenant` | Authenticated Tenant; Admin alone does not gain tenant actions. |
| `Rental.Participant` | Authenticated Admin, Landlord or Tenant; endpoint audience only. |
| `Rental.AdministerRoles` | Authenticated Admin; mutations additionally require recent password verification and persisted actor revalidation. |
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

Dashboard routing remains Admin > Landlord > Tenant. Multi-role capabilities form
a union without bypassing ownership or consent; Admin alone cannot consent for a
tenant. Query scoping and locked lifecycle checks remain defense in depth.

## Identity and session rules

- New/changed passwords require 12 characters, four distinct characters, uppercase/
  lowercase, a digit and a symbol. Existing passwords are not rewritten.
- New users are lockout-enabled. Five failed password attempts cause a 15-minute
  lockout. Login/reauthentication count failures and use generic errors; application
  code never echoes or logs passwords.
- Application cookies are HttpOnly, Secure Always and SameSite=Lax in every
  environment: use HTTPS locally. Idle expiry is 30 minutes with sliding renewal;
  protected original-session metadata caps active sessions at eight hours.
  Remember-me affects browser persistence, not those limits.
- Every authenticated request validates persisted account existence, Identity
  security stamp, lockout state and sign-in eligibility. Database failure never
  grants access. This intentionally adds an account lookup per authenticated
  request instead of accepting stale permissions for a long validation interval.
- Permanent disable uses existing `LockoutEnabled=true` and a far-future
  `LockoutEnd`; no new disabled flag/UI is introduced. Operational disable/recovery
  must also rotate the stamp. Merely unlocking without rotating it is not a
  supported global-session-revocation operation.
- Role changes rotate the target's stamp in the same transaction. Their old
  application cookies are rejected on the next request; sign-in loads current
  local roles. No-op role submissions do not invalidate sessions. Already-running
  requests are not retroactively cancelled; role writes recheck their actor.
- **Rollout requires users to sign in again:** old cookies lack the protected
  original-session timestamp. Sliding renewal/profile refresh never resets the
  original session age or manufactures recent password verification.

The application validator checks stamps directly every request instead of renewing
the principal every request through the interval validator. Identity's stamp
validation interval is also explicitly zero for framework-managed paths. Existing
API 401/403 Problem Details and MVC login/access-denied redirects remain. No auth
token is stored in browser storage. See [Identity configuration](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0).

## Onboarding and account retention

Registration is enabled only in Development and the controlled Testing environment.
Server GET/POST gates return 404 elsewhere; navigation hiding is not authorization.
New accounts atomically receive **Tenant only** regardless of forged role/ownership/
confirmation fields. Only an Admin can grant Landlord/Admin. Creation and initial
role assignment share a transaction, so failure cannot leave partial onboarding.

Production/Staging self-registration is **closed**, not an email-confirmation flow
with a no-op sender. Existing provisioned local accounts can sign in;
`RequireConfirmedAccount=false` is explicit because email delivery is absent.
This does not claim verified email ownership. Production provisioning/recovery
needs a trusted operator process or future P08-02 onboarding; do not enable
Development mode against a production database. Automatic production bootstrap,
working recovery email and external onboarding remain deployment/onboarding gates.
Development admin seeding stays Development-only and uses external secrets that
meet the password policy.

The default registration-confirmation demo endpoint is also closed in every
environment: without real email delivery, it must not generate a public confirmation
link or distinguish existing accounts. Genuine token-based confirmation is not
replaced with an unverified shortcut.

Self-service Identity account deletion is blocked on the server for every role,
including users without business references. It cannot bypass audit retention or
last-admin protection. A future reviewed privacy/account workflow must explicitly
define deletion/anonymization and usable-admin checks; no purge schedule, legal
retention period or background job is invented.

## Sensitive role changes and concurrency

`POST /Admin/UpdateRoles` requires password verification within 10 minutes. This
timestamp is protected cookie metadata, written only by actual password login/
reauthentication, never posted fields or sliding/profile refresh. Missing/old/future
metadata redirects to `/Identity/Account/Reauthenticate` with a fixed local return
destination. That form verifies the signed-in account, counts failures and accepts
only local redirects. Success returns to Admin to review and resubmit; it never
automatically replays a sensitive POST. MFA login paths without recent-password
metadata also require this step; it does not replace MFA at initial sign-in.

Supported role mutations acquire a database-scoped, transaction-owned exclusive
`sp_getapplock` before reading user/role state, inside the serializable transaction.
The actor must still be a persisted, unlocked, password-capable Admin after
acquiring it; queued requests cannot operate from revoked cookie claims. Previously
tracked targets are reloaded. Only the three known roles are assignable, including
when an unsupported role exists in storage; malformed values fail validation.

Removing Admin must retain at least one unlocked password-capable local Admin.
Locked/disabled/passwordless accounts do not count; an unusable Admin can be removed
when a usable replacement remains. This definition matches current local sign-in
and must be extended for external-only credentials in P08-02. It cannot guarantee
availability if operators disable every admin or failed sign-ins temporarily lock
every admin; lockouts expire and trusted operator recovery must be planned.
Arbitrary privileged SQL/Identity-manager mutations are not supported alternative
role/account administration workflows.

Role adds/removals and stamp rotation commit together. Identity/constraint/
concurrency failures preserve previous roles/stamps. Lock acquisition times out
after five seconds; timeout/deadlock/conflict yields a recoverable reload/retry
message, including failure while starting the transaction. There are no blind
retries. Disposal releases locks and clears rolled-back tracked state. The DB login
needs normal `sp_getapplock` access under the default public principal; no new
schema object or application-specific SQL grant is added. See [SQL application locks](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql?view=sql-server-ver17).

## Verification

Release solution build: zero warnings/errors. **459 fast .NET tests**, **45 frontend
tests**, frontend lint/type-check/build passed, and EF reports no pending changes.
The focused account-security SQL run passed **25 checks** including the existing
last-admin regression; the additional **seven** role/locked-owner actor checks
passed. The complete SQL regression suite is running; final evidence will be
recorded before marking P08-01 complete. No shared/developer DB was used/migrated.

SQL tests use real password login, production account/cookie validation and
disposable Testcontainers databases. Fast HTTP contract tests use protected
synthetic identities and an explicit test-only session-validator double, because
they do not own persisted accounts. Real authorization/routing/antiforgery remain
enabled. No production environment bypass or weaker assertion accommodates them.

Coverage includes policy/ownership/multi-role matrices, missing identifiers/
resources, disabled/locked sessions, stamp revocation, idle/absolute expiry and
secure cookie flags, lockout, forged-role registration, closed production signup,
blocked self-deletion, recent-password verification, no automatic POST replay,
local redirects, unsupported roles, concurrent self/cross demotions, unusable other
admins, SQL lock timeout/recovery, stamp write rollback and existing workflows.

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
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release
```

Use the installed Node 24 runtime for the frontend commands; do not change the
global Node installation. The layout retains existing CRLF line endings;
whitespace validation recognizes CR-at-EOL instead of rewriting unrelated lines:
`git -c core.whitespace=blank-at-eol,blank-at-eof,space-before-tab,cr-at-eol diff --check`.
All changed files are listed below; generated frontend/build/TRX outputs remain ignored.

## Changed-file inventory

| File | Purpose |
| --- | --- |
| `Authorization/AppRoles.cs` | Exact existing role constants. |
| `Authorization/RentalAccess.cs` | Trusted resource snapshots and shared, fail-closed permission rules. |
| `Authorization/RentalAuthorization.cs` | Named audience/resource policies, requirement, handler and registration. |
| `Program.cs` | Register policies/account protections and use seed role constants. |
| `Controllers/AdminController.cs` | Named Admin policy and recent-password requirement on role writes. |
| `Controllers/PropertiesController.cs` | Use the named manager audience on existing protected MVC actions. |
| `Controllers/Api/V1/PropertiesController.cs` | Use the same manager audience on API mutations/edit reads. |
| `Controllers/MaintenanceRequestController.cs` | Use named participant/Tenant/manager audiences without changing actions. |
| `Features/Properties/PropertyService.cs` | Reuse shared ownership/Admin checks and role names. |
| `Features/Maintenance/MaintenanceCommandService.cs` | Reuse shared transition ownership checks and role names. |
| `Features/Maintenance/MaintenanceQueryService.cs` | Reuse shared maintenance read/transition checks and role names. |
| `RentRight.Tests/Authorization/RentalAuthorizationTests.cs` | Framework policy matrix, missing identity/resource, tenant eligibility and multi-role regressions. |
| `RentRight.Tests/SqlIntegration/SqlPipelineTests.cs` | Real-cookie Admin policy matrix, including MVC login/access-denied redirects. |
| `README.md` | Current HTTPS, onboarding and account behavior. |
| `docs/ARCHITECTURE.md` | Implemented authorization/account boundaries; separate P08-02 scope. |
| `docs/TASKS.md` | Accurate implementation/verification checkpoint. |
| `docs/AUTHORIZATION.md` | Policies, operational limits, verification and file inventory. |
| `Authorization/AccountSecurity.cs` | Identity/cookie configuration, session validation and protected recent-auth filter. |
| `Controllers/Api/V1/LandlordMaintenanceRequestsController.cs` | Named manager triage audience. |
| `Controllers/Api/V1/MaintenanceRequestsController.cs` | Named Tenant request audience. |
| `Controllers/Api/V1/SessionController.cs` | Supported role constants in session projection. |
| `Controllers/Api/V1/TenanciesController.cs` | Named participant/manager/Tenant tenancy audiences. |
| `Controllers/Api/V1/TenantDashboardController.cs` | Named Tenant dashboard audience. |
| `Controllers/DashboardController.cs` | Dedicated dashboard policies without broadening role capabilities. |
| `Controllers/MaintenanceQueueController.cs` | Named manager queue audience. |
| `Controllers/TenanciesController.cs` | Matching named MVC tenancy audiences. |
| `Features/Administration/AdministrationService.cs` | Supported-role validation, locked actor recheck, usable-admin decision and atomic stamp outcome. |
| `Features/Administration/IAdministrationRoleStore.cs` | Actor/target usability and stamp-rotation contracts. |
| `Features/Administration/IdentityAdministrationRoleStore.cs` | SQL role lock, usability queries, target reload and transactional stamp/cleanup. |
| `Features/Dashboards/DashboardServices.cs` | Central role names and existing routing precedence. |
| `Features/Maintenance/LandlordMaintenanceService.cs` | Central manager role names, preserving EF ownership scopes. |
| `Features/Maintenance/TenantMaintenanceService.cs` | Central Tenant role name, preserving EF tenancy scopes. |
| `Features/Tenancies/TenancyService.cs` | Central role names and nonblank authenticated identifiers. |
| `Areas/Identity/Pages/_ViewImports.cshtml` | Tag helpers for local Identity overrides. |
| `Areas/Identity/Pages/_ViewStart.cshtml` | Existing application layout for account pages. |
| `Areas/Identity/Pages/Account/Login.cshtml` | Accessible login form and onboarding guidance. |
| `Areas/Identity/Pages/Account/Login.cshtml.cs` | Failed-login lockout, generic errors, local redirects and fresh-password marker. |
| `Areas/Identity/Pages/Account/Register.cshtml` | Dedicated tenant-only development registration form. |
| `Areas/Identity/Pages/Account/Register.cshtml.cs` | Production registration gate, allow-listed input and atomic Tenant assignment. |
| `Areas/Identity/Pages/Account/RegisterConfirmation.cshtml` | Override the default demo confirmation view. |
| `Areas/Identity/Pages/Account/RegisterConfirmation.cshtml.cs` | Block public no-op-email confirmation-token generation in every environment. |
| `Areas/Identity/Pages/Account/Reauthenticate.cshtml` | Accessible verification form and explicit resubmission guidance. |
| `Areas/Identity/Pages/Account/Reauthenticate.cshtml.cs` | Verify signed-in account, count failures and refresh protected metadata. |
| `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml` | Explain retained-account workflow without a delete form. |
| `Areas/Identity/Pages/Account/Manage/DeletePersonalData.cshtml.cs` | Server-side self-deletion denial. |
| `Views/Shared/_Layout.cshtml` | Closed registration navigation and correct dashboard area links. |
| `Views/Shared/_LoginPartial.cshtml` | Same registration navigation gate in the reusable partial. |
| `RentRight.Tests/Administration/AdministrationServiceTests.cs` | Revoked actors, malformed/unsupported roles, stamp failure/no-op and usable-admin regressions. |
| `RentRight.Tests/Api/RentRightApplicationFactory.cs` | Explicit synthetic-session validator only for non-database contract tests. |
| `RentRight.Tests/Authorization/AccountSecurityTests.cs` | Settings, environment gates, timestamp bounds and refresh invariants. |
| `RentRight.Tests/SqlIntegration/SqlApplication.cs` | Optional real environment and controlled cookie time for SQL scenarios. |
| `RentRight.Tests/SqlIntegration/AccountSecuritySqlTests.cs` | Real-login session/onboarding/reauth/race/failure/antiforgery tests. |
| `docs/API.md` | Current session/CSRF/revocation semantics. |
