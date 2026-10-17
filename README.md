# RentRight

RentRight is an ASP.NET Core MVC property management application for three user roles:

- `Admin` manages users and roles.
- `Landlord` creates and manages property listings.
- `Tenant` can view associated tenancies and submit/view maintenance requests.

The application uses ASP.NET Core Identity for authentication and role management, Entity Framework Core for data access, and SQL Server for persistence.

## Tech Stack

- `.NET 10 LTS`
- `ASP.NET Core MVC`
- `ASP.NET Core Identity`
- `Entity Framework Core`
- `SQL Server`
- `Razor Views`
- Built-in ASP.NET Core OpenAPI generation

## Current Features

- Public home page with latest property listings
- Public property list and property details pages
- Authentication with built-in Identity UI
- Automatic seeding of roles: `Admin`, `Landlord`, `Tenant`
- Automatic seeding of a default admin user on startup
- Admin dashboard with total user and property counts
- Admin user role management screen
- Landlord dashboard showing the landlord's own properties
- Property create, edit, and delete flows for `Landlord` and `Admin`
- Admin/owning-landlord tenancy invitations, explicit tenant acceptance, and audited ending
- Tenant dashboard with currently active tenancies, recent requests, open counts, and 30-day submission counts
- Tenant-scoped maintenance request creation, list, and detail views plus REST endpoints
- Immutable maintenance status history with explicitly marked legacy baselines
- Landlord maintenance queues and controlled status updates for owned properties
- Admin access to all maintenance requests
- Versioned `/api/v1` JSON, error, pagination, cookie, and antiforgery conventions
- Public paged/filterable property REST list/detail plus authorized property commands
- Runtime OpenAPI document at `/openapi/v1.json` in Development only

## Current Gaps

The core MVC workflow is implemented. The larger roadmap still includes:

- Landlord maintenance API queue/filtering and React client
- Landlord transition/concurrency UI and admin triage (P05-03)

## Tenant maintenance API

Authenticated tenants use `GET /api/v1/maintenance-requests` (bounded `page`/`pageSize`),
`GET /api/v1/maintenance-requests/{id}`, and `POST /api/v1/maintenance-requests`.
POST accepts only `{ "description": "Describe the problem here" }`, with 10–2000
characters after trimming. Same-origin Identity cookies and `X-CSRF-TOKEN` protect
writes. The server derives tenancy, tenant, property relationship, UTC timestamps,
and initial `Submitted` status. Status values in responses/history are strings.
A successful POST returns `201` with an ID and a detail `Location`.

Eligibility requires an accepted, currently active tenancy. Pre-invitation legacy
active tenancies remain eligible under Phase 5.1 compatibility; consent is not
invented for those rows. No active tenancy (including pending, cancelled/declined,
expired, future, or ended tenancy) returns `409` with `no_active_tenancy`. Phase 5.1
has no distinct Declined status: cancelled invitations use `Ended`. Multiple active
tenancies return `409 resource_conflict`; use the existing Razor property selector
in that case. Valid repeated descriptions create separate requests: there is no
stable business key for duplicate suppression or automatic POST retry.

Tenant lists are empty without an eligible tenancy; inaccessible and missing IDs
both return `404 resource_not_found`, including requests from ended tenancies.
Invalid input/paging returns `400`; unauthenticated/wrong-role callers receive
`401`/`403`. Database outages return `503 service_unavailable`; persistence/racing
state conflicts return `409 resource_conflict`. Errors use the shared Problem
Details contract without submitted descriptions or dependency details.

The dashboard shows up to five recent requests. Open means `Submitted` or
`InProgress`; recent counts submissions in the preceding 30 days, both scoped to
currently eligible tenancies. History is ordered by UTC event time then event ID.
Tenants read it in API details or Razor; owning landlords/admins use Razor details.
Existing Razor status updates remain available and append an event atomically;
no new landlord mutation API is introduced here.

