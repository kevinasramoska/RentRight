# RentRight implementation tasks

Status: proposed backlog

Ordering rule: complete tasks in phase/task-number order unless a task explicitly permits parallel work.

Complexity: **S** (small, usually one focused PR), **M** (several components or meaningful design), **L** (cross-cutting workflow; split into reviewable PRs).

Every behavior-changing task includes tests. The dedicated testing phase creates the high-fidelity harness; it does not defer basic tests from earlier work.

## Phase 1 — Existing domain completion

Implementation checkpoint (2026-08-11): the focused core-domain request is implemented and documented in [PHASE1_MANUAL_VERIFICATION.md](PHASE1_MANUAL_VERIFICATION.md). P01-01 now includes the solution entry point, .NET 10 SDK pin, package alignment, CI update, and dependency audit; database-backed startup and migration rehearsal remain environment-dependent. P01-02 and P01-03 remain partial against their broader original acceptance criteria; the delivered portions are recorded below rather than overstated as complete.

### P01-01 — Establish a supported, reproducible baseline

- **Implementation status:** Partial — added `RentRight.slnx`, upgraded both projects to .NET 10, aligned Microsoft packages, removed unused scaffolding dependencies, pinned the SDK, and updated CI; database-backed startup and migration rehearsal remain outstanding.

- **Why it matters:** The initial working tree contained valuable but uncommitted test/CI/view-model work, had no solution entry point, mixed .NET 9 package patches, and targeted an STS framework nearing end of support. A stable baseline prevents later work from being built on ambiguous state.
- **Files/components likely affected:** `RentRight.csproj`, `RentRight.Tests/RentRight.Tests.csproj`, `Program.cs`, a new solution file (`.slnx` or `.sln`), optional `global.json`/`Directory.Build.props`, `.github/workflows/ci.yml`, `README.md`, current uncommitted `ViewModels` and admin/dashboard changes.
- **Implementation steps:**
  1. Review and commit the existing local changes as an intentional, isolated baseline; do not mix them with the framework upgrade.
  2. Add a solution-level entry point containing web and test projects; remove the need for the web project to exclude nested test source by relocating/restructuring only if required.
  3. Upgrade to .NET 10 LTS and align ASP.NET Core/EF Core packages on one supported patch line; assess whether the scaffolding package is still needed and remove it from the application dependency graph when it is not.
  4. Add deterministic SDK/build settings and enable nullable/warning discipline without hiding existing warnings.
  5. Restore, build, run tests, list packages, rerun the NuGet vulnerability audit, and record local SQL Server prerequisites.
- **Acceptance criteria:** One documented solution command restores/builds/tests all projects; the application starts on .NET 10; Microsoft packages are patch-aligned; the currently reported high-severity transitive tooling advisory is resolved or explicitly risk-assessed with runtime-asset evidence; current functionality and the two existing tests remain intact; project inclusion is explicit and test source cannot be compiled accidentally into the web assembly.
- **Tests required:** Existing xUnit suite; release build; startup smoke test in Development; package/reference inspection; CI dry run or equivalent commands locally.
- **Security considerations:** Use a currently supported runtime/patch; do not introduce secrets into `global.json`, project files, settings, or CI; retain the development-only admin-seed guard.
- **Database implications:** No intentional schema change. Back up/rehearse the provider/runtime upgrade against a disposable SQL Server database and confirm the existing migration applies.
- **Estimated complexity:** M
- **Dependencies:** None.
- **Resume/portfolio value:** Demonstrates lifecycle awareness, dependency hygiene, reproducible builds, and disciplined handling of an in-progress worktree.

### P01-02 — Define tenancy and maintenance domain invariants

- **Implementation status:** Partial — added the tenancy relationship, active/ended state, date validation, tenancy-rooted requests, controlled maintenance statuses, and transition rules. Property publication/archive modelling and richer audit history remain outstanding.

- **Why it matters:** Maintenance cannot be authorized correctly without a tenancy connecting a tenant to a property. Free-text status and client-controlled tenant/property identifiers permit invalid states.
- **Files/components likely affected:** `Models/Property.cs`, `Models/MaintenanceRequest.cs`, new `Models/Tenancy.cs`, proposed status/priority/publication enums, domain-rule classes, `Data/ApplicationDbContext.cs`, `Data/Configurations/*`, architecture decision record.
- **Implementation steps:**
  1. Write the property, tenancy, maintenance, status-transition, date, ownership, archival, and multi-role invariants as an ADR and test cases.
  2. Model `Tenancy` with property, tenant, dates, lifecycle status, and timestamps.
  3. Make tenancy the authorization root for a maintenance request; remove redundant tenant/property inputs where safe migration permits.
  4. Replace arbitrary maintenance status text with controlled values and a centralized transition policy; add bounded category/priority only if used by the workflow.
  5. Define property publication/archive semantics and historical-record retention.
- **Acceptance criteria:** The model cannot represent a request unrelated to a tenancy; invalid tenancy dates and invalid status transitions are rejected; one active-tenancy-per-property behavior is explicit; existing data migration rules are documented; no UI workflow is implemented yet.
- **Tests required:** Pure xUnit tests for valid/invalid dates, transition matrix, tenancy/request association, publication/archive rules, enum/value conversion, and UTC timestamp behavior.
- **Security considerations:** Tenant and landlord IDs always derive from authenticated context or persisted relationships; no client can self-assign ownership/occupancy; avoid putting personal data in enum/history text.
- **Database implications:** New tenancy table and foreign keys; maintenance foreign-key redesign; controlled numeric/string conversions; retention/delete behavior must be explicit. Migration is generated in P01-03.
- **Estimated complexity:** L
- **Dependencies:** P01-01.
- **Resume/portfolio value:** Strong domain-modelling story showing business invariants, state machines, and security-driven relational design.

### P01-03 — Align EF persistence and current property lifecycle behavior

- **Implementation status:** Partial — added lengths/precision, required no-action relationships, essential indexes/check constraints, safe legacy-request backfill, property-history delete protection, Admin precedence, and permission-aware Razor controls. Publication state, rowversion, and automated SQL Server migration tests remain outstanding.

- **Why it matters:** EF currently reports pending model changes and a decimal precision warning. Property deletion conflicts with retained maintenance data, and MVC presentation does not consistently reflect permissions.
- **Files/components likely affected:** `Data/ApplicationDbContext.cs`, `Data/Configurations/*`, `Models/*`, `Migrations/*` (new incremental migration only), `Controllers/PropertiesController.cs`, property/home/dashboard Razor views, targeted tests.
- **Implementation steps:**
  1. Configure explicit max lengths, decimal precision, required relationships, enum conversions, UTC fields, and delete behavior.
  2. Generate an incremental migration; do not edit the existing initial migration. Review migration SQL and define a safe transform for any existing maintenance rows.
  3. Resolve EF model drift until `has-pending-model-changes` succeeds.
  4. Implement the agreed archive/unpublish behavior for properties with history and a safe hard-delete rule for unused records.
  5. Normalize ownership outcomes (`Challenge`, `Forbid`, deliberate concealed `NotFound`) and define Admin-plus-Landlord behavior.
  6. Hide create/edit/delete links from users who cannot perform them and stop exposing raw landlord IDs on public pages.
- **Acceptance criteria:** EF emits no precision warning and reports no pending changes; a clean database migrates successfully; a populated pre-change database has a documented path; unauthorized property mutation is blocked consistently; property history cannot be accidentally cascade-deleted; Razor pages show only valid actions.
- **Tests required:** Model-configuration tests, migration apply/rollback-on-disposable-database test where practical, property ownership controller tests, multi-role case, delete/archive behavior, anonymous view behavior.
- **Security considerations:** Prevent IDOR and overexposure of internal user identifiers; perform checks on the server regardless of hidden links; never infer the owner from posted form fields.
- **Database implications:** First intentional domain migration; column narrowing may require data cleanup; new tenancy and maintenance constraints; delete behavior changes; essential uniqueness/check constraints only—performance tuning waits for Phase 7.
- **Estimated complexity:** L
- **Dependencies:** P01-02.
- **Resume/portfolio value:** Demonstrates safe schema evolution, relational integrity, authorization-aware lifecycle design, and migration review.

## Phase 2 — Application/service architecture

### P02-01 — Introduce application services around core use cases

- **Why it matters:** MVC controllers currently own EF queries, Identity calls, authorization decisions, and mutations. React/API entry points would duplicate those rules without a shared application layer.
- **Files/components likely affected:** New `Features/Properties`, `Features/Tenancies`, `Features/Maintenance`, and `Features/Administration` service contracts/implementations; `Controllers/PropertiesController.cs`, `DashboardController.cs`, `AdminController.cs`; DI registration in `Program.cs`; tests.
- **Implementation steps:**
  1. Identify use cases from existing actions and the new domain; create focused property, dashboard, tenancy, maintenance, and role-administration services rather than generic CRUD services.
  2. Move business queries, ownership scoping, state transitions, Identity result handling, and transaction boundaries into services one controller at a time.
  3. Introduce a small immutable current-user context and `IClock` where they improve determinism.
  4. Return explicit success/not-found/forbidden/validation/conflict outcomes that MVC and API can map consistently.
  5. Keep EF Core as unit of work; document why generic repository/UoW and MediatR are not being introduced.
