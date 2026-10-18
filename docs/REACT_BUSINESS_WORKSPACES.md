# React business workspaces — F01-01

The user prioritized a consistent React business interface before Docker. This
extends the existing React/TypeScript application, not a new framework or Node
backend. ASP.NET Core, Identity, the application services, EF Core and SQL Server
remain in place. There is no package or schema change.

## Screen map

| React route | Audience / behavior |
| --- | --- |
| `/app/properties` | Public paged property catalog with title/address and rent filters. |
| `/app/properties/{id}` | Public details, stored approximate location and attribution; server-derived ownership hint controls the manager link. |
| `/app/` | Existing tenant overview, or manager overview for accounts without the Tenant role. |
| `/app/manage` | Landlord's own property summary; Admin sees overall user/property counts. |
| `/app/manage/properties` | Landlord's bounded own-property list, or Admin's paged all-property list. |
| `/app/manage/properties/new` | Property creation with server-derived owner. |
| `/app/manage/properties/{id}` | Owned/Admin details, location and versioned refresh. |
| `/app/manage/properties/{id}/edit` | Versioned property edit; no ownership/coordinate input. |
| `/app/manage/properties/{id}/delete` | Explicit confirmed, versioned delete; history still prevents deletion. |
| `/app/manage/tenancies` | Paged manager-scoped tenancy/contact list. |
| `/app/manage/tenancies/new` | Invite an exact tenant login to an available owned/Admin property; receipt held only in memory. |
| `/app/manage/tenancies/{id}/end` | Explicit versioned end/cancellation, with history retained. |
| `/app/tenancies/accept` | Tenant accepts a privately received invitation token; never token-in-URL navigation. |
| `/app/manage/maintenance` | Owner/Admin queue with status, property, UTC date and page-size filters. |
| `/app/manage/maintenance/{id}` | Description/history, server-allowed transitions, priority/note and observed revision. |
| `/app/admin/users` | Admin-only paged role management with password step-up and last-usable-admin protection. |

The ordinary `/` entry redirects to the React catalog. `/Dashboard` chooses the
React manager or tenant workspace with the existing Admin-first role precedence.
Multi-role users retain tenant navigation alongside manager/Admin navigation.
Logout is an explicit CSRF-protected API POST, not a GET.

## Retained Razor boundary and limitations

- Login, registration policy, password verification, 2FA and account management
  remain Identity Razor Pages. Error/legal pages and explicit legacy MVC routes
  remain available. `.cshtml` files are deliberately not deleted or disabled.
- `/Home/Index`, explicit role-dashboard actions, `/Properties`, `/Tenancies`,
  `/MaintenanceRequest`, `/MaintenanceQueue` and `/Admin` remain compatibility
  routes. This is not a claim that every possible URL renders React.
- The existing tenant portal still requires an unambiguous single eligible tenancy
  for API submission; accounts with multiple active tenancies need the existing
  explicit MVC selection workflow or a separate reviewed API extension. This
  pre-existing limitation is not silently resolved by guessing a tenancy.
- The landlord overview/list retains its existing 100-property bound. Admin lists
  and public catalog are paged; available invitation properties retain their
  existing 200-result bound. These are not newly introduced unbounded queries.
- `/app/demo/` remains the explicitly labelled tenant-only fictional demo. This
  phase does not publish a landlord/Admin demo, host, account or new screenshots.
- Maps remain disabled until configured separately. No browser tile SDK, key or
  automatic provider lookup is added. Existing schema migrations must still be
  applied through the documented deployment process; this task applies none to
  shared/developer databases. Docker remains the next roadmap phase.

## Security and data flow

The shared React shell verifies the session and gates role-specific navigation.
Those gates are presentation only. The new `/api/v1/workspace` and `/admin-users`
controllers use named server policies and existing scoped services. Property
details are concealed across owners; tenant contact projections are manager-only.
The anonymous detail projection excludes owner IDs, rowversions and provider IDs.
Its `canManage` hint is derived server-side and is not accepted as write authority.

Unsafe requests bootstrap a fresh antiforgery token; delete/end requests send only
the allow-listed `X-Expected-Version` precondition. Forms submit dedicated inputs,
disable duplicate pending submission, abort on navigation and ignore late results.
Role/session revalidation removes private content before another read. Writes are
never replayed automatically, including conflicts and network ambiguity.

Admin changes still use the serialized existing role service, persisted actor
eligibility, role validation, security-stamp revocation and usable-last-admin
check. The recent-authentication filter retains the MVC redirect behavior but
returns a no-store API Problem Details 403 `reauthentication_required` for API
writes. React links to Identity verification with a fixed local return URL; the
user returns and explicitly submits again. Passwords never pass through React.

All API screen responses are no-store; raw dependency/Problem Details text is not
rendered. Descriptions/notes remain escaped React text. Location links are explicit
OpenStreetMap navigation with attribution and `noopener noreferrer`.

## Verification

