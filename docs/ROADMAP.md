# RentRight engineering roadmap

Status: proposed

Repository inspected: 2026-08-11

Scope: one ASP.NET Core modular monolith, one SQL Server database, Razor plus a focused React client

## Executive direction

RentRight should evolve by completing its core rental domain first, then adding testable application services, a versioned HTTP API, a focused React tenant experience, and production delivery concerns. The goal is engineering depth around a coherent property/tenancy/maintenance workflow, not a large catalogue of unrelated features.

The recommended target remains a single deployable application. ASP.NET Core hosts MVC, Identity, the REST API, and the built React assets. EF Core remains the data-access technology and SQL Server remains the system of record. This is enough to demonstrate full-stack and cloud engineering without introducing distributed-system complexity.

The repository now targets .NET 10, the active LTS release through November 2028. This replaces the original .NET 9 STS baseline before substantial feature work. See the [official .NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).

## Inspection summary

### Current repository shape

- One root web project, `RentRight.csproj`, and one xUnit project, both targeting `net10.0`, with `RentRight.slnx` as the solution entry point.
- MVC controllers for home, dashboard, admin, properties, and an empty maintenance-request controller.
- Razor views for public property browsing, property CRUD, dashboards, and admin role management.
- `ApplicationDbContext` inherits from `IdentityDbContext<ApplicationUser>` and exposes `Properties` and `MaintenanceRequests`.
- One initial EF Core migration creates Identity, property, and maintenance-request tables.
- ASP.NET Core Identity uses the built-in UI, local cookies, three seeded roles, and an optional development-only seeded administrator supplied through configuration.
- Property create/edit/delete is role-gated. Landlords are checked against `LandlordId` for edit and delete; admins bypass ownership checks.
- The tenant dashboard is a placeholder. There is no tenancy/lease relationship, so the system cannot prove which property a tenant may raise a request for.
- `MaintenanceRequest` exists, but its controller has no MVC base class, actions, authorization, service, or views.
- Controllers query EF Core and Identity directly; there is no application/service boundary or API.
- There is no React client, Docker configuration, Azure infrastructure, third-party client, or production telemetry.

### Important worktree qualification

The user-provided baseline says there are no automated tests. The inspected working tree already contains uncommitted work that adds:

- `RentRight.Tests` with two passing xUnit model tests;
- `.github/workflows/ci.yml`;
- dedicated admin view models;
- global MVC antiforgery validation and role-management hardening.

These files should be reviewed and committed as an intentional baseline rather than recreated. The test command currently passes two tests. The roadmap treats this work as present but not yet established repository history.

### Verified technical findings

- `dotnet test RentRight.slnx --configuration Release` passes 13/13 tests on .NET 10.
- `dotnet ef migrations has-pending-model-changes` reports no model drift.
- `Property.RentAmount` has explicit decimal precision in the EF Core model.
- ASP.NET Core and EF Core package versions are aligned on the supported .NET 10 patch line.
- Removing the unused scaffolding dependency eliminates the previously reported transitive `Microsoft.Build` and NuGet advisories; the solution vulnerability audit is clean.
- Property and maintenance text columns are mostly `nvarchar(max)` and domain indexes are limited to foreign-key indexes.
- Public property queries are unpaged and tracked. The admin user/role page performs an Identity role lookup per user.
- Controllers lack cancellation tokens and mostly expose EF entities directly to Razor model binding.
- Property details and list views expose raw landlord IDs and show create/edit/delete links regardless of the visitor's permissions, although controller authorization blocks the actions.
- A property with dependent maintenance history cannot be deleted under the current `NoAction` relationship; the UI/controller does not handle that failure or define archive semantics.
- Role strings and ownership decisions are distributed across controllers and views. Multi-role behavior is not explicitly defined.
- New registrations receive no application role and land on the generic dashboard. `RequireConfirmedAccount` is disabled.
- The admin role update ignores `IdentityResult` failures, has an N+1 query pattern, and its last-admin check is vulnerable to concurrent updates.
- Configuration has no committed password, but the default connection string is machine-specific, enables `TrustServerCertificate`, and is unsuitable as a production default.
- There is no validated options model, health endpoint, database readiness check, rate limiting, structured domain-event logging, or distributed data-protection-key configuration.
- The layout loads Bootstrap Icons twice from a CDN and has no integrity metadata. The privacy page is still template text.

