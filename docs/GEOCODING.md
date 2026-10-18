# Optional property locations — P09-01

Implemented using Geoapify, **off by default**. No account was created, no live
address lookup was performed, and no shared/developer database was migrated.
The user-approved provider decision and source/terms review are in
[ADR 0001](adr/0001-optional-geoapify-geocoding.md).

## Delivered behavior

- A successful property create or actual address edit may attempt optional lookup
  after its core SQL save. Title/rent/description-only changes and all reads do not.
- Manual address entry/save remains usable when lookup is disabled, unavailable,
  malformed, timed out, rate-limited or returns no suitable match. Lookup is bounded
  in the request, not an untracked background task. Cancellation after commit does
  not turn the committed property save into a reported optional-provider failure.
- Address edits atomically clear all four old location facts before attempting a
  new match, even for case/trailing-space changes that SQL string equality can hide.
- Only authenticated owning Landlords or Admins may POST
  `/PropertyLocations/Refresh/{id}`, with antiforgery and the displayed
  `ExpectedVersion`. Tenant/anonymous and cross-owner requests cannot trigger lookup.
  Missing/malformed versions get 400, stale versions redirect with a reload message,
  and missing/non-owned properties return the same 404. Actor/ownership checks precede
  version validation. There is no caller-supplied address, ownership or coordinate.
- A late lookup is persisted only if property ID, saved owner, address and observed
  SQL rowversion still match. Any concurrent edit/delete/reassignment discards it.
- Razor property details display an approximate coordinate card with UTC observation
  time, required Geoapify/OSM attribution and an explicit OpenStreetMap link. There
  is no embedded map or automatic third-party browser request. No coordinates means
  the existing address-only experience. Managers see a retry form only when enabled.
- Enabled create/edit forms warn that saved addresses go to Geoapify. Tenant names
  and private material do not belong in the address. Keys stay server-side; provider
  references and raw responses are not rendered. Existing public API DTOs/routes,
  form fields and the tenant React portal are unchanged.

## Configuration — later, outside source

First apply the reviewed `20261018071947_OptionalPropertyGeocoding` and
`20261018080827_ExpandGeocodingPlaceId` migrations to a
confirmed safe target using the existing deployment workflow. It adds nullable
`Latitude`/`Longitude` (`decimal(9,6)`), a bounded `GeocodingPlaceId` and UTC
`GeocodedUtc`, with an all-or-none/range constraint. Existing rows retain nulls;
no external calls, data backfill, new index or rental/Identity relationship change
is included. The follow-up migration widens the provider reference from 512 to
2048 characters without rewriting the original migration. Reverting that widening
requires exporting/removing references longer than 512 before narrowing; do not
truncate them silently. Reverting the initial migration drops optional location
data only; export it first if needed.
The application never migrates or enables this feature automatically.

Then create/configure your own provider project and review privacy/terms/quotas.
For local Development, these are example commands, **not executed by this task**:

```sh
dotnet user-secrets set "Geocoding:ApiKey" "<your-provider-key>"
dotnet user-secrets set "Geocoding:Enabled" "true"
```

Deployment equivalents are `Geocoding__ApiKey` and `Geocoding__Enabled` in the host's
secret settings. Never place the key in Git, a URL shared with users, Razor, browser
storage or `VITE_*` variables. Keep the key specific to this application. Set
`Geocoding:Enabled` to `false` to stop lookups; existing stored estimates may still
be displayed without contacting the provider. Disabled lookup needs no key.

Optional settings: `Geocoding:TimeoutSeconds` (default 3, allowed 1–10),
`Geocoding:DailyRequestLimit` (default 2500, allowed 1–3000). Enabled invalid options
fail startup without echoing the key. Disabled options do not require credentials.

## HTTP safety, caching and cost controls

The endpoint is fixed at `https://api.geoapify.com/v1/geocode/search`. Address and
API-key query parameters are escaped; redirect following/cookies are disabled.
TLS validation stays enabled. There is no configurable user URL/SSRF proxy or
incoming tenant/user data forwarded to the provider. Named-client factory logging
is removed because the protocol places credentials/addresses in its query string.
Future tracing/proxy diagnostics must also be reviewed before production; do not
enable sensitive request/EF logging. Application warnings never include dependency
exceptions, response bodies or URIs.