Apply `MaintenanceStatusHistory` through the normal migration workflow. It adds a
status-event table and one marked baseline for each existing request at its last
known update time (never before submission), without fabricating prior transitions.
New requests and their initial events commit together. Application saves reject
updates/deletes of history. Rollback refuses to discard newly recorded events;
export/archive history before planning a downgrade. No existing migration is edited.

## Project Structure

```text
RentRight/
├── Api/                  Shared API, Problem Details, and OpenAPI conventions
├── Controllers/          MVC controllers
├── Data/                 EF Core DbContext
├── Migrations/           Entity Framework migrations
├── Models/               Domain and identity models
├── ViewModels/           MVC input and presentation models
├── Views/                Razor views
├── RentRight.Tests/      xUnit tests
├── wwwroot/              Static assets
├── Program.cs            App startup, DI, middleware, seeding
└── RentRight.csproj      Project file
```

## Tests and Coverage

The deterministic xUnit suite exercises domain rules, application-service outcomes,
authorization decisions, boundary validation, and cancellation-token forwarding. It
does not require SQL Server, Docker, or network access:

```bash
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release
```

Collect production-assembly coverage with the .NET 10 Microsoft coverage collector:

```bash
dotnet test RentRight.Tests/RentRight.Tests.csproj \
  --configuration Release \
  --settings coverage.runsettings \
  --collect:"Code Coverage;Format=Cobertura" \
  --results-directory TestResults/Coverage
```

The Cobertura artifact is written below `TestResults/Coverage/`. The checked-in
settings exclude generated migrations, build output, and static assets. Coverage is
used as diagnostic evidence rather than a repository-wide percentage gate; real
HTTP, Identity, migration, and SQL Server behavior belongs to the P04-02 integration
suite.

## Prerequisites

Before running the app locally, make sure you have:

- `.NET 10 SDK` (the repository-pinned version in `global.json`)
- `SQL Server` available locally

The default connection string expects SQL Server on `localhost,1433`.

## Configuration

The app reads the default database connection from [appsettings.json](/Users/kevinasramoska/Desktop/Property Manager/RentRight/appsettings.json).

The checked-in value uses Windows authentication. Override it with a local .NET user secret if your SQL Server requires a different connection string:

```json
"DefaultConnection": "Server=localhost,1433;Database=RentRightDB;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
```

From the project directory, configure local secrets without committing them:

```bash
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=RentRightDB;User Id=sa;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=True;"
dotnet user-secrets set "SeedAdmin:Email" "admin@example.test"
dotnet user-secrets set "SeedAdmin:Password" "use-a-strong-local-password"
```

## Getting Started

1. Restore dependencies:

```bash
dotnet restore RentRight.slnx
```

2. Apply the database migrations:

```bash
dotnet ef database update
```

3. Run the application:

```bash
dotnet run
```

4. Open the app in your browser.

By default, local launch settings use:

- `https://localhost:7216`
- `http://localhost:5022`

When running in Development, the generated API contract is available at
`/openapi/v1.json`. The property REST resource is available at `/api/v1/properties`;
see [docs/API.md](docs/API.md) for filters, schemas, authorization, antiforgery, and
Problem Details behavior.

## Development Seeded Admin Account

When running in Development, the app creates an administrator only when both `SeedAdmin:Email` and `SeedAdmin:Password` are configured with user secrets. It never uses a checked-in default password.

## Role Behaviour

- Anonymous users can browse the home page, property list, and property details
- `Landlord` and `Admin` users can create properties
- `Landlord` users can edit their own properties
- `Tenant` users can view their tenancies and create/view requests only through their own active tenancy
- `Landlord` users can view and update requests only for properties they own
- `Admin` and owning `Landlord` users can invite/end tenancies; only the invited `Tenant` can accept. Admin can manage every maintenance request
- `Admin` users can access the admin dashboard and update user roles

## Database Notes