## Guiding engineering decisions

1. **Modular monolith, not microservices.** Keep one process and database. Organize by feature and add application-service seams inside the current solution.
2. **EF Core is already the repository/unit of work.** Do not add generic repository and unit-of-work wrappers. Application services should use the DbContext through focused interfaces or direct scoped injection where testing remains practical.
3. **Complete tenancy before maintenance UI.** A maintenance request must be authorized through an active tenancy and property ownership, not from a client-supplied tenant/property pair.
4. **Use explicit request/response contracts.** MVC input models and API DTOs must prevent over-posting and avoid serializing tracked entities or Identity objects.
5. **Use same-origin cookie authentication.** Host React from ASP.NET Core and call the API on the same origin. Protect unsafe API calls with antiforgery tokens. Do not build a custom token issuer.
6. **Migrate presentation without rewriting security.** React started with the tenant portal. The user-approved React business-workspace phase below now moves public/manager/admin presentation before Docker. Identity, error/legal pages and explicit MVC compatibility routes remain Razor; backend authorization and services are retained.
7. **Make authorization resource-aware.** Roles establish broad capability; handlers/services decide whether the current user owns the property or participates in the tenancy.
8. **Use migrations as reviewed artifacts.** Never edit an applied migration. Resolve current model drift, generate incremental migrations, inspect SQL, and run migrations as a deployment step rather than blindly on application startup.
9. **Measure before indexing.** Add indexes for observed query shapes and verify query plans. Avoid speculative indexes on every field.
10. **Integrate one useful external service.** Geocoding and map coordinates add real value to property data and demonstrate typed `HttpClient`, timeouts, resilience, caching, options validation, and graceful degradation.
11. **Keep AI advisory and optional.** If added, AI may suggest category/priority or summarize a request, but never silently change workflow state, make tenancy decisions, or receive unnecessary personal data.

## Technical debt versus new features

### Technical debt and correctness work

- Reconcile the uncommitted test/CI/view-model work and add a solution-level build entry point.
- Upgrade from .NET 9 to .NET 10 LTS and align Microsoft package versions.
- Resolve EF model drift and decimal precision warnings.
- Add length, precision, timestamp, relationship, delete, and concurrency rules.
- Replace entity binding with purpose-specific input models/DTOs.
- Centralize property and maintenance ownership rules.
- Normalize HTTP outcomes (`Challenge`, `Forbid`, `NotFound`, validation, conflict).
- Fix admin role-management result handling, last-admin concurrency, pagination, and query behavior.
- Remove permission-inappropriate UI links and raw internal identifiers.
- Validate configuration and separate development convenience from production settings.
- Add tests, logging, error contracts, health checks, Docker, CI quality gates, and deployment documentation.
- Correct README claims, absolute local file links, template privacy content, duplicate CDN references, and current setup instructions.

### New product capabilities

- Tenancy/lease lifecycle linking a tenant to a landlord-owned property.
- Tenant dashboard with active tenancy and maintenance summaries.
- Tenant maintenance submission/list/detail and landlord triage/status history.
- Versioned REST API with OpenAPI documentation.
- React and TypeScript tenant portal.
- External OpenID Connect sign-in/account linking.
- Property geocoding/map enrichment through one external provider.
- Optional AI-assisted maintenance categorization/summarization.

## Phased delivery plan

Each phase should be delivered as one or more small pull requests. Detailed implementation and acceptance criteria are in [TASKS.md](TASKS.md).

### Phase 1 — Existing domain completion

**Tasks:** P01-01 through P01-03

**Outcome:** a supported, reproducible baseline with explicit tenancy and maintenance invariants, a migration that matches the model, and coherent property lifecycle behavior.

**Exit gate:** solution builds on .NET 10, tests pass, EF reports no pending model changes, and ownership/delete behavior is covered by tests.

### Phase 2 — Application/service architecture

**Tasks:** P02-01 through P02-02

**Outcome:** thin controllers call cohesive application services; domain decisions, current-user access, time, validation, and mappings have test seams.

**Exit gate:** existing MVC behavior is preserved and controller actions no longer own business workflows.

