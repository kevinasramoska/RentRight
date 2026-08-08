# RentRight

RentRight is an ASP.NET Core MVC property management application for three user roles:

- `Admin` manages users and roles.
- `Landlord` creates and manages property listings.
- `Tenant` can sign in and access a tenant dashboard placeholder.

The application uses ASP.NET Core Identity for authentication and role management, Entity Framework Core for data access, and SQL Server for persistence.

## Tech Stack

- `.NET 9`
- `ASP.NET Core MVC`
- `ASP.NET Core Identity`
- `Entity Framework Core`
- `SQL Server`
- `Razor Views`

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

## Current Gaps

These areas appear to be incomplete in the current codebase:

- `MaintenanceRequestController` is present but not implemented
- Tenant maintenance workflows are not implemented yet
- Tenant dashboard is currently a placeholder
- There are no automated test projects in the repository

## Project Structure

```text
RentRight/
├── Controllers/          MVC controllers
├── Data/                 EF Core DbContext
├── Migrations/           Entity Framework migrations
├── Models/               Domain and identity models
├── Views/                Razor views
├── wwwroot/              Static assets
├── Program.cs            App startup, DI, middleware, seeding
└── RentRight.csproj      Project file
```

## Prerequisites

Before running the app locally, make sure you have:

- `.NET 9 SDK`
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
dotnet restore
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

## Development Seeded Admin Account

When running in Development, the app creates an administrator only when both `SeedAdmin:Email` and `SeedAdmin:Password` are configured with user secrets. It never uses a checked-in default password.

## Role Behaviour

- Anonymous users can browse the home page, property list, and property details
- `Landlord` and `Admin` users can create properties
- `Landlord` users can edit their own properties
- `Admin` users can access the admin dashboard and update user roles

## Database Notes

The EF Core context is defined in [Data/ApplicationDbContext.cs](/Users/kevinasramoska/Desktop/Property Manager/RentRight/Data/ApplicationDbContext.cs) and currently includes:

- `Properties`
- `MaintenanceRequests`
- Identity tables through `IdentityDbContext<ApplicationUser>`

Existing migrations are stored in [Migrations](/Users/kevinasramoska/Desktop/Property Manager/RentRight/Migrations).

## Useful Commands

```bash
dotnet restore
dotnet build
dotnet ef database update
dotnet run
```

## Notes

- Generated `bin/`, `obj/`, IDE files, and local secrets are intentionally excluded from Git
- The app uses default Identity UI routes such as `/Identity/Account/Login`
- Some UI and controller files suggest work in progress, so the `README` intentionally documents only behaviour visible in the current code