- **Acceptance criteria:** Existing MVC flows behave the same or better; controllers contain transport mapping rather than business logic; services enforce ownership even when called outside MVC; role updates check and surface every `IdentityResult`; one service transaction owns each mutation.
- **Tests required:** Unit tests for service outcomes, ownership scope, Admin override, role-update failures, last-admin protection, and transaction/state behavior; existing controller smoke tests.
- **Security considerations:** Authorization must not rely only on controller attributes; current-user identity is server-derived; sensitive Identity errors are translated safely; concurrent last-admin changes are handled deliberately.
- **Database implications:** Primarily query/transaction reorganization; no schema change expected. Verify generated queries to avoid worsening the admin N+1 problem.
- **Estimated complexity:** L
- **Dependencies:** P01-03.
- **Resume/portfolio value:** Core clean-architecture evidence: thin delivery adapters, testable use cases, explicit outcomes, and pragmatic boundaries.

### P02-02 — Add explicit contracts, validation, mapping, and query models

- **Why it matters:** Binding EF entities directly permits over-posting and couples UI/API shape to persistence. Unpaged entity queries waste data and complicate client evolution.
- **Files/components likely affected:** `Features/*/Contracts`, input/view models, validators or DataAnnotations, mapping extensions, MVC controllers/views, service query methods, admin/dashboard projections.
- **Implementation steps:**
  1. Define separate create/update/detail/list contracts for properties, tenancies, maintenance requests, dashboards, and role changes.
  2. Bind only permitted client fields; derive IDs, timestamps, status defaults, and ownership on the server.
  3. Centralize bounded validation and normalize validation errors for MVC and API reuse.
  4. Project read queries directly into response/view models with `AsNoTracking` and cancellation tokens.
  5. Replace all-users/admin role loading with a paged query strategy that avoids avoidable per-row work where Identity permits.
- **Acceptance criteria:** No mutation action accepts a tracked entity as its public input; no public contract exposes password/security fields or unnecessary Identity/entity internals; lists are bounded; invalid input produces field-level feedback; all async database paths accept cancellation.
- **Tests required:** Contract validation boundaries, over-posting attempts, mapping tests, null/empty/unicode/length cases, pagination bounds, query projection tests.
- **Security considerations:** Prevent mass assignment and PII leakage; cap page size and text lengths; do not trust caller-supplied actor/owner/time/status fields.
- **Database implications:** Read projections reduce selected columns/tracking; validation lengths must agree with EF column lengths; no migration unless a mismatch is discovered.
- **Estimated complexity:** M
- **Dependencies:** P02-01.
- **Resume/portfolio value:** Demonstrates API-safe contracts, validation consistency, performance-aware querying, and maintainable view models.

## Phase 3 — REST API

### P03-01 — Establish versioned API conventions and OpenAPI

- **Implementation status:** Complete — added the `/api/v1` controller convention,
  camel-case JSON and bounded pagination contracts, RFC 7807 outcome/validation/error
  mapping with stable codes and trace IDs, API-specific cookie challenge behavior,
  global unsafe-request antiforgery coverage with a documented header, safe exception
  responses, and runtime OpenAPI exposed only in Development. No resource or smoke
  endpoint was added; integration coverage uses a test-only application part.

- **Why it matters:** A professional API needs predictable routing, errors, validation, documentation, auth behavior, and testable contracts before resource endpoints multiply.
- **Files/components likely affected:** `Controllers/Api/V1/*`, API conventions/options, `Program.cs`, `ProblemDetails` customization, OpenAPI setup, shared result-to-HTTP mapper, API documentation.
- **Implementation steps:**
  1. Add `/api/v1` routing and a consistent controller or endpoint convention without introducing a separate service.
  2. Configure OpenAPI for DTO schemas, authentication behavior, response codes, and example errors.
  3. Map application outcomes to RFC 7807 `ProblemDetails` with stable error codes and trace IDs.
  4. Standardize validation, pagination envelopes/headers, JSON naming, UTC timestamps, cancellation, and content types.
  5. Define same-origin cookie and antiforgery behavior for unsafe API calls, ready for the React client.
- **Acceptance criteria:** OpenAPI is available in Development only (unless explicitly protected elsewhere); representative validation/not-found/forbidden/conflict failures share one documented shape; internal exception details are absent; a health-independent API smoke endpoint is not added merely for testing.
- **Tests required:** Integration tests for JSON casing, validation Problem Details, trace IDs, response content types/codes, OpenAPI generation, authentication challenge, and antiforgery rejection.
- **Security considerations:** Do not expose Swagger/OpenAPI UI indiscriminately in production; require antiforgery on cookie-authenticated writes; prevent stack traces and route-based object enumeration.
- **Database implications:** None directly.
- **Estimated complexity:** M
- **Dependencies:** P02-02.
- **Resume/portfolio value:** Clear evidence of REST contract design, standards-based errors, OpenAPI, versioning, and secure browser/API integration.

### P03-02 — Deliver the property REST API

- **Implementation status:** Complete for the current property lifecycle — added
  public projected list/detail reads, bounded paging and filters, Landlord/Admin
  create/update/delete commands, owner scoping with concealed cross-landlord IDs,
  HTTP/OpenAPI/antiforgery coverage, and dedicated API DTOs. The current model has no
  unpublished/archive or rowversion field; persistent lifecycle and concurrency state
  remain explicitly deferred to Phase 7 without adding schema in this task.

- **Why it matters:** Properties provide a bounded first API vertical slice that proves service reuse before the more sensitive maintenance workflow is exposed.
- **Files/components likely affected:** `Controllers/Api/V1/PropertiesController.cs`, property contracts/services, OpenAPI descriptions, authorization mapping, integration tests.
- **Implementation steps:**
  1. Implement paged/filterable public published-property list and detail endpoints.
  2. Implement authenticated create/update/archive endpoints for landlords/admins through `IPropertyService`.
  3. Return `201` locations, bounded pagination metadata, and consistent conflict/forbidden/not-found outcomes.
  4. Add an opaque concurrency value to update contracts only if rowversion is already present; otherwise document its Phase 7 addition.
  5. Verify MVC and API execute the same ownership and validation rules.
- **Acceptance criteria:** Anonymous callers see only published properties; landlords can mutate only owned properties; Admin override is explicit; posted owner/status/server fields cannot be forged; list endpoints remain bounded; OpenAPI matches observed responses.
- **Tests required:** Full HTTP matrix for anonymous/Tenant/owning Landlord/other Landlord/Admin; valid/invalid creation; over-posting; paging/filtering; missing records; archive/delete semantics; response schema snapshots or assertions.
- **Security considerations:** Resource-scoped access and IDOR tests are mandatory; public responses exclude landlord Identity IDs and private tenancy/maintenance data; error behavior does not reveal concealed resources.
- **Database implications:** Uses optimized projections and bounded reads; no new schema required beyond Phase 1.
- **Estimated complexity:** M
- **Dependencies:** P03-01.
- **Resume/portfolio value:** A demonstrable, documented REST vertical slice sharing rules with MVC.

## Phase 4 — Automated testing

### P04-01 — Build a meaningful xUnit unit-test suite

- **Implementation status:** Completed — P04-01 adds 45 deterministic domain and application-service tests, bringing the combined suite to 286 passing tests after Phase 3. The .NET 10 Microsoft coverage collector reports 61.06% line and 54.07% branch coverage for the production assembly. Generated migrations and static assets are excluded, no threshold is enforced from this diagnostic baseline, and SQL Server-backed HTTP, Identity, antiforgery, and migration coverage remains P04-02.
- **Why it matters:** Ownership, tenancy, role management, and maintenance transitions need deterministic regression protection below the HTTP and database boundaries.
- **Files/components likely affected:** `RentRight.Tests`, test builders/fixtures, application/domain tests, coverage configuration, project references.
- **Implementation steps:**
  1. Organize tests by feature and replace repeated setup with readable builders.
  2. Cover every domain invariant and application-service outcome, including negative and boundary cases.
  3. Use a fake clock/current user; avoid mocking EF internals or testing framework implementation details.
  4. Add coverage collection for insight, setting only a modest critical-path threshold after measuring the baseline.
  5. Name tests as business behavior and keep them deterministic/parallel-safe.
- **Acceptance criteria:** Core property/tenancy/maintenance/admin rules have positive and negative tests; tests run without network/SQL Server; failures identify the broken business rule; coverage excludes generated migrations and static assets.
- **Tests required:** This task is the unit suite itself; include mutation-sensitive cases such as invalid transitions, expired tenancy, owner mismatch, multi-role Admin, and last-admin update failure.
- **Security considerations:** Synthetic users/data only; no real tokens, secrets, addresses, or credentials in fixtures/snapshots.
- **Database implications:** None; unit tests do not claim to validate relational behavior.
- **Estimated complexity:** M
- **Dependencies:** P02-02; can begin after P02-01 and grow alongside Phase 3.
- **Resume/portfolio value:** Demonstrates testing judgment, not just test count: pure rules, deterministic time, negative cases, and maintainable fixtures.