### Phase 3 — REST API

**Tasks:** P03-01 through P03-02

**Outcome:** a documented `/api/v1` surface exposes property queries and authorized commands using DTOs, pagination, validation, and RFC 7807 errors.

**Exit gate:** OpenAPI is generated, API contracts are integration-tested, and no EF/Identity entity is returned directly.

### Phase 4 — Automated testing

**Tasks:** P04-01 through P04-02

**Outcome:** xUnit covers domain/service rules and `WebApplicationFactory` tests exercise the real HTTP/auth/SQL Server boundaries.

**Exit gate:** critical authorization and workflow paths fail the build when regressed; tests are deterministic and isolated.

### Phase 5 — Tenant and maintenance workflows

**Tasks:** P05-01 through P05-03

**Outcome:** landlords/admins establish tenancies, tenants see their rental and submit requests, and landlords triage requests only for owned properties.

**Exit gate:** the end-to-end tenancy-to-maintenance lifecycle works through services and API/MVC contracts with audited status changes.

### Phase 6 — React + TypeScript frontend

**Tasks:** P06-01 through P06-02

**Outcome:** a Vite-based React tenant portal consumes the same-origin API with typed contracts, accessible UI states, and frontend tests.

**Exit gate:** the tenant maintenance journey works in React while existing Razor areas remain functional.

### Phase 7 — Database modelling/performance

**Tasks:** P07-01 through P07-02

**Outcome:** measured query improvements, targeted composite/filtered indexes, projections, pagination, constraints, and optimistic concurrency.

**Exit gate:** representative query plans and before/after evidence are recorded; concurrency conflicts return useful responses.

### Phase 8 — Authentication/authorization improvements

**Tasks:** P08-01 through P08-02

**Outcome:** policy/resource authorization, verified or explicitly controlled account onboarding, external OIDC, cookie/API CSRF hardening, security headers, and rate limits.

**Exit gate:** role/ownership matrices are integration-tested and external-login secrets are never stored in source.

### Phase 9 — Third-party integration

**Task:** P09-01

**Outcome:** one configurable geocoding integration enriches properties and demonstrates resilient HTTP behavior without becoming a hard dependency for core workflows.

**Exit gate:** success, timeout, rate-limit, invalid response, caching, and provider-unavailable cases are tested.

### Phase 9A — React business-workspace migration (user-prioritized)

**Task:** F01-01, detailed in [TASKS.md](TASKS.md).

**Outcome:** extend the existing React/TypeScript application to public properties,
landlord/Admin dashboards, property management, tenancy invitations/ending and
acceptance, maintenance triage and Admin role management. Reuse the .NET services,
Identity cookies, resource authorization, antiforgery and concurrency controls.

**Boundary:** this is a presentation migration, not a Node backend, authentication
rewrite, schema change or removal of every `.cshtml` file. Identity remains Razor;
existing MVC routes are retained for compatibility. No new frontend dependencies.

**Exit gate:** role-specific React journeys, safe failure/session handling, API
and SQL authorization regressions, and responsive browser checks pass. See
[REACT_BUSINESS_WORKSPACES.md](REACT_BUSINESS_WORKSPACES.md) for implemented scope
and explicit limitations. Docker resumes after this phase.

### Phase 10 — Docker and local development

**Tasks:** P10-01 through P10-02

**Outcome:** reproducible non-root application image and Docker Compose environment for app plus SQL Server.

**Exit gate:** a new developer can start the stack from documented commands and data persists in a named volume.

### Phase 11 — CI/CD

**Tasks:** P11-01 through P11-02

**Outcome:** pull requests run backend/frontend quality gates and main produces a scanned, immutable container artifact ready for deployment.

**Exit gate:** branch protection can require the checks; release artifacts are traceable to a commit.

### Phase 12 — Azure deployment preparation

**Tasks:** P12-01 through P12-02

**Outcome:** minimal Bicep provisions App Service, Azure SQL, Key Vault, telemetry, and managed identities; deployments use environment approvals and an explicit migration job.

**Exit gate:** a clean Azure environment can be provisioned and smoke-tested without long-lived cloud credentials in GitHub.

### Phase 13 — Observability and reliability

**Tasks:** P13-01 through P13-02

