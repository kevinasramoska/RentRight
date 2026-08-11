# AGENTS.md

## Scope and intent

These instructions apply to the entire RentRight repository. Work as a senior software engineer: understand the current implementation before editing, make the smallest coherent change, preserve existing behavior unless the task explicitly changes it, and leave the repository in a verifiable state.

RentRight is a portfolio-scale ASP.NET Core MVC property-management application. Prefer clear, secure, conventional .NET code over speculative abstractions or broad rewrites.

## Repository map

- `Program.cs`: dependency injection, Identity setup, middleware, routing, and development-only role/admin seeding.
- `Controllers/`: MVC request handling and server-side authorization.
- `Models/`: domain and Identity entities, enums, validation, and workflow rules.
- `ViewModels/`: purpose-specific Razor input and presentation models.
- `Data/ApplicationDbContext.cs`: EF Core model configuration and relationships.
- `Migrations/`: generated, reviewed EF Core migrations and model snapshot.
- `Views/`: Razor views. Keep authorization enforcement on the server even when the UI hides actions.
- `wwwroot/`: application CSS, JavaScript, and static assets. Do not edit vendored files under `wwwroot/lib/`.
- `RentRight.Tests/`: xUnit tests.
- `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, and `docs/TASKS.md`: proposed direction and planning documents, not proof that a feature already exists.
- `README.md`: current setup and user-facing behavior. Update it when those facts change.

The running code, tests, and current EF model are the source of truth. If a planning document conflicts with them, verify the implementation and call out the discrepancy instead of silently coding to a stale statement.

## Required workflow

Before changing code:

1. Read the relevant controller, model, view model, view, EF configuration, and tests end to end.
2. Search the repository for the same behavior and follow existing naming and patterns.
3. Check `git status --short`; preserve all unrelated user changes.
4. State a short plan and identify any behavior, authorization, data, or migration impact.

While changing code:

- Keep edits focused on the requested outcome. Do not combine feature work with cleanup or formatting churn.
- Follow the existing architecture unless the task explicitly asks for an architectural step from the roadmap.
- Do not add packages, projects, frameworks, or generic repository/unit-of-work layers without a demonstrated need.
- Do not change public routes, form field names, database semantics, or user-visible behavior accidentally.
- Use nearby code as the formatting guide. New C# should use nullable annotations, clear names, async EF/Identity APIs, and cancellation tokens for I/O paths where practical.
- Add or update tests whenever behavior or a business rule changes. A bug fix should normally include a regression test.
- Update documentation only where the change makes it inaccurate.

After changing code:

1. Review the diff for scope, secrets, generated noise, and accidental API/schema changes.
2. Run the narrowest relevant test first, then the standard build/test checks below.
3. If a command fails, report the actual error and diagnose it before making another code change.
4. Summarize every file changed, the behavior affected, and the checks run.

## Architecture and implementation rules

- Preserve the current single-deployable ASP.NET Core application and SQL Server persistence unless explicitly asked otherwise.
- Keep controllers responsible for HTTP concerns: binding, response selection, and delegation/orchestration. When a workflow grows or must be shared, extract a focused application/query service; do not introduce abstractions preemptively.
- Keep stable domain rules close to the domain. For example, maintenance status transitions belong in the centralized transition logic, not duplicated in controllers or views.
- Use dedicated input/view models for new forms and projections. Do not bind EF Core or Identity entities directly when that permits over-posting.
- Use `AsNoTracking()` for read-only EF queries and project only the data needed when practical.
- Avoid synchronous database calls in new code. Thread `CancellationToken` through asynchronous request/database operations where it improves the path consistently.
- Do not expose tracked EF entities, Identity objects, secrets, or unnecessary internal IDs through new API/UI contracts.
- Avoid N+1 queries. Use intentional projections/includes and deterministic ordering for user-facing lists.
- Store timestamps in UTC. Use `DateOnly` for tenancy calendar dates, consistent with the existing domain.

## Authorization and security invariants

Authorization is server-side and resource-aware. A hidden button is not a security boundary.

- Roles are exactly `Admin`, `Landlord`, and `Tenant` unless a task explicitly changes the role model.
- `Admin` can manage all resources and role assignments.
- A landlord may mutate or inspect landlord-only data only for properties they own.
- A tenant may access tenancy and maintenance data only through their own currently active tenancy.
- Never trust submitted user IDs, landlord IDs, property ownership, tenancy ownership, roles, statuses, or timestamps. Resolve them from the authenticated user and persisted data.
- Preserve at least one usable administrator when changing roles, and validate every submitted role name.
- Return `Challenge`, `Forbid`, `NotFound`, and validation errors intentionally; avoid leaking whether an inaccessible resource exists when the existing endpoint treats that as hidden.
- State-changing actions must use POST/PUT/PATCH/DELETE as appropriate and remain protected by antiforgery validation. The application already installs a global MVC antiforgery filter.
- Prevent mass assignment. Allow-list changed fields through input models or explicit property assignment.
- Never commit credentials, connection strings containing passwords, tokens, certificates, local secrets, or personal data. Use .NET user secrets for local values and configuration/environment secrets for deployment.
- Do not log passwords, tokens, connection strings, antiforgery values, or unnecessary tenant/user data.

## Domain invariants to preserve

- A property has one owning landlord. Non-admin landlords can edit/delete only their own properties.
- A property with tenancy history is not hard-deleted under the current workflow.
- A tenancy links one tenant to one property, has a valid date range, and is active only when its status and dates permit it.
- The database permits at most one active tenancy per property under the current model.
- A maintenance request is created only against the authenticated tenant's active tenancy.
- Maintenance request statuses are controlled by `MaintenanceRequestStatus`; permitted transitions are centralized in `MaintenanceRequestStatusTransitions` and must be covered by tests.
- Landlords may view/update maintenance requests only for their own properties; tenants may view only their own requests; admins may access all requests.

If a requested feature conflicts with one of these rules, do not weaken the rule implicitly. Explain the conflict and make the domain change explicit, including tests and migration impact.

## EF Core and migrations

- Put schema constraints, lengths, precision, indexes, conversions, relationships, and delete behavior in `ApplicationDbContext` configuration unless the existing model clearly uses an attribute for validation.
- Treat migrations as reviewed source artifacts. Never hand-edit or delete an already-applied migration to reshape history.
- For an intentional model change, generate a new descriptively named migration, inspect both migration directions and the snapshot, and check for unrelated schema churn.
- Do not run `dotnet ef database update`, drop a database, or mutate a shared database unless the task explicitly requires it and the target is confirmed safe.
- After model changes, run the pending-model check. If it reports drift, determine whether the task requires a new migration rather than masking the result.
- Preserve SQL Server compatibility, explicit decimal precision, bounded strings, existing foreign-key behavior, and filtered-index semantics.

Useful migration commands:

```bash
dotnet ef migrations add <DescriptiveName> --project RentRight.csproj
dotnet ef migrations has-pending-model-changes --project RentRight.csproj
dotnet ef migrations script --project RentRight.csproj
```

## Razor and frontend conventions

- Keep Razor views presentation-focused; place input validation, authorization, and workflow decisions on the server.
- Use tag helpers and strongly typed view models. Avoid adding new `ViewBag`, tuple, or raw entity-based forms.
- POST forms must include antiforgery support and show validation feedback.
- Preserve accessibility: explicit labels, semantic elements, keyboard operation, visible focus, validation summaries, and meaningful link/button text.
- Reuse the existing layout and styles before adding new CSS. Keep CSS changes scoped and responsive.
- Do not modify Bootstrap, jQuery, validation libraries, or license files under `wwwroot/lib/`.

## Testing expectations

Use xUnit and follow the existing Arrange/Act/Assert style and descriptive `Member_Scenario_ExpectedResult` naming. Tests must be deterministic and independent of developer-local configuration.

At minimum, add coverage for affected behavior in these areas:

- domain validation and status/date transitions;
- role and resource authorization, including admin override and cross-user denial;
- invalid, missing, and unauthorized resource outcomes;
- over-posting and forged ownership/tenancy identifiers;
- EF constraints or query behavior when a schema/query change is introduced.

Do not replace meaningful behavioral assertions with snapshots or implementation-detail tests. Do not weaken or remove a test merely to make a change pass.

## Standard commands

Run targeted checks first. The repository's CI baseline is:

```bash
dotnet restore RentRight.Tests/RentRight.Tests.csproj
dotnet build RentRight.csproj --no-restore --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --no-restore --configuration Release
```

For a normal local verification when restore state is unknown:

```bash
dotnet build RentRight.slnx --configuration Release
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release
```

If only documentation changed, inspect the diff and validate referenced paths/commands; a full test run is optional. If tests cannot run because SQL Server, the .NET SDK, or another external dependency is unavailable, say so explicitly and report what was verified instead.

## Completion checklist

A change is complete only when:

- it satisfies the requested behavior with no unrelated edits;
- authorization and ownership are enforced server-side;
- inputs are validated and over-posting is prevented;
- domain or schema changes include appropriate tests and a reviewed migration when needed;
- relevant build/tests pass, or any limitation is reported precisely;
- no secrets or generated build output are included;
- the final summary lists each changed file and verification performed.