### P04-02 — Add SQL Server-backed ASP.NET Core integration tests

- **Implementation status:** Completed — 26 real SQL Server integration cases passed twice on 2026-10-16, in addition to the empty-database smoke run and all 286 fast tests. The separate project uses Testcontainers, per-case migrated databases and real Identity login cookies without production hooks or schema changes. See `docs/SQL_INTEGRATION_TESTS.md` for local/CI commands and verification evidence.

- **Why it matters:** Authorization, Identity cookies, antiforgery, migrations, SQL Server constraints, and HTTP error contracts cannot be trusted from unit tests alone.
- **Files/components likely affected:** A dedicated integration-test project or namespace, `Program.cs` (`public partial class Program` test seam), custom `WebApplicationFactory`, Testcontainers setup, database reset/fixtures, CI Docker support.
- **Implementation steps:**
  1. Start the real application with `WebApplicationFactory<Program>` and override only external configuration.
  2. Provision disposable SQL Server with Testcontainers, apply migrations, and isolate/reset test data reliably.
  3. Provide explicit anonymous, Tenant, Landlord, other-Landlord, multi-role, and Admin authentication helpers; keep a small real Identity-cookie path.
  4. Exercise MVC/API routing, filters, antiforgery, authorization, database constraints, Problem Details, and concurrency behavior available to date.
  5. Make local and CI execution documented; fail clearly when Docker is unavailable rather than silently skipping critical tests.
- **Acceptance criteria:** Tests run against SQL Server rather than EF InMemory; the migration from empty database succeeds; authenticated HTTP ownership cases pass; tests are repeatable and clean up containers/data; CI can run the suite.
- **Tests required:** Migration smoke test, role/ownership matrix, CSRF, 401/403/404 behavior, validation, property API, last-admin case, and at least one relational-constraint failure.
- **Security considerations:** Test authentication is registered only in the test host; container passwords are ephemeral; test hooks cannot be enabled by production configuration.
- **Database implications:** Disposable SQL Server validates provider-specific types, indexes, foreign keys, filtered constraints, and migration scripts.
- **Estimated complexity:** L
- **Dependencies:** P03-02 and P04-01.
- **Resume/portfolio value:** Very high: real-pipeline, real-database integration testing is strong evidence of professional .NET engineering.

## Phase 5 — Tenant and maintenance workflows

### P05-01 — Implement the tenancy establishment lifecycle

- **Implementation status:** Complete (2026-10-16). Admin/owning-Landlord invitations, explicit matched-Tenant acceptance, scoped API list/detail, audited ending/cancellation, exact-identifier privacy, hashed expiring single-use tokens, transactional property reservations and rowversion are implemented. Immediate Admin assignment is intentionally replaced by consent. Legacy records retain unknown audit/consent values. Manual token delivery is the operational limit; property archive state does not exist in the current model.
- **Verification:** Final fast suite: 321 passed. Two independent complete disposable SQL Server runs: **72 passed each**, 0 failed/skipped, including lifecycle HTTP/Razor/cookie/antiforgery, migration/backfill/rollback, constraints, privacy and all six race combinations. Release builds, EF pending-model check, generated Up/Down SQL review and all three project transitive vulnerability audits passed. See [lifecycle decisions, file inventory and verification](TENANCY_LIFECYCLE.md).

- **Why it matters:** Tenant features need an auditable, authorized occupancy record. Assigning a tenant directly to a maintenance request is not sufficient.
- **Files/components likely affected:** tenancy service/contracts, API/MVC endpoints and views as selected, `ApplicationDbContext`, authorization checks, status history/audit records, tests.
- **Implementation steps:**
  1. Implement create/invite, accept/activate, list/detail, and end tenancy use cases according to the Phase 1 ADR.
  2. Permit Admin or the owning Landlord to initiate; require explicit tenant acceptance if invitation is exposed.
  3. Resolve tenant accounts by a privacy-preserving exact identifier/invite token rather than exposing a global user directory to landlords.
  4. Enforce date, role, ownership, duplicate-active-tenancy, expiry, and archival rules transactionally.
  5. Record lifecycle actor/time and return conflict responses for invalid concurrent actions.
- **Acceptance criteria:** A valid tenancy can be established and ended; unauthorized landlords cannot view or mutate it; tenants cannot self-attach to properties; only one active tenancy per property is accepted; history remains after ending.
- **Tests required:** Service and integration tests for every actor, acceptance token/user mismatch, duplicate active tenancy race, invalid dates, archived property, end behavior, and concealed IDs.
- **Security considerations:** Invitation tokens are random, expiring, single-use, and never logged; user enumeration is prevented; landlord access is property-scoped; tenant consent is explicit where applicable.
- **Database implications:** Uses Phase 1 tenancy schema; transaction/unique constraint handles races; lifecycle timestamps/history may add a reviewed migration if not already included.
- **Estimated complexity:** L
- **Dependencies:** P04-02.
- **Resume/portfolio value:** Shows lifecycle modelling, transactional invariants, privacy-aware invitations, and resource authorization.

### P05-02 — Complete the tenant dashboard and maintenance submission journey

- **Implementation status:** Delivered in P05-02 — active-tenancy dashboard projections/counts, tenant REST create/list/detail, shared Razor submission rules, immutable status events with truthful legacy baselines, and fast/HTTP/SQL regression coverage. See README for eligibility, repeated-submit, multiple-tenancy, conflict, and unavailable behavior.

- **Why it matters:** Provides the central tenant dashboard and maintenance submission/read journey.
- **Files/components likely affected:** tenant dashboard query service/view model, maintenance service/contracts, maintenance API endpoints, interim Razor views if needed, `DashboardController`, tests.
- **Implementation steps:**
  1. Query the authenticated tenant's active tenancy, property summary, and recent/open maintenance counts with projections.
  2. Implement tenant request create/list/detail endpoints through the maintenance service.
  3. Derive tenancy, tenant, submitted time, and initial status on the server; apply bounded validation.
  4. Define empty/no-active-tenancy, expired-tenancy, duplicate-submit, conflict, and unavailable states.
  5. Expose status history read-only to authorized participants.
- **Acceptance criteria:** A tenant with an active tenancy sees the correct dashboard and can submit/read requests only for that tenancy; a tenant without one gets a useful empty state; another tenant cannot enumerate a request; no client can choose the tenant or landlord.
- **Tests required:** Dashboard projection, active/expired/no tenancy, valid/invalid submission, duplicate request policy if adopted, cross-tenant IDOR, history ordering, status serialization, and API problem responses.
- **Security considerations:** Scope every query by authenticated user and persisted tenancy; descriptions are untrusted input and encoded in UI; cap length and avoid logging content.
- **Database implications:** Adds maintenance rows and status history; queries should be index candidates for Phase 7; no uncontrolled table scans in dashboard implementation.
- **Estimated complexity:** L
- **Dependencies:** P05-01.
- **Resume/portfolio value:** Provides the central tenant-facing vertical slice across domain, EF, API, auth, and UI contracts.

### P05-03 — Implement landlord maintenance triage and status history

- **Implementation status:** Implemented — bounded owner/Admin queue with status/property/UTC-date filters, versioned triage API, retained Razor priority/status controls, mandatory expected revision, atomic actor/previous-status/time/note events, truthful legacy defaults, and ended-tenancy operational visibility. Tenant access remains active-tenancy scoped. See README for contracts and concurrency decisions.

- **Why it matters:** A request workflow is incomplete until the owning landlord can prioritize, progress, resolve, and explain work while tenants retain visibility.
- **Files/components likely affected:** maintenance workflow service, landlord dashboard/query model, API/MVC endpoints, status-event model/configuration, Razor views if retained, tests.
- **Implementation steps:**
  1. Add a paged landlord work queue scoped through property ownership with status/property/date filters.
  2. Implement allowed status/priority updates using the centralized transition policy and optimistic version input when available.
  3. Append immutable status events with actor/time/note in the same transaction.
  4. Surface stale updates, invalid transitions, and archived/ended-tenancy behavior explicitly.
  5. Add admin support access without weakening landlord scoping.
- **Acceptance criteria:** Landlords see/update only requests for owned properties; every accepted transition creates history; invalid or stale updates do not partially write; tenants see the new state/history; queues are bounded.
- **Tests required:** Owner/non-owner/Admin/multi-role matrix, full transition table, concurrent updates, event atomicity/order, filtering/paging, ended tenancy, and encoded note rendering.
- **Security considerations:** Prevent cross-landlord IDOR; notes/descriptions are encoded and not included in broad logs; actor identity is server-derived; Admin override is auditable.
- **Database implications:** Transactional status/event writes; work-queue queries feed Phase 7 index design; history is append-only and retained.
- **Estimated complexity:** L
- **Dependencies:** P05-02.
- **Resume/portfolio value:** Strong workflow/state-machine, auditability, authorization, and transactional-consistency example.

## Phase 6 — React + TypeScript frontend

### P06-01 — Establish the React/TypeScript build and API foundation