The EF Core context is defined in [Data/ApplicationDbContext.cs](/Users/kevinasramoska/Desktop/Property Manager/RentRight/Data/ApplicationDbContext.cs) and currently includes:

- `Properties`
- `Tenancies`
- `MaintenanceRequests`
- Identity tables through `IdentityDbContext<ApplicationUser>`

Existing migrations are stored in [Migrations](/Users/kevinasramoska/Desktop/Property Manager/RentRight/Migrations).

## Useful Commands

```bash
dotnet restore RentRight.slnx
dotnet build RentRight.slnx --configuration Release
dotnet test RentRight.slnx --configuration Release
dotnet ef database update
dotnet run
```

SQL Server-backed HTTP tests are a separate explicit command; see [SQL integration tests](docs/SQL_INTEGRATION_TESTS.md) for Docker prerequisites, isolation and CI execution.

```bash
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release
```

## Deployment and MVC/API routing

See [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md) for the .NET 10 container, SQL Server,
Identity key storage, HTTPS proxy requirements, and demo promotion checks.

API controllers have the `Api` area route value to isolate link generation from
MVC controllers with the same name. Public API URLs remain `/api/v1/...`; Razor
property forms post to `/Properties/Create` and retain antiforgery protection.

## Repository notes

- Generated `bin/`, `obj/`, IDE files, and local secrets are intentionally excluded from Git
- The app uses default Identity UI routes such as `/Identity/Account/Login`
- Some UI and controller files suggest work in progress, so the `README` intentionally documents only behaviour visible in the current code

## Tenancy invitations (P05-01)

Use `/Tenancies` as Admin or an owning Landlord to invite by the tenant's **exact
login identifier** (currently their registration email). The account must already
have the Tenant role. A successful invitation shows a random token once for
private manual delivery; no email is sent. The intended tenant signs in and pastes
it at `/Tenancies/Accept` within seven days. Occupancy becomes Active only after
explicit acceptance. Future Active tenancies reserve the property but do not grant
date-based occupancy access before their start date.

An invitation receipt deliberately does not confirm account existence or role.
Unknown/ineligible identifiers still reserve the property until expiry/cancellation;
check the identifier privately with the intended tenant, cancel a mistaken invite,
and issue another. Tokens cannot be recovered; cancel/reissue if lost. Store or
transmit them only through a private channel. Never place them in a query string.
Lists/detail never return a token, hash, or invitation target identity.

The incremental `TenancyInvitationLifecycle` migration preserves existing Active
and Ended rows, with null lifecycle audit fields where no historical evidence
exists. New rows record invitation, acceptance and ending actors/UTC times.
Rollback refuses to discard lifecycle audit data. All extant properties are
eligible subject to reservation rules; this model has no archive/publication state.
See [lifecycle decisions and manual checks](docs/TENANCY_LIFECYCLE.md) and
[API contracts](docs/API.md). Maintenance submission/history remains outside P05-01.

## Landlord maintenance triage (P05-03)

Landlords and admins can open `/MaintenanceQueue` (linked from the retained
maintenance list) to filter by status, property number and inclusive UTC submission
dates. Pages contain 1–100 requests, ordered by submission time and request number,
both descending. Owners/admins retain operational access after a tenancy ends;
tenant reads still require their own currently active tenancy.

`GET /api/v1/landlord-maintenance-requests` exposes the same filtered queue;
`GET /api/v1/landlord-maintenance-requests/{id}` includes priority, revision,
allowed next statuses and ordered audit history. Non-owner resources are concealed
as 404. Admin takes precedence over other roles. The tenant API is unchanged except
for additive previous-status/note history fields; actor identifiers are available
only on authorized operational reads.