**Outcome:** structured logs, trace correlation, safe exception handling, metrics/traces, health checks, dependency resilience, and an operational runbook.

**Exit gate:** a failed dependency and a failed database readiness check are diagnosable without exposing PII or secrets.

### Phase 14 — Optional AI feature

**Task:** P14-01

**Outcome:** feature-flagged, human-reviewed maintenance categorization/summarization with privacy controls, evaluation cases, and a deterministic fallback.

**Exit gate:** disabling or losing the AI provider does not block request submission or status management.

### Phase 15 — GitHub/README/portfolio polish

**Tasks:** P15-01 through P15-02

**Outcome:** a recruiter-friendly repository with accurate setup, diagrams, screenshots, demo data, ADRs, contribution guidance, release notes, and measurable engineering evidence.

**Exit gate:** a reviewer can understand, run, test, and evaluate the application from the repository landing page.

## Dependency path

The critical path is:

`P01-01 -> P01-02 -> P01-03 -> P02-01 -> P02-02 -> P03-01 -> P03-02 -> P04-02 -> P05-01 -> P05-02/P05-03 -> P06-02 -> P07-01/P07-02 -> P08-01/P08-02 -> deployment phases`

Unit tests should be added with every behavior-changing task even though the dedicated testing phase establishes the complete harness. Documentation and small security fixes should not wait for a large phase branch.

## Inappropriate or over-engineered additions

The following would weaken this portfolio at its current scale unless a real requirement appears:

- Microservices, message brokers, distributed sagas, service mesh, Kubernetes, or database-per-service.
- Event sourcing, a generic workflow engine, or full CQRS/MediatR architecture for simple CRUD and maintenance state changes.
- Generic repository/unit-of-work abstractions over EF Core.
- A custom OAuth/OIDC authorization server or home-grown JWT issuance for the React client.
- Rewriting Identity/security or deleting compatibility views merely to remove every `.cshtml` file.
- Splitting the codebase into many class-library projects before dependency boundaries require it.
- Real-time chat, SignalR notifications, rent payment processing, accounting, document signing, and broad CRM features before the core tenancy/maintenance workflow is excellent.
- Redis, Elasticsearch, sharding, or a data warehouse without measured load and query requirements.
- AKS for a single application; App Service with Azure SQL is the proportionate Azure target.
- Multiple third-party integrations. One reliable, well-tested integration is stronger evidence than several shallow ones.
- AI that autonomously changes priority/status, makes housing decisions, or processes unrestricted personal data.

## Priority summary

### First five tasks

1. **P01-01 — Establish a supported, reproducible baseline.**
2. **P01-02 — Define tenancy and maintenance domain invariants.**
3. **P01-03 — Align persistence and property lifecycle behavior.**
4. **P02-01 — Introduce application services around core use cases.**
5. **P02-02 — Introduce explicit contracts, validation, and mapping.**

### Highest CV value

- P02-01 application/service architecture.
- P03-01/P03-02 REST API and OpenAPI contracts.
- P04-02 SQL Server-backed integration testing.
- P06-01/P06-02 React + TypeScript delivery.
- P10-01/P10-02 Dockerized development.
- P11-01/P11-02 CI and immutable release artifacts.
- P12-01/P12-02 Azure IaC and secure deployment.
- P13-01 telemetry and structured logging.

### Highest interview value

- P01-02 domain invariants and lifecycle trade-offs.
- P02-01 why a thin application layer is useful and why a generic repository is not.
- P04-02 test fidelity, authenticated integration tests, and SQL Server Testcontainers.
- P07-01 evidence-led indexes, projections, and query-plan analysis.
- P07-02 optimistic concurrency and database-enforced invariants.
- P08-01 role versus resource authorization and IDOR prevention.
- P08-02 same-origin cookie/antiforgery design versus issuing SPA tokens.
- P09-01 resilient external API behavior and graceful degradation.

### Tasks to postpone

- P09-01 until property, tenancy, API, tests, and React basics are stable.
- P12-01/P12-02 until the local container and release artifact are repeatable.
- P13-02 advanced operational tuning until deployment topology exists.
- P14-01 until representative maintenance data, privacy rules, and deterministic workflows exist.
- P15-02 public demo/case-study work until the quality gates and deployment are credible.