**Implemented:** isolated `/app` shell, pinned Vite/React/strict TypeScript build, cookie/CSRF API client, no-store session/token endpoints, frontend and HTTP tests, and publish integration. See README for the Node 24 HTTPS workflow. Tenant portal delivered in P06-02.

- **Why it matters:** React should be introduced as a maintainable client with a reproducible build, typed errors, secure cookies, and a clear ASP.NET integration—not as disconnected demo code.
- **Files/components likely affected:** new `ClientApp` Vite project, TypeScript/ESLint/Vitest configuration, `RentRight.csproj` build targets, `Program.cs` static-file/fallback routing, development proxy, API client, antiforgery endpoint/host page.
- **Implementation steps:**
  1. Scaffold Vite React TypeScript with pinned lockfile and scripts for dev, lint, type-check, test, and build.
  2. Configure development proxy and production asset output under the ASP.NET host without taking over Razor/Identity routes.
  3. Build a small typed fetch layer for JSON, pagination, Problem Details, cancellation, and antiforgery headers.
  4. Add current-session loading without storing auth tokens in browser storage.
  5. Define accessible layout/navigation and shared loading/error/empty states.
- **Acceptance criteria:** One documented local workflow runs server and client; production publish includes hashed React assets; Razor/Identity routes still work; authenticated API calls use cookies and unsafe calls pass antiforgery; TypeScript strict mode succeeds.
- **Tests required:** Client API wrapper tests, Problem Details parsing, antiforgery header behavior, route/static-asset integration smoke test, frontend production build.
- **Security considerations:** No tokens in `localStorage`; no secrets in `VITE_*` variables; same-origin/CORS policy is minimal; HTML from descriptions is never rendered unsafely.
- **Database implications:** None.
- **Estimated complexity:** M
- **Dependencies:** P05-02 and P03-01.
- **Resume/portfolio value:** Demonstrates real React/TypeScript integration with an authenticated ASP.NET API and production build pipeline.

### P06-02 — Build and test the React tenant maintenance portal

- **Presentation follow-up:** Responsive resident-workspace styling, local fonts and illustrations, an explicitly labelled synthetic `/app/demo/` journey, and portfolio screenshot assets are documented in [PORTFOLIO_SHOWCASE.md](PORTFOLIO_SHOWCASE.md). The demo is a browser-only simulation of the same tenant screens, not an authenticated backend demonstration.

- **Implementation status:** Delivered: authenticated overview, paged list, detail/history and validated submission under `/app`; safe dashboard API adapter and read-only maintenance priority; session clearing/cancellation, fixed problem messages, responsive accessible controls, component integration and real-host HTTP smoke coverage. SQL tests verify dashboard eligibility/scoping. No dependencies or schema changes. Browser/device and assistive-technology testing remains manual; the smoke combines component integration with WebApplicationFactory rather than a browser runner. See README.

- **Why it matters:** A focused, complete user journey is stronger portfolio evidence than a broad visual rewrite of working MVC screens.
- **Files/components likely affected:** `ClientApp/src/features/dashboard`, `features/maintenance`, routes/components/forms, generated or maintained API types, CSS/design tokens, frontend tests, minimal server host view.
- **Implementation steps:**
  1. Implement tenant tenancy summary, maintenance list/detail, submission form, and status timeline.
  2. Handle loading, empty, validation, unauthenticated, forbidden, not-found, conflict, offline/provider failure, and retry states.
  3. Use semantic controls, focus management, visible labels, keyboard navigation, and responsive layouts.
  4. Keep cache invalidation simple and explicit after mutations; add a server-state library only if justified.
  5. Add component/integration tests with controlled API responses and one small full-stack smoke journey.
- **Acceptance criteria:** A tenant can complete the core journey on desktop/mobile widths and keyboard; errors map to useful UI; another tenant's data never appears; no Razor admin/property flow regresses; frontend tests/type-check/lint pass.
- **Tests required:** React Testing Library/Vitest coverage for form boundaries, API problems, status timeline, empty/loading/error states, conflict recovery, permission redirects, and accessibility checks; one authenticated end-to-end smoke test when stable.
- **Security considerations:** Treat all API text as untrusted; no role/ownership decisions in client code; avoid sensitive data in client logs/analytics; CSRF remains enforced.
- **Database implications:** Read/write load should match API paging; record observed query patterns for Phase 7.
- **Estimated complexity:** L
- **Dependencies:** P06-01 and P05-03.
- **Resume/portfolio value:** The clearest full-stack demonstration: TypeScript UI, secure REST calls, accessibility, testing, and a real domain workflow.

## Phase 7 — Database modelling/performance

### P07-01 — Measure queries and add evidence-based indexes/projections

- **Implementation status (2026-10-17):** Implemented — opt-in disposable SQL measurements, three measured index improvements, bounded admin-role query-count checks, regression and migration coverage. See [query evidence and reproduction](performance/QUERY_PERFORMANCE.md). Existing projections, bounds, and authorization were preserved; historical N+1 claims were not present in current code.

- **Why it matters:** SQL Server skill is best demonstrated by explaining real query shapes and measured improvements, not by adding speculative indexes.
- **Files/components likely affected:** EF query services, entity configurations, new migration, SQL/query-plan notes under `docs`, performance test/seed tooling, dashboard/list endpoints.
- **Implementation steps:**
  1. Create representative seed volumes and capture generated SQL, timings, logical reads, and execution plans for public list, tenant dashboard, landlord queue, and admin users.
  2. Remove tracking/Includes where projections suffice; enforce paging and stable sorts.
  3. Add the smallest useful composite/filtered indexes for observed predicates/order/join paths.
  4. Re-measure and document improvements and write costs.
  5. Add regression checks for maximum page size and query count where reliable.
- **Acceptance criteria:** Each new index names a target query and has before/after evidence; hot reads are projected, no-tracking, paged, and use stable ordering; admin N+1 behavior is eliminated or explicitly bounded; migration applies cleanly.
- **Tests required:** Query-result correctness, paging stability, generated-SQL/query-count assertions where non-brittle, migration test, representative benchmark script with documented non-CI expectations.
- **Security considerations:** Seed/plan artifacts use synthetic data; query optimization must preserve ownership predicates and must not broaden result sets.
- **Database implications:** Targeted indexes likely cover publication/date, landlord/property, tenant/tenancy status, maintenance status/date, and status-history order; storage/write impact documented.
- **Estimated complexity:** L
- **Dependencies:** P05-03 and P06-02 so real query shapes exist.
- **Resume/portfolio value:** High-value SQL Server story with execution plans, EF projections, indexing trade-offs, and measurable results.

### P07-02 — Add database-enforced integrity, optimistic concurrency, and retention rules

- **Implementation status:** Implemented — SQL rowversion for mutable aggregates, required property/end versions, compatible maintenance revision protection, SQL constraints and retained-history guards, restrictive audit references, and reviewed incremental/idempotent migration. See [CONCURRENCY_RETENTION.md](CONCURRENCY_RETENTION.md) for deployment limits and verification.

- **Why it matters:** Application checks alone are race-prone. Production-style workflows need database constraints, stale-write detection, and explicit history retention.
- **Files/components likely affected:** domain entities, EF configurations, services/API contracts, migrations, Problem Details mapping, concurrency UI, database tests.
- **Implementation steps:**
  1. Add rowversion to mutable property, tenancy, and maintenance aggregates.
  2. Add safe unique/check constraints for active tenancy, date ranges, bounded statuses, and other invariants SQL Server can enforce.
  3. Map concurrency tokens to opaque API/UI versions and stale-write responses.
  4. Finalize archive/anonymize/retain rules for property, tenancy, maintenance history, and user references.
  5. Review migration locking/backfill risk and produce an idempotent deployment script.
- **Acceptance criteria:** Concurrent edits cannot silently overwrite; duplicate active tenancy races fail predictably; invalid persisted states are blocked; clients can reload after conflict; destructive deletes cannot erase required history.
- **Tests required:** Two-context concurrency tests, unique/check constraint tests against SQL Server, API conflict mapping, migration/backfill test, archive/retention behavior.
- **Security considerations:** Concurrency values are opaque, not authorization credentials; retained data is minimized; anonymization does not break audit integrity; database errors are not leaked.
- **Database implications:** Rowversion columns, constraints, possible filtered unique index, backfill/default strategy, and explicit delete behaviors.
- **Estimated complexity:** L
- **Dependencies:** P07-01.
- **Resume/portfolio value:** Excellent interview material on race conditions, constraints versus application checks, optimistic concurrency, and safe migrations.

## Phase 8 — Authentication/authorization improvements

### P08-01 — Centralize resource authorization and harden Identity/account rules

- **Implementation status:** Implemented and verified — named policies, Identity/cookie/session hardening, tenant-only development onboarding, closed production self-registration/demo confirmation, recent-password account/role actions and serialized usable-last-admin protection. Release build clean; 459 fast and 45 frontend tests passed; SQL regression/focused runs cover 207 distinct passing cases with one opt-in benchmark skipped. No schema/package change. See [AUTHORIZATION.md](AUTHORIZATION.md) for exact run scopes and operational boundaries; P08-02 remains separate.

