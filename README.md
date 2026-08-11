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
- Admin tenancy creation and ending
- Tenant dashboard with associated properties/tenancies
- Tenant-scoped maintenance request creation, list, and detail views
- Landlord maintenance queues and controlled status updates for owned properties
- Admin access to all maintenance requests
- Versioned `/api/v1` JSON, error, pagination, cookie, and antiforgery conventions
- Runtime OpenAPI document at `/openapi/v1.json` in Development only

## Current Gaps

The core MVC workflow is implemented. The larger roadmap still includes:

- Resource API endpoints and React client
- SQL Server-backed integration tests and broader authorization coverage
- Tenant invitation/acceptance and immutable maintenance status history
- Optimistic concurrency and pagination

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
`/openapi/v1.json`. P03-01 establishes the transport conventions but intentionally
does not add resource or smoke endpoints. See [docs/API.md](docs/API.md).

## Development Seeded Admin Account

When running in Development, the app creates an administrator only when both `SeedAdmin:Email` and `SeedAdmin:Password` are configured with user secrets. It never uses a checked-in default password.

## Role Behaviour

- Anonymous users can browse the home page, property list, and property details
- `Landlord` and `Admin` users can create properties
- `Landlord` users can edit their own properties
- `Tenant` users can view their tenancies and create/view requests only through their own active tenancy
- `Landlord` users can view and update requests only for properties they own
- `Admin` users can create/end tenancies and manage every maintenance request
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

## Notes

- Generated `bin/`, `obj/`, IDE files, and local secrets are intentionally excluded from Git
- The app uses default Identity UI routes such as `/Identity/Account/Login`
- Some UI and controller files suggest work in progress, so the `README` intentionally documents only behaviour visible in the current code