Requests normalize Unicode/whitespace and use case-normalized SHA-256 cache keys,
not logged/raw addresses. At most 256 bounded results are cached per process:
successful matches for 24 hours, no suitable match for five minutes. Transport,
malformed-response and dependency failures are not cached as valid coordinates.
Explicit retry can reuse a fresh cache entry; it is not a forced cache bypass.

Only one lookup runs per process; concurrent cache misses are shed, not queued.
HTTP attempts start at most once per second and share the UTC daily attempt budget;
the second attempt counts too. Only transient network/selected 5xx errors retry,
at most once within the same total lookup timeout. 4xx/redirects/malformed bodies do
not retry. 429/Retry-After sets a shared cooldown without immediate retry. A cooldown
over a day conservatively blocks new calls until operator review/process restart
rather than retrying earlier than requested. Cached reads remain available.

These are process-local controls, not a hard billing cap: restart/replicas reset or
multiply budgets, other uses of the same key consume credits, and provider accounting
may differ. Set provider-side quota controls/alerts and reassess before scaling.

Responses are limited to 64 KiB, including unknown-length streams, and JSON depth
16. Coordinates must be numeric/in range, the reference bounded to 2048 characters, the result an
OSM-backed building/amenity/street and confidence at least 0.8. Coarse centroids,
unsupported sources and uncertain results remain address-only. Coordinates are
rounded to six decimal places, never treated as verified boundaries.

`RentRight.Geocoding` exposes `rentright.geocoding.lookups` with only bounded
`outcome` and `cached` tags. No address, key, user, property ID or response text is
used as a metric label. The retained UTC time reflects the observed cached/provider
result persistence, not an invented provider update time.

## Verification

Provider unit tests use controlled handlers only. SQL tests use random disposable
Testcontainers databases, real Identity/resource authorization/antiforgery and fake
provider responses. One test runs the actual HTTP adapter through create → parsed
response → conditional persistence → Razor display and verifies query secrets are
absent from captured logs. Test factories explicitly disable inherited live lookup;
the enabled-adapter test overrides every factory-created transport with a fake.

| Check | Result |
| --- | --- |
| Release solution build | 0 warnings/errors. |
| Final fast backend suite | 560 passed, 0 failed/skipped, including the pre-merge provider-reference boundary cases. |
| Focused geocoding/property service tests | 87 passed, 0 failed/skipped. |
| New SQL/HTTP geocoding suite | 23 passed, 0 failed/skipped, including 2048-character reference persistence. |
| Existing SQL regression selection | 83 passed, 0 failed/skipped, 6m56s: `SqlPipelineTests`, `IntegrityConcurrencyRetentionSqlTests`, `QueryRegressionTests`, `QueryIndexMigrationTests`. |
| Final fake-HTTP adapter journey after test-host configuration guard | 1 passed; a rerun of an existing case, not an additional distinct case. |
| Node 24 frontend | After merging the reviewed showcase into this branch, lint, type check/build and all 62 combined tests passed. P09-01 adds no frontend dependencies or source changes of its own. |
| EF/model/migration | No pending model drift; reviewed Up/Down, incremental SQL and snapshot; real SQL migration rehearsal passed. |
| Scope | 37 files, including the pre-merge reference-length fix, matching the inventory below; no shared database, account, key or live lookup used. |

There are 106 distinct passing SQL cases across these runs, not a claim that every
SQL test was rerun. The first builds caught two test constructor/helper updates;
those were corrected without changing behavioral assertions. No live provider
accuracy, production deployment or real-browser/assistive-technology audit is claimed.

Pre-merge review accepts provider references through 2048 characters and rejects
2049 at both the HTTP adapter and application boundary. SQL coverage checks full
reference persistence, initial null metadata, preservation of an existing
512-character reference through the widening migration, and rollback/reapplication.
The original migration is unchanged. No shared/developer database was updated.

Ignored TRX evidence is under `artifacts/test-results/p09-01/`. Run the normal
Release solution checks plus the SQL project with a filter for
`PropertyGeocodingSqlTests`; broader existing property/concurrency/query tests
remain part of regression verification. Do not point tests at a shared database.

## Changed files