- **Why it matters:** Role attributes alone cannot answer whether a landlord owns a property or a tenant participates in a tenancy. Identity defaults and scattered role strings also leave account onboarding and multi-role behavior ambiguous.
- **Files/components likely affected:** new `Authorization/*` requirements/handlers, role/policy constants, services/controllers/views, `Program.cs` Identity/cookie options, registration/onboarding customization if needed, admin role service, integration tests.
- **Implementation steps:**
  1. Define named policies and resource handlers for property management, tenancy participation, maintenance read, maintenance transition, and role administration.
  2. Replace scattered role literals and ad hoc `IsInRole` branches incrementally; keep application-service query scoping as defense in depth.
  3. Specify multi-role precedence, newly registered user behavior, landlord promotion, disabled/locked users, and last-admin behavior.
  4. Configure appropriate password, lockout, cookie lifetime/sliding expiration, secure flags, access-denied, and security-stamp settings by environment.
  5. Either provide verified local-account onboarding with a real email path or disable local self-registration in production when OIDC is the supported onboarding route.
  6. Require recent authentication for sensitive role/account changes where practical.
- **Acceptance criteria:** The authorization matrix in `ARCHITECTURE.md` is executable through policies and tests; a landlord cannot access another landlord's resources even by ID; multi-role behavior is deterministic; production account onboarding has no unconfirmed-account dead end; last-admin protection handles failure/concurrency safely.
- **Tests required:** Policy-handler unit tests and full HTTP matrix for anonymous/Tenant/owner Landlord/non-owner Landlord/Admin/multi-role/disabled user; cookie expiry/access-denied behavior; registration privilege-escalation attempts; concurrent last-admin update.
- **Security considerations:** Prevent IDOR, privilege escalation, role injection, user enumeration, and accidental lockout of all administrators; do not trust UI visibility or external claims as local authorization.
- **Database implications:** Identity settings mostly use existing tables; disabled/audit fields or serialized admin protection may require a migration/transaction strategy.
- **Estimated complexity:** L
- **Dependencies:** P07-02.
- **Resume/portfolio value:** High interview value: concrete policy/resource authorization, defense in depth, onboarding trade-offs, and Identity hardening.

### P08-02 — Add external OpenID Connect and secure the browser API boundary

- **Scope decision:** External OIDC/provider selection, account linking and callback tests are deferred by user choice. Keep the existing local Identity login; no paid/free provider registration is required to continue the portfolio work.
- **Implementation status:** Browser-security portion implemented and verified: 499 fast backend tests, 53 frontend tests and 68 focused real-SQL account/property-pipeline tests passed; Release build clean and no EF model drift. Secure antiforgery cookies, same-origin client path checks, baseline headers, per-IP account/per-user write rate limits and opt-in encrypted external key-ring configuration. No schema/package change or production key-store provisioning. Original OIDC acceptance criteria are not complete. See [BROWSER_SECURITY.md](BROWSER_SECURITY.md).

- **Why it matters:** External sign-in demonstrates standards-based federation, while React plus cookie authentication requires an explicit CSRF, CORS, redirect, and data-protection design.
- **Files/components likely affected:** authentication registration/options in `Program.cs`, external-login Identity UI customization, configuration/options classes, secret documentation, antiforgery token endpoint/header handling, React API client, security-header/rate-limit middleware, tests.
- **Implementation steps:**
  1. Select one production-relevant OIDC provider and register it through configuration; keep client credentials out of source.
  2. Link the external provider subject to a local Identity account and require deliberate local role assignment; handle duplicate email/account-linking safely.
  3. Validate callback/redirect URIs, correlation, sign-out, revoked/failed login, and environment-specific HTTPS behavior.
  4. Complete the same-origin cookie/antiforgery protocol for React API writes and use the narrowest CORS policy (normally no cross-origin production client).
  5. Add baseline security headers and ASP.NET Core rate limiting to authentication and mutation endpoints with usable `429` responses.
  6. Configure durable data-protection keys for container/Azure environments in Phase 12-compatible form.
- **Acceptance criteria:** Local Identity and the configured OIDC login coexist; external users cannot acquire roles from untrusted claims; callback/logout work; unsafe requests without antiforgery fail; no auth token is stored in browser storage; security headers/rate limits are documented and tested.
- **Tests required:** OIDC handler/account-linking unit tests with fake protocol responses, callback failure/duplicate identity cases, CSRF integration tests, CORS tests, open-redirect tests, rate-limit tests, cookie security assertions.
- **Security considerations:** Validate issuer/audience/nonce/state/correlation through framework middleware; rotate secrets; allowlist redirect targets; protect account linking from takeover; never log tokens/claims wholesale; assess SameSite behavior.
- **Database implications:** Identity external-login tables already exist; durable data-protection storage is external operational state; optional audit rows for login/link events.
- **Estimated complexity:** L
- **Dependencies:** P08-01 and P06-01.
- **Resume/portfolio value:** Demonstrates OAuth/OIDC understanding, federated login, secure cookie-based SPA integration, CSRF, CORS, and rate limiting.

## Phase 9 — Third-party integration

### P09-01 — Add resilient property geocoding with graceful degradation

- **Implementation status:** Implemented and verified with disabled-by-default Geoapify lookup and nullable location migrations: clean Release build, 560 fast backend tests, 62 combined frontend tests after the showcase merge, 23 new SQL cases plus 83 existing property/concurrency/query SQL cases passed; no model drift. Pre-merge review aligned provider references to 2048 characters with a separate widening migration and boundary tests. Live account/key/accuracy/deployment checks remain operator work. No shared database was migrated. Existing local Identity remains the authorization boundary while P08-02 external sign-in is independently deferred. See [GEOCODING.md](GEOCODING.md) and its provider ADR for exact run scope, activation and verification.

- **Why it matters:** Geocoding is useful to property data and demonstrates a real external API boundary, validated options, resilient HTTP calls, caching, cost/rate-limit awareness, and failure design.
- **Files/components likely affected:** `Integrations/Geocoding/*`, `IGeocodingClient`, typed `HttpClient` registration, options/configuration, property service/contracts/model, EF configuration/migration for coordinates/provider reference, Razor/React map presentation, tests/documentation.
- **Implementation steps:**
  1. Evaluate and record the provider choice, terms, quota, data residency, credential method, and permitted caching in an ADR.
  2. Implement a provider-agnostic contract and one typed `HttpClient` with timeout, cancellation, bounded transient retries, rate-limit handling, and response validation.
  3. Normalize/cache address lookup results and persist only required coordinates/provider identifiers.
  4. Invoke geocoding explicitly after property create/address change, never on every read or anonymous list render.
  5. Show a map/location enhancement when coordinates exist and a normal address-only experience when the provider is disabled/unavailable.
  6. Add admin/owner retry behavior and operational metrics without leaking keys or full address payloads.
- **Acceptance criteria:** Valid addresses can be enriched; timeouts, `429`, malformed responses, no-result, disabled provider, and outage do not block core property management; calls are cached/bounded; configuration fails fast only when the feature is enabled; maps do not expose credentials.
- **Tests required:** Controlled HTTP-handler tests for success/error/timeout/cancellation/retry/rate-limit/malformed payload; cache tests; service fallback tests; integration test with fake provider; UI no-coordinate state.
- **Security considerations:** Secrets remain server-side; prevent SSRF by fixing the configured provider base address; encode provider content; minimize address logs; review third-party privacy/terms and browser map key restrictions.
- **Database implications:** Nullable latitude/longitude with explicit precision and optional provider place ID/geocoded timestamp; migration/backfill is non-blocking; address index is not added without a query need.
- **Estimated complexity:** L
- **Dependencies:** P08-02 and P07-02.
- **Resume/portfolio value:** Strong external-integration story covering HTTP resilience, options, caching, privacy, and degraded operation.

## Phase 9A — React business-workspace migration

### F01-01 — Unify business screens in the existing React application

- **Priority:** User requested this before Docker on 2026-10-18.
- **Implementation status:** Implemented with clean Release build, 583 backend tests, 85 frontend tests, 54 focused SQL/security cases and no model drift. PR review fixed tenancy status-name serialization with three HTTP regressions and a strengthened SQL assertion. Browser checks use fictional isolated fixtures, not a claimed live-browser database journey. See the report below for retained compatibility boundaries.
- **Scope:** Public property catalog/details, landlord/Admin overview, owned/all property management, tenancy invitation/ending/acceptance, maintenance queue/triage, and Admin role management. Retain the .NET backend, SQL model, Identity pages and MVC compatibility routes.
- **Implementation:** React business modules and role-aware shared navigation; focused workspace/admin API projections delegating to existing services; explicit CSRF-protected logout; versioned writes; API-safe recent-password response. Root entry and dashboard chooser select React; existing explicit MVC routes remain functional.
- **Acceptance:** No frontend-only authorization; no direct EF/Identity entity binding; stale data clears on role/session changes; writes are not retried automatically; concurrency, last-admin, consent and retention rules stay intact. No schema or package change.
- **Verification:** See [REACT_BUSINESS_WORKSPACES.md](REACT_BUSINESS_WORKSPACES.md) for the current run scope, retained Razor boundary and every changed file.

## Phase 10 — Docker and local development