Verified on 2026-10-18: Release solution build had zero warnings/errors; **580
backend tests** passed, **54 SQL cases** passed (`WorkspaceSqlTests`: 18,
`AccountSecuritySqlTests`: 36), and EF reported no pending model changes. Node 24
lint, type checking, all **85 frontend tests** and production build passed. No
live provider or developer database was used. Initial verification caught an old
tenant-only test expectation, a test-only unsupported option and a concurrent
build-cache collision; corrected tests and sequential .NET checks all pass.

Checks include frontend lint, strict TypeScript, production build, API transport
tests and real-cookie SQL role/resource tests. SQL tests own random disposable
databases and explicitly keep the provider disabled; they never use a developer
database. The full-story review separates browser layout evidence from backend
persistence evidence: browser smoke uses fictional, intercepted local API fixtures,
while real Identity/EF/SQL behavior is exercised by the integration suite. Fixture
interception is test-tab-only and removed afterward; it is not application code or
a production authentication bypass.

Browser checks cover the manager overview, property form submission payload and
navigation, role UI, 390px/320px layouts, Escape focus restoration and horizontal
overflow (document width equalled the viewport in both narrow checks). The
address-edit regression preserves an untouched stored address exactly during a
rent-only edit, avoiding accidental coordinate invalidation/provider lookup. They do not
claim a real-browser login/database save, live provider activation, production
deployment or screen-reader audit. The original tenant demo remains independently
usable without SQL through `npm run demo` in `ClientApp` with Node 24.

Run:

```sh
cd ClientApp
npm run lint
npm test
npm run build
```

From the repository root, run the normal Release build/test baseline and the SQL
project with a filter for `WorkspaceSqlTests` and `AccountSecuritySqlTests`. Do not
build the shared .NET project from concurrent commands in the same checkout.

## Files changed

| File | Purpose |
| --- | --- |
| `ClientApp/src/App.tsx` | Public, tenant, manager and Admin routing, session gates and sign-out. |
| `ClientApp/src/components/Workspace.tsx` | Role-specific navigation and responsive shared shell. |
| `ClientApp/src/Portal.tsx` | React acceptance link and audience-neutral forbidden message. |
| `ClientApp/src/api/client.ts` | Allow-listed version precondition header without changing request/token safety. |
| `ClientApp/src/business/ui.tsx` | Focused page/read/form/paging/location components with cancellation and fixed safe feedback. |
| `ClientApp/src/business/Properties.tsx` | Public catalog/details, manager property create/edit/delete/location workflows. |
| `ClientApp/src/business/Tenancies.tsx` | Manager list/invite/end and tenant acceptance; in-memory receipt. |
| `ClientApp/src/business/Management.tsx` | Manager/Admin overview, maintenance filtering/triage and Admin role forms. |
| `ClientApp/src/style.css` | Scoped responsive business forms, cards, tables and role controls. |
| `ClientApp/src/App.test.tsx` | Preserve stale-tenant-data assertions while supporting the new landlord destination. |
| `ClientApp/src/business/Business.test.tsx` | Role routing, ownership controls, writes, versions, consent, triage, step-up and session regressions. |
| `ClientApp/src/api/client.test.ts` | Version header is not forwarded to token bootstrap. |
| `Controllers/Api/V1/WorkspaceController.cs` | Dedicated public/manager screen projections and authorized versioned location refresh. |
| `Controllers/Api/V1/AdminUsersController.cs` | Admin-only typed role-management transport over the existing service. |
| `Controllers/Api/V1/SessionController.cs` | Explicit idempotent antiforgery-protected sign-out. |
| `Authorization/AccountSecurity.cs` | API-safe step-up error without changing MVC/Identity security rules. |
| `Controllers/HomeController.cs` | Normal root entry selects React, with explicit legacy home preserved. |
| `Controllers/DashboardController.cs` | Existing role chooser selects React workspaces. |
| `Views/Shared/_Layout.cshtml` | Identity/legacy navigation points to the React public entry. |
| `RentRight.Tests/Api/WorkspaceApiTests.cs` | API role matrix, DTO outcomes, antiforgery, step-up, logout and OpenAPI. |
| `RentRight.Tests/SqlIntegration/WorkspaceSqlTests.cs` | Real-cookie/SQL ownership, contact isolation, last-admin, disabled lookup and logout regressions. |
| `RentRight.Tests/Dashboards/DashboardControllerTests.cs` | Verify intentional React destination changes and preserve legacy action tests. |
| `RentRight.Tests/Properties/HomeControllerTests.cs` | Root redirect needs no property query; legacy latest-list test retained. |
| `README.md` | Current React/.NET description and retained boundaries. |
| `docs/API.md` | New screen endpoints, safe DTO boundaries and role/step-up/logout behavior. |
| `docs/ROADMAP.md` | User-approved React phase before Docker, with Identity retained. |
| `docs/TASKS.md` | F01-01 scope and acceptance. |
| `docs/REACT_BUSINESS_WORKSPACES.md` | Routes, limitations, verification and complete file inventory. |
