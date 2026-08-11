# RentRight development task list

This list is ordered by dependency and risk. Complete the first section before adding major user-facing features.

## 1. Stabilize the application

- [x] Exclude generated build, IDE, and local-secret files from Git.
- [x] Remove unused Razor Pages code-behind files from MVC views.
- [x] Protect property edits and deletion with server-side ownership checks.
- [x] Remove the hard-coded seeded administrator password from source control.
- [ ] Configure a local SQL Server connection and development seed administrator with .NET user secrets.
- [x] Add a CI workflow that restores, builds, and runs tests on every pull request.

## 2. Complete the domain model

- [ ] Define a tenancy/lease model linking a tenant to a property with start/end dates and status.
- [ ] Replace free-text maintenance-request status with a controlled enum and define permitted transitions.
- [ ] Add database constraints, field lengths, indexes, timestamps, and concurrency protection where appropriate.
- [ ] Create and apply a migration for each approved model change; never edit an applied migration.

## 3. Deliver core workflows

- [ ] Implement tenant maintenance-request create/list/detail flows, scoped to an active tenancy.
- [ ] Implement landlord maintenance-request triage and status updates for their own properties.
- [ ] Finish the tenant dashboard with tenancy and maintenance-request summaries.
- [x] Replace `ViewBag` and tuple-based dashboard/admin views with dedicated view models.
- [ ] Add pagination, filtering, and an explicit published/available state for public property listings.

## 4. Improve security and operations

- [x] Define a role-assignment policy: validate submitted role names and preserve at least one Admin account.
- [ ] Add audit records for role and maintenance-status changes.
- [ ] Add rate limiting and production authentication/account-confirmation settings.
- [ ] Store production connection strings and seed credentials in deployment secrets, never appsettings files.
- [ ] Add structured logging, health checks, and a production database-migration deployment strategy.

## 5. Establish quality controls

- [ ] Expand the test project to cover authorization, property ownership, role management, and maintenance status transitions.
- [ ] Add integration tests using an isolated database/container.
- [ ] Add analyzers/formatting checks and enforce zero warnings in CI.
- [ ] Add a contribution guide covering migrations, secrets, local setup, and pull-request checks.