### P10-01 — Create a secure multi-stage application image

- **Implementation status:** Complete locally: digest-pinned Node 24/.NET SDK stages and .NET 10.0.13 runtime, explicit prebuilt frontend publication, expanded secret/output exclusions and OCI metadata. Non-root/read-only SQL/HTTP smoke and actual public browser flow pass; Release build/583 backend tests/model check pass; patched image scan reports zero HIGH/CRITICAL findings. Raw historical SQL scripts failed bootstrap compilation; the disposable smoke uses EF's migrator instead, without changing migration history. See [CONTAINER_VERIFICATION.md](CONTAINER_VERIFICATION.md) for evidence and deployment boundaries; Compose is separate P10-02.
- **Why it matters:** A reproducible image proves the combined .NET/React application can be built and run consistently outside a developer IDE.
- **Files/components likely affected:** new `Dockerfile`, `.dockerignore`, project/client build configuration, runtime configuration documentation, health endpoint dependency from Phase 13 if available later.
- **Implementation steps:**
  1. Use pinned .NET 10 SDK/runtime base images and a Node build stage for `ClientApp`.
  2. Restore dependency manifests before source copies for cache efficiency; publish the app in Release.
  3. Use the ASP.NET non-root user, a read-only-compatible filesystem where practical, explicit port, and minimal runtime image.
  4. Exclude Git, secrets, tests/results, local databases, IDE data, and build output from context.
  5. Add OCI labels/version metadata and document configuration injection.
- **Acceptance criteria:** A clean image build includes React assets, starts as non-root, exposes only the expected port, contains no local secrets/source-control metadata, and serves MVC/API/static content.
- **Tests required:** Container build; non-root assertion; startup/HTTP smoke; image content/config inspection; vulnerability scan initially informational then policy-driven in CI.
- **Security considerations:** Pin supported image lines/digests according to update policy; no secrets in layers or build arguments; minimize packages and filesystem permissions.
- **Database implications:** The image does not contain a database and does not automatically migrate production on every replica start.
- **Estimated complexity:** M
- **Dependencies:** P06-02 and P01-01.
- **Resume/portfolio value:** Demonstrates multi-stage container builds, least-privilege runtime, and full-stack artifact packaging.

### P10-02 — Add a one-command Docker Compose development environment

- **Implementation status:** Implemented locally: private setup, loopback HTTPS, private SQL, explicit guarded migration/admin job, persistent SQL/key volumes, scoped confirmed reset and a CI lifecycle smoke. Release build/596 backend tests/model check pass; real HTTPS login/API/SQL save, restart/down-up cookie/data persistence, repeatable initialization, graceful shutdown and reset tests pass. Real browser sign-in and React property creation also pass without fixtures. See [LOCAL_COMPOSE.md](LOCAL_COMPOSE.md) for commands, verification and every changed file. Production hosting/health and full CI expansion remain separate phases.
- **Why it matters:** Local SQL Server setup is currently machine-specific. Compose makes onboarding and integration testing predictable while retaining SQL Server fidelity.
- **Files/components likely affected:** new `compose.yml` and optional development override, `.env.example`, app settings/options, migration/seed helper command or profile, README/runbook.
- **Implementation steps:**
  1. Define app and SQL Server services with explicit networks, named database volume, health checks, and dependency readiness.
  2. Supply non-secret example variables and require local secret values through ignored environment files or user secrets.
  3. Add an explicit one-shot migration/seed operation rather than coupling schema changes to every web startup.
  4. Document start, logs, migrate, reset-test-data, stop, and volume-removal commands; make destructive reset opt-in and clearly scoped.
  5. Verify macOS/Linux developer behavior and sensible resource requirements.
- **Acceptance criteria:** A new developer can start app plus SQL Server from documented commands; readiness prevents misleading startup failures; data persists across normal restarts; reset is explicit; no secret is committed.
- **Tests required:** Clean-volume bring-up, migration, app smoke, restart persistence, missing-secret failure, graceful shutdown; CI Compose smoke where affordable.
- **Security considerations:** SQL is not exposed beyond local need; example credentials are not production defaults; secrets are not printed; container capabilities/ports are minimal.
- **Database implications:** Named local volume, explicit migration job, development/demo seeding only; reset procedure destroys only the named Compose volume after confirmation.
- **Estimated complexity:** M
- **Dependencies:** P10-01.
- **Resume/portfolio value:** Shows reproducible developer experience, SQL Server containers, health ordering, migrations, and secrets discipline.

## Phase 11 — CI/CD

### P11-01 — Turn CI into a complete pull-request quality gate

- **Implementation status:** Implemented — pinned read-only actions, frontend/backend/container quality jobs, locked NuGet dependencies, CI warnings policy, full disposable SQL suite, coverage/TRX/JUnit artifacts, local pinned EF drift check, image scan and existing HTTPS Compose lifecycle. The actual React build crosses into backend HTTP tests. After explicit owner authorization, `main` requires a PR and an up-to-date passing aggregate from GitHub Actions, including for admins; force pushes/deletion are blocked. Patch whitespace is enforced without reformatting the legacy C# baseline. See [CI.md](CI.md) and the linked PR checks for exact scope and verification.

- **Why it matters:** The current uncommitted workflow proves intent but only restores/builds/tests .NET. A full-stack project needs deterministic backend, frontend, migration, and container checks.
- **Files/components likely affected:** `.github/workflows/ci.yml`, solution/build settings, package and npm lockfiles, formatter/linter configuration, test coverage/reporting, integration-test container settings, branch-protection documentation.
- **Implementation steps:**
  1. Pin action versions and least-privilege permissions; add concurrency cancellation for superseded runs.
  2. Restore/build the solution once with locked dependencies and warnings policy.
  3. Run unit and SQL Server integration tests, collect useful test/coverage artifacts, and verify EF has no pending model changes.
  4. Run frontend install, lint, type-check, tests, and production build.
  5. Verify formatting and build the Docker image; add actionable dependency/vulnerability checks with a documented response rule.
  6. Document required branch-protection checks and keep secrets unavailable to untrusted PR code.
- **Acceptance criteria:** A pull request cannot pass with compile errors, failing backend/frontend tests, type errors, model drift, or a broken image build; duplicated restore/build work is minimized; logs/artifacts make failures diagnosable; permissions are read-only unless a job needs more.
- **Tests required:** The workflow executes all repository test suites plus migration and image smoke checks; test a deliberate failing branch before requiring the gate.
- **Security considerations:** Guard fork PRs from secrets; pin trusted actions; use minimal permissions; review dependency automation/noise; do not upload logs containing sensitive configuration.
- **Database implications:** CI uses disposable SQL Server and applies migrations from zero; it never points to shared/development/production data.
- **Estimated complexity:** L
- **Dependencies:** P10-02 and P04-02.
- **Resume/portfolio value:** High CV value: mature CI for .NET, React, SQL migrations, containers, coverage, and supply-chain basics.

### P11-02 — Produce immutable, traceable release artifacts

- **Implementation status:** Implemented for review; live activation pending merge/owner approval — chosen private GHCR, manual dispatch only, exact protected-main push CI/attempt guard, isolated package-write job with configured owner approval, unique source/run/attempt tags, tested-image digest pull and Production smoke, CycloneDX SBOM/checksums and explicit image/migration/deployment/rollback metadata. PR CI rehearses against a disposable loopback registry without GHCR write credentials. No shared database migration, executable migration bundle, signed provenance, cloud deployment or live GHCR publication is claimed. See [RELEASES.md](RELEASES.md) for activation, scope and verification.

- **Why it matters:** Continuous delivery starts with a repeatable artifact tied to tested source; rebuilding differently during deployment undermines confidence.
- **Files/components likely affected:** new release workflow, container registry settings, version/label generation, changelog/release notes automation, SBOM/provenance configuration, deployment interface documentation.
- **Implementation steps:**
  1. On protected main/tags, promote or rebuild under the same locked inputs after required CI succeeds.
  2. Tag the image with commit SHA and semantic release tag where used; never rely only on `latest`.
  3. Publish to a selected registry with least-privilege short-lived credentials and generate digest/SBOM metadata.
  4. Define the deployment inputs (image digest, environment, migration artifact) that Phase 12 consumes.
  5. Add manual approval/rollback metadata without deploying to an undefined cloud target yet.
- **Acceptance criteria:** Every release artifact maps to a commit and passing workflow; image digest is recorded; older artifact can be selected for rollback; registry write permission is isolated from PR jobs.
- **Tests required:** Release workflow dry run in a non-production registry/repository, image pull/start smoke, tag/digest/label assertions, failed-CI non-publication test.
- **Security considerations:** Prefer GitHub/Azure workload identity or short-lived registry credentials; sign/provenance according to available tooling; do not execute untrusted release inputs.
- **Database implications:** Publish reviewed migration scripts/bundles alongside the app where adopted; do not run them in this task.
- **Estimated complexity:** M
- **Dependencies:** P11-01.
- **Resume/portfolio value:** Demonstrates artifact immutability, traceability, container registry use, and safe separation of build from deploy.

## Phase 12 — Azure deployment preparation