`POST /api/v1/landlord-maintenance-requests/{id}/triage` accepts `expectedRevision`
(required), optional `newStatus`, `priority` and `note` (at most 1000 characters).
Status names are `Submitted`, `InProgress`, `Resolved`, `Closed`; priorities are
`Low`, `Normal`, `High`, `Urgent`. Defined numeric enum values are also accepted.
A status change must follow the centralized transition rules. Omit `newStatus`
(and any note) to change priority alone. No-op updates and notes without a status
change are rejected. Cookie mutations require the existing CSRF header/token.

Read the revision from detail or the rendered update form. A competing change
returns 409; reload before trying again. Every accepted change increments the
maintenance-only revision through a conditional SQL update that also rechecks
ownership. Status changes append one event in the same transaction; failed or
stale writes commit neither priority/status nor an event. Priority-only changes
advance the revision without inventing a status transition. A status-only check
would miss competing priority edits and reopened status cycles; the scalar revision
closes those gaps using the existing conditional-update approach, without adding
rowversion to other aggregates.

The incremental `MaintenanceTriageAudit` migration defaults existing priorities to
Normal and revisions to zero. Historical actor/previous-status/note fields remain
null because they were not recorded; migration baselines retain their original
meaning. New submissions record the tenant actor with no previous status. New
transitions record authenticated actor, previous/new status, server UTC time and
optional note. Actor IDs are retained as historical facts without a user-delete
cascade. Downgrade refuses to discard recorded triage/audit data. Descriptions and
notes render as encoded text. Phase 7 query/index measurement remains deferred.

## React tenant portal (P06-01 / P06-02)

The authenticated tenant portal lives at `/app/`; Razor, Identity and `/api/v1`
retain their existing routes and server authorization. Overview shows accepted,
currently active tenancies, open and 30-day request counts, and recent requests.
`/app/requests` lists ten requests per page; `/app/requests/{id}` shows the request,
priority and chronological status history in UTC, including legacy baselines and
bounded notes. `/app/requests/new` validates a trimmed 10–2000 character description
and opens the created detail after a successful submission. Priority is read-only.

`GET /api/v1/tenant-dashboard` adapts the existing tenant dashboard query service;
it returns property titles/addresses, tenancy dates, counts and recent request
links, without tenancy, property, user or landlord identifiers. The tenant
maintenance read projection now also includes `priority`. No schema changes.

Navigation uses the browser URL/history and fresh requests, with no shared data
cache. Session rechecks on navigation, window focus and returning to a visible tab
clear the portal first; hiding the tab also clears tenant UI and unsaved form data.
Anonymous, wrong-role and failed session responses cannot retain tenant content.
Read failures offer retry; failed submissions preserve text until navigation and
warn that a network failure may follow a successful save, so check the list before
retrying. Multiple active tenancies require resolution rather than guessing a
property. API ProblemDetails fields select fixed client messages; raw server
messages, traces and exceptions are never displayed or logged.

The layout uses semantic navigation/main landmarks, a skip link, focused page
headings, visible field labels, linked validation summary, field errors, live
status announcements, visible focus and wrapping mobile layouts. Component tests
cover the complete submission/navigation journey, errors, cancellation, session
changes and escaped text. The HTTP smoke covers the real static host, cookie,
session, dashboard, antiforgery and maintenance contracts with application-service
test doubles; the separate SQL suite covers real persistence and tenant scoping.
This combines component integration and WebApplicationFactory coverage; it is not
a real-browser end-to-end or screen-reader test. Narrow/wide layout review is by
component/CSS inspection; real-device and assistive-technology checks remain manual.
Node **24 LTS** and npm are required for frontend development and `dotnet publish`.
Dependencies use exact versions and a committed lockfile. No database changes are
required by this phase. The existing Dockerfile has no Node runtime and is not yet
compatible with this publish step; container integration remains deferred.

### Local ASP.NET + Vite workflow

Keep your existing SQL Server/user-secrets setup. From the repository root, prepare
a trusted local HTTPS certificate **outside the checkout** (the PEM private key is
unencrypted, so protect its directory and never commit it):