| File | Purpose |
| --- | --- |
| `Models/Property.cs` | Nullable, server-controlled location facts. |
| `Data/ApplicationDbContext.cs` | Precision, bounded reference and location consistency/range constraint. |
| `Migrations/20261018071947_OptionalPropertyGeocoding.cs` | Incremental nullable-column/check migration and optional-data rollback. |
| `Migrations/20261018071947_OptionalPropertyGeocoding.Designer.cs` | Generated target model for the new migration. |
| `Migrations/20261018080827_ExpandGeocodingPlaceId.cs` | Preserve valid provider references up to 2048 characters; narrowing rollback requires checking existing lengths. |
| `Migrations/20261018080827_ExpandGeocodingPlaceId.Designer.cs` | Generated target model for the widening migration. |
| `Migrations/ApplicationDbContextModelSnapshot.cs` | Updated current model only. |
| `Integrations/Geocoding/GeocodingContracts.cs` | Focused provider contract/results and disabled-by-default options. |
| `Integrations/Geocoding/GeoapifyGeocodingClient.cs` | Fixed HTTPS protocol, bounds, quality, cancellation, retries and outcome metrics. |
| `Integrations/Geocoding/GeocodingState.cs` | Bounded shared cache, single-flight gate, cooldown and attempt budget. |
| `Integrations/Geocoding/GeocodingServiceCollectionExtensions.cs` | Validated settings, typed client and safe transport/logging configuration. |
| `Features/Properties/PropertyLocationService.cs` | Authorized enrichment/retry orchestration and conditional EF writer. |
| `Features/Properties/PropertyService.cs` | After-commit create/address-change hooks and detail presentation. |
| `Features/Properties/PropertiesServiceCollectionExtensions.cs` | Scoped location service/writer registration. |
| `Features/Properties/IPropertyStore.cs` | Optional read projection and trusted address-change write flag. |
| `Features/Properties/EfPropertyStore.cs` | Location detail projection and atomic address-change invalidation. |
| `Features/Properties/Contracts/PropertyContracts.cs` | Location presentation without changing posted input fields. |
| `Controllers/PropertyLocationsController.cs` | Dedicated antiforgery-protected, versioned MVC manager retry input/action. |
| `Views/Properties/_Location.cshtml` | Estimated location, attribution, explicit map link and manager retry form. |
| `Views/Properties/_LocationNotice.cshtml` | Enabled-only address-sharing notice. |
| `Views/Properties/Details.cshtml` | Include the optional location presentation. |
| `Views/Properties/Create.cshtml` | Include address-sharing notice when enabled. |
| `Views/Properties/Edit.cshtml` | Same notice for edits. |
| `Program.cs` | Register the optional integration without enabling it. |
| `RentRight.Tests/Geocoding/GeoapifyGeocodingClientTests.cs` | Fake protocol, limits, caching, retry, cancellation and configuration/transport coverage. |
| `RentRight.Tests/Geocoding/PropertyLocationServiceTests.cs` | Ownership/version/failure/no-write application-service coverage. |
| `RentRight.Tests/Properties/PropertyServiceTests.cs` | After-save invocation and address-change invalidation contracts. |
| `RentRight.Tests/SqlIntegration/PropertyGeocodingSqlTests.cs` | Real SQL/HTTP role matrix, races, constraints, migration rehearsal and adapter/UI journey. |
| `RentRight.Tests/SqlIntegration/SqlApplication.cs` | Explicit fake-provider configuration hook and disabled inherited live settings. |
| `RentRight.Tests/SqlIntegration/Performance/QueryRegressionTests.cs` | Supply the disabled optional dependency without changing query assertions. |
| `RentRight.Tests/Api/RentRightApplicationFactory.cs` | Prevent HTTP tests inheriting live provider activation. |
| `docs/adr/0001-optional-geoapify-geocoding.md` | Provider/terms/privacy/cost decision and tradeoffs. |
| `docs/GEOCODING.md` | Configuration, rollout, safety, verification and complete file inventory. |
| `docs/TASKS.md` | Actual P09-01 implementation and operational checkpoint. |
| `docs/ARCHITECTURE.md` | Record the implemented optional integration boundary. |
| `docs/API.md` | Preserve public contracts and explain optional write/version effects. |
| `README.md` | Current feature, default-off behavior and migration/setup link. |