**Scope decision (2026-10-23):** Azure infrastructure and cloud deployment are
deferred by user choice. Do not provision paid resources or treat Azure as a
prerequisite for the local/standalone portfolio demo. The main release rehearsal
passed on 2026-10-21; GHCR publication remains deferred (rehearsal only). No cloud
deployment or live private-registry verification is claimed.

### P12-01 — Define minimal Azure infrastructure as Bicep

- **Why it matters:** Azure readiness is credible when infrastructure is repeatable, reviewable, least-privilege, and cost-aware—not a list of portal screenshots.
- **Files/components likely affected:** new `infra/` Bicep modules/parameters, architecture diagrams, environment examples, validation workflow, cost/security notes.
- **Implementation steps:**
  1. Provision a minimal Linux App Service/plan, Azure SQL server/database, Key Vault, Application Insights/Log Analytics, managed identity, and registry only if the release path needs it.
  2. Parameterize names, region, SKU, allowed ingress, retention, and environment; keep secret values out of parameter files.
  3. Grant managed identity only required Key Vault/SQL/registry access and prefer Entra/passwordless SQL where feasible.
  4. Configure TLS, HTTPS-only, diagnostic settings, backup/retention, and cost-safe defaults.
  5. Validate/lint Bicep and document estimated recurring resources and destroy commands.
- **Acceptance criteria:** A clean environment can be previewed/deployed idempotently; no secret appears in source/state outputs; role assignments are scoped; public exposure is documented; teardown is safe and explicit.
- **Tests required:** Bicep build/lint, what-if, deployment to a disposable resource group, idempotent second deployment, policy/security review, teardown verification.
- **Security considerations:** Least privilege, managed identity, Key Vault RBAC, minimum TLS, restricted SQL/network access, protected outputs, no public admin credentials.
- **Database implications:** Provisions Azure SQL with explicit SKU, backup/retention, firewall/private-access decision, and managed-identity access; no application schema is applied automatically.
- **Estimated complexity:** L
- **Dependencies:** P11-02.
- **Resume/portfolio value:** Very high CV value: Azure IaC, managed identity, Azure SQL, Key Vault, telemetry, and cost-aware design.

### P12-02 — Implement secure Azure configuration, migrations, deployment, and rollback

- **Why it matters:** A provisioned environment is not production-ready until configuration, keys, database change order, smoke checks, and rollback are deliberate.
- **Files/components likely affected:** deployment workflow, Bicep outputs, app configuration/options, Key Vault references, SQL auth setup, data-protection configuration, EF migration bundle/script, deployment runbook.
- **Implementation steps:**
  1. Connect GitHub Actions to Azure with OIDC workload federation and protected environments/approvals.
  2. Configure app settings and Key Vault references; persist data-protection keys outside the container.
  3. Run a reviewed, one-shot migration job/script before application promotion; back up and define forward-fix/rollback limitations.
  4. Deploy the immutable image digest, run health and authenticated smoke checks, and stop promotion on failure.
  5. Document rollback of application image separately from irreversible database migrations and test a safe rollback scenario.
- **Acceptance criteria:** Deployment uses no long-lived Azure secret/publish profile; settings validate at startup; migration runs once and is observable; failed health/smoke checks halt promotion; image rollback is demonstrated; production startup does not seed a development admin.
- **Tests required:** Disposable-environment deployment, secret/config failure, migration success/failure, smoke tests, key persistence across restarts/scale, previous-image rollback rehearsal.
- **Security considerations:** Environment approvals, least-privilege federated identity, secret redaction, protected Key Vault/SQL access, durable data protection, no development diagnostics/admin seed.
- **Database implications:** Explicit backup/migration order, migration identity permissions, zero/low-downtime compatibility review, documented non-reversible changes and recovery plan.
- **Estimated complexity:** L
- **Dependencies:** P12-01 and P11-02. Use a minimal deployment smoke check here; Phase 13 adds the complete health and reliability model afterward.
- **Resume/portfolio value:** Shows real deployment engineering: workload identity, secrets, migration safety, health gates, and rollback reasoning.

## Phase 13 — Observability and reliability

### P13-01 — Add structured logging, safe exception handling, tracing, and metrics

- **Incremental implementation (2026-10-24):** Local diagnostics foundation: UTC JSON console events; allow-listed request summaries with successful-log sampling; W3C correlation and safe API/MVC failure handling; bounded HTTP/SQL metrics; sync/async EF timing/failure spans and optional geocoding spans without SQL/URI/credential payloads; startup version metadata and privacy/configuration regressions. No new package, schema change, cloud exporter, account or paid resource. This does not complete all P13 goals: workflow-specific event expansion, collector/export selection, hosted telemetry controls, dashboards and automated alerts remain unimplemented; P13-02 health/resilience remains separate. See [OBSERVABILITY.md](OBSERVABILITY.md) for exact scope, operating limits, tests and every changed file.

- **Why it matters:** Production failures must be diagnosable across MVC, API, EF, Identity, and external calls without leaking tenant content or credentials.
- **Files/components likely affected:** `Program.cs`, global exception handler/Problem Details customization, logging event definitions, OpenTelemetry/Application Insights registration, request correlation middleware, redaction rules, service/external-client instrumentation, dashboards/alerts documentation.
- **Implementation steps:**
  1. Define structured event names/IDs and fields for key workflows, authorization denials, status transitions, external calls, and deployment/version metadata.
  2. Centralize expected application-error mapping and unexpected exception handling for MVC/API.
  3. Propagate W3C trace context and instrument ASP.NET Core, HttpClient, and EF at an appropriate level.
  4. Export to console locally and Application Insights/OpenTelemetry target in Azure; validate options and sampling.
  5. Define useful low-cardinality metrics/alerts for latency, error rate, dependency failure, queue size, and request transitions.
  6. Document and test PII/secret redaction.
- **Acceptance criteria:** A request can be correlated from HTTP through SQL/external call; errors include trace ID but no stack/SQL details to clients; logs are structured/queryable; maintenance descriptions, tokens, passwords, connection strings, and full claims are absent; alert thresholds have an owner/action.
- **Tests required:** Exception-to-response integration tests, structured-log capture assertions for key events and forbidden fields, trace propagation test, telemetry-disabled local behavior, sampling/config validation.
- **Security considerations:** Data minimization and redaction are mandatory; restrict telemetry access/retention; prevent log injection; use internal IDs only when necessary.
- **Database implications:** EF telemetry must not capture sensitive parameter values in production; audit/history remains in SQL rather than relying on application logs.
- **Estimated complexity:** L
- **Dependencies:** P09-01 and P03-01; useful pieces may be introduced earlier and consolidated here.
- **Resume/portfolio value:** High CV value: structured logging, distributed tracing concepts, metrics, safe error handling, and Azure telemetry.

### P13-02 — Add health checks, dependency resilience, and an operational runbook

- **Incremental implementation (2026-10-25):** Separate GET/HEAD liveness and SQL readiness endpoints, minimal noncacheable status bodies, a three-second SQL cancellation budget with no retries, two concurrent readiness probes per process and no queue, plus an operational [runbook](RUNBOOK.md). Liveness bypasses SQL-backed cookie validation. Tests cover timeout/cancellation, safe failures, overload, real disposable-SQL outage/recovery and optional-provider isolation; existing image/Compose smoke scripts now exercise both HTTP probes. No schema, package, cloud or shared-database changes. Ongoing Docker/Azure HTTP probe wiring, broader load/pool-pressure testing, hosted alerts and deployment-specific recovery rehearsals remain separate; this is not completion of all P13-02 gates.

- **Why it matters:** Deployment and support need clear liveness/readiness semantics, bounded dependency failures, and documented recovery actions.
- **Files/components likely affected:** health-check registration/endpoints, SQL readiness check, external-client resilience settings, container/Azure probes, retry/timeout options, `docs/RUNBOOK.md`, load/failure tests.
- **Implementation steps:**
  1. Add separate liveness and readiness endpoints with minimal public detail; readiness checks SQL and required configuration.
  2. Keep optional geocoding/AI dependencies out of core readiness while reporting their telemetry/degraded state.
  3. Standardize timeouts, cancellation, bounded retries with jitter for idempotent calls, and circuit/rate-limit handling.
  4. Configure Docker and Azure health probes with realistic startup/failure thresholds.
  5. Write runbook steps for database unavailable, migration failure, external-provider outage, elevated errors, rollback, and credential rotation.
  6. Run basic load and fault-injection scenarios to identify timeouts and connection-pool pressure.
- **Acceptance criteria:** Liveness stays healthy during an optional-provider outage; readiness fails safely when SQL is unavailable; probes do not leak configuration; retries do not amplify sustained outages; the runbook maps alerts to concrete checks/actions.
- **Tests required:** Health endpoint integration tests, SQL outage/recovery, external timeout/circuit behavior, cancellation propagation, retry count bounds, container/Azure probe smoke, modest load/fault test.
- **Security considerations:** Health details are protected or minimal; no dependency URLs/credentials/exceptions are public; retries cannot bypass rate limits or duplicate non-idempotent writes.
- **Database implications:** Readiness uses a cheap bounded check; load testing observes connection pools and query latency without using production data.
- **Estimated complexity:** M
- **Dependencies:** P13-01 and P10-02. Complete before the final production deployment gate in P12-02 even though preparation can start earlier.
- **Resume/portfolio value:** Demonstrates reliability engineering, health semantics, failure containment, and operational readiness.

