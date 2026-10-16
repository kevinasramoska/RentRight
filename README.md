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