```sh
mkdir -p "$HOME/.aspnet/https"
chmod 700 "$HOME/.aspnet/https"
dotnet dev-certs https --trust
dotnet dev-certs https --export-path "$HOME/.aspnet/https/rentright.pem" --format Pem --no-password
chmod 600 "$HOME/.aspnet/https/rentright.pem" "$HOME/.aspnet/https/rentright.key"
npm ci --prefix ClientApp
npm run build --prefix ClientApp
dotnet run --launch-profile https
```

In a second terminal, from the repository root:

```sh
export RENTRIGHT_DEV_CERT="$HOME/.aspnet/https/rentright.pem"
export RENTRIGHT_DEV_KEY="$HOME/.aspnet/https/rentright.key"
export NODE_EXTRA_CA_CERTS="$RENTRIGHT_DEV_CERT"
npm run dev --prefix ClientApp
```

Open `https://localhost:5173/app/` and sign in **through this origin**. Vite binds to
loopback only and proxies API, Identity, existing MVC navigation and their assets
to `https://localhost:7216`, retaining the request host. Both hops use HTTPS and
upstream certificate validation stays enabled; no permissive CORS is installed.
Cookies on localhost are shared across ports; keep the same hostname throughout.
Vite refuses to start without the certificate paths. Never place secrets in
`VITE_*` variables: those values become public browser code.

For certificate errors, verify the exported certificate matches the certificate
used by ASP.NET, trust it in your browser, and restart Vite after setting
`NODE_EXTRA_CA_CERTS`. Do not disable TLS verification. Check that ASP.NET is
listening on port 7216 and port 5173 is free. For stale CSRF failures after changing
accounts, refresh the page; unsafe API requests bootstrap a fresh request token
before each mutation. The token stays in memory only. Authentication remains the
server's HttpOnly Identity cookie; the client stores no authentication tokens.

### Checks and production publish

```sh
npm ci --prefix ClientApp
npm run lint --prefix ClientApp
npm run type-check --prefix ClientApp
npm test --prefix ClientApp
npm run build --prefix ClientApp
npm audit --prefix ClientApp
dotnet build RentRight.slnx --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release
dotnet publish RentRight.csproj --configuration Release --output ./artifacts/publish
```

Run the frontend build before the .NET HTTP suite: its smoke test loads the real
host and hashed assets. CI performs the frontend checks before backend checks.
`dotnet publish` automatically runs `npm ci` and the strict frontend production
build, then includes `wwwroot/app/index.html` and hashed JS/CSS under
`wwwroot/app/assets/`. Normal .NET builds do not invoke npm. Generated frontend
output and node_modules are ignored. Publish output should go outside the checkout
(or an ignored directory); `artifacts/` is ignored for the command above.

Only extensionless `/app` routes fall back to the React host. Missing assets return
404; routes outside `/app` keep MVC/API/Identity behavior. No client router, state
library or UI framework is introduced. React Testing Library and jsdom are
build-time dependencies for semantic rendering, retry and escaping tests; Vitest,
ESLint/typescript-eslint, TypeScript, Vite and its React plugin support the required
checks and build. Production dependencies are only React and React DOM.

`GET /api/v1/session` returns `{ "isAuthenticated": false, "roles": [] }` for
anonymous users; authenticated responses include only the known presentation roles
(`Admin`, `Landlord`, `Tenant`). Both it and `GET /api/v1/session/antiforgery` are
no-store. The latter sets the HttpOnly antiforgery cookie and returns only
`requestToken`; unsafe API methods must submit it as `X-CSRF-TOKEN`. Roles in React
choose presentation only; existing server authorization remains authoritative.

### SQL query measurements

P07-01's opt-in disposable SQL benchmark, synthetic dataset, observed plans/reads,
and index tradeoffs are documented in [Query performance](docs/performance/QUERY_PERFORMANCE.md).
It runs separately from normal startup and test timing assertions.