## Phase 14 — Optional AI feature

### P14-01 — Add human-reviewed AI maintenance triage behind a feature flag

- **Why it matters:** A narrow advisory feature can demonstrate responsible AI integration without making core property workflows dependent on probabilistic output.
- **Files/components likely affected:** AI triage contract/client, validated options/feature flag, maintenance application service, React/Razor suggestion UI, redaction/consent logic, evaluation dataset/tests, telemetry and ADR.
- **Implementation steps:**
  1. Write a privacy/threat/model-cost ADR and define an evaluation set before selecting a provider/model.
  2. Limit input to the minimum maintenance text; remove names, email, address, IDs, and attachments unless explicitly required and approved.
  3. Request structured category/priority/summary suggestions with schema validation, timeout, cost/token caps, and deterministic fallback.
  4. Present output as an editable suggestion requiring human confirmation; never auto-change priority/status or block submission.
  5. Feature-flag the capability, record model/config version and acceptance metrics without storing raw prompts in normal logs.
  6. Test disabled, unavailable, invalid, unsafe, slow, and low-confidence behavior.
- **Acceptance criteria:** Core maintenance works identically when AI is disabled/down; users can reject/edit suggestions; no autonomous housing/workflow decision occurs; evaluation quality/cost limits are documented; personal data sent to the provider is minimized and disclosed.
- **Tests required:** Schema/parser unit tests, prompt-injection/adversarial cases, redaction tests, timeout/rate-limit/fallback integration tests, feature-flag test, fixed evaluation suite, UI human-confirmation test.
- **Security considerations:** Treat model output as untrusted; defend against prompt injection and data exfiltration; no secrets/system prompts in client; review provider retention/residency; rate/cost limit per user; no sensitive logs.
- **Database implications:** Optional fields for accepted AI suggestion/model metadata only if needed; do not persist raw prompt/response by default; migration remains backward-compatible.
- **Estimated complexity:** L
- **Dependencies:** P13-02, P05-03, representative synthetic/evaluation data, and explicit decision to proceed.
- **Resume/portfolio value:** Valuable only when framed around evaluation, privacy, resilience, and human control; a shallow chatbot would have less value.

## Phase 15 — GitHub/README/portfolio polish

### P15-01 — Make the repository contributor- and PR-friendly

- **Why it matters:** Reviewers judge engineering maturity from setup accuracy, commit scope, issue/PR hygiene, and decision history as much as from screenshots.
- **Files/components likely affected:** `README.md`, `CONTRIBUTING.md`, `SECURITY.md`, `LICENSE`, `.github` PR/issue templates, CODEOWNERS if appropriate, Dependabot/Renovate configuration, ADR index, roadmap/task links, `.editorconfig`, release notes.
- **Implementation steps:**
  1. Replace outdated claims and absolute local links with portable commands/relative links; describe Razor/React/API boundaries accurately.
  2. Document prerequisites, user secrets, Compose, migrations, tests, frontend workflow, demo accounts/data, Docker, and Azure deployment.
  3. Add concise contribution/branch/commit/PR/migration/security-reporting guidance and checklists.
  4. Add architecture diagrams/ADRs and explain major trade-offs, including rejected over-engineering.
  5. Configure dependency updates with sensible grouping/cadence and an ownership process.
  6. Replace template privacy copy and duplicated/unpinned external assets according to the actual deployed behavior.
- **Acceptance criteria:** A new contributor can run/test the stack from README alone; links work outside the original machine; repository status/features are truthful; PR templates request tests/security/database evidence; license/security policy are explicit.
- **Tests required:** Follow setup on a clean environment/container; markdown/link check; command verification; dependency-bot configuration validation; screenshot accessibility/text review.
- **Security considerations:** Documentation never includes usable secrets, tenant data, internal Azure identifiers, or insecure demo defaults; security reporting is private and clear.
- **Database implications:** Setup/reset/migration/backup commands clearly distinguish local, CI, and production and warn before destructive actions.
- **Estimated complexity:** M
- **Dependencies:** P11-01, P12-02, and P13-02 for final accuracy; incremental updates occur throughout.
- **Resume/portfolio value:** Makes the engineering discoverable and signals professional Git/PR collaboration practices.

### P15-02 — Publish a concise portfolio case study and reproducible demo

- **Incremental implementation (2026-10-23):** Added an optional four-step tenant demo guide, confirmed in-memory reset, scoped responsive controls, local favicon, six journey/isolation regressions, three current browser screenshots and an evidence-linked [case study](PORTFOLIO_CASE_STUDY.md). All 91 frontend tests, lint, type checks and normal/static builds pass; the frontend audit reports no vulnerabilities. The demo still makes no API calls and simulates only the tenant experience. This is partial portfolio preparation, not completion of all P15 gates: public hosting, stable release/tag, all-role authenticated captures and complete operational readiness remain separate. Azure is deferred by choice. See [showcase scope and file inventory](PORTFOLIO_SHOWCASE.md).

- **Why it matters:** The strongest implementation has limited hiring value if reviewers cannot quickly see the problem, architecture, evidence, and working journey.
- **Files/components likely affected:** README portfolio section, screenshots/GIF/video links, demo seed profile, release/tag, architecture/performance/test reports, hosted demo/runbook/status note.
- **Implementation steps:**
  1. Create synthetic demo data and role-specific demo journeys without publishing reusable privileged credentials.
  2. Capture the tenant-to-maintenance workflow, API/OpenAPI, tests, CI, Docker, and Azure architecture in concise visuals.
  3. Present measurable evidence: test suite scope, selected query-plan improvement, image/deployment flow, reliability behavior, and key security controls.
  4. Write a short case study covering constraints, trade-offs, what was deliberately postponed, and lessons learned.
  5. Tag a stable release and verify the public demo/cost controls or provide a fully reproducible local demo if continuous hosting is not justified.
- **Acceptance criteria:** A reviewer can understand the project in five minutes and reproduce it from documented steps; all demo data is synthetic; screenshots match the current release; claims link to code/tests/docs; hosting has a cost/shutdown plan.
- **Tests required:** Clean-machine demo rehearsal, all three role journeys, public links/assets, release artifact/image digest, accessibility/responsive smoke, no-secret scan.
- **Security considerations:** No shared permanent admin password; reset demo state; rate-limit/protect hosted environments; strip personal/cloud metadata from screenshots and logs.
- **Database implications:** Demo seeding is idempotent, environment-gated, and never runs against production unintentionally; cleanup/retention is documented.
- **Estimated complexity:** M
- **Dependencies:** P15-01 and a stable deployed/releasable application. P14-01 is not required.
- **Resume/portfolio value:** Converts the technical work into credible, evidence-backed CV and interview material.

## Recommended starting sequence

The first five tasks are:

1. P01-01 — Establish a supported, reproducible baseline.
2. P01-02 — Define tenancy and maintenance domain invariants.
3. P01-03 — Align EF persistence and current property lifecycle behavior.
4. P02-01 — Introduce application services around core use cases.
5. P02-02 — Add explicit contracts, validation, mapping, and query models.

Keep each task in a focused branch/PR. For L tasks, split mechanical setup, domain changes, migration, and UI wiring into a short PR stack while preserving the dependency order and acceptance criteria.

## Value and postponement guide

### Highest CV value

- P02-01, P03-01, P03-02 — application architecture and REST API.
- P04-02 — real ASP.NET Core + SQL Server integration testing.
- P06-01, P06-02 — React/TypeScript full-stack delivery.
- P10-01, P10-02 — Docker and reproducible local development.
- P11-01, P11-02 — full CI and immutable release artifacts.
- P12-01, P12-02 — Azure, Bicep, managed identity, and safe deployments.
- P13-01 — structured logging and telemetry.

### Highest interview value

- P01-02 — explaining tenancy as the maintenance authorization root.
- P02-01 — service boundaries and the decision not to wrap EF in generic repositories.
- P04-02 — test fidelity, Identity/CSRF testing, and Testcontainers trade-offs.
- P07-01 — query plans and evidence-led indexes.
- P07-02 — races, SQL constraints, rowversion, and migration compatibility.
- P08-01, P08-02 — resource authorization, OIDC, cookies, and antiforgery.
- P09-01 — timeouts, retries, caching, privacy, and graceful degradation.

### Postpone until the foundations exist

- P09-01 geocoding: useful, but not before the domain/API/tests/UI are coherent.
- P12-01/P12-02 Azure: not before a reproducible, tested container artifact.
- P13-02 advanced load/fault tuning: not before the real deployment/query topology exists.
- P14-01 AI: optional and last among product features; do not use it to distract from incomplete maintenance rules.
- P15-02 public demo/case study: polish after evidence is stable.
- Any microservices, payment processing, real-time chat, mobile app, Kubernetes, event sourcing, generic workflow engine, full Razor rewrite, or custom token server: postpone indefinitely unless a genuine requirement changes the architecture.
