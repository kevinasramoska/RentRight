# RentRight API conventions

## Current scope

Phase 3 exposes a versioned transport contract under `/api/v1`. P03-01 establishes
the shared conventions described here, and P03-02 adds the property resource. Tenancy,
maintenance, health, and test resources are not part of the production API.

The runtime-generated OpenAPI document is available at `/openapi/v1.json` only when
the application runs in `Development`. No interactive documentation UI is installed,
and the document endpoint is not mapped in other environments.

## JSON and HTTP contract

- Request and response property names use `camelCase`. JSON input property matching is
  case-insensitive.
- UTC `DateTime` values use the System.Text.Json ISO-8601 representation, including the
  `Z` suffix for UTC values.
- Successful response bodies use `application/json`; RFC 7807 failures use
  `application/problem+json`.
- New controller APIs derive from `ApiV1ControllerBase`, use attribute routing below
  `/api/v1`, declare DTO response types, and accept a `CancellationToken`. Request
  cancellation flows through the application service and persistence calls.
- API contracts must be dedicated DTOs. EF Core and Identity entities are never API
  inputs or outputs.

Paged endpoints bind `ApiPageRequest`. `page` is between 1 and 10,000 and `pageSize`
is between 1 and 100; defaults are 1 and 20. Out-of-range values are validation
failures rather than silently unbounded queries. Responses use this envelope:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 20,
  "totalCount": 0,
  "totalPages": 1
}
```

## Problem Details

All API failures use RFC 7807 fields plus two stable extensions:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/api/v1/example",
  "code": "validation_failed",
  "traceId": "00-...",
  "errors": {
    "displayName": ["The DisplayName field is required."]
  }
}
```

Validation field names follow the JSON naming convention. Application services keep
returning transport-neutral outcomes; the API maps representative outcomes as follows:

| HTTP status | `code` | Meaning |
|---:|---|---|
| 400 | `invalid_request` | Malformed request or antiforgery rejection |
| 400 | `validation_failed` | Model or application validation failed |
| 401 | `authentication_required` | No valid authenticated session |
| 403 | `access_denied` | Authenticated caller is not permitted |
| 404 | `resource_not_found` | Missing or deliberately concealed resource |
| 409 | `resource_conflict` | State, history, or concurrency conflict |
| 500 | `internal_error` | Unexpected server failure |

Problem responses do not include exception messages, stack traces, provider/SQL
details, secrets, or details about deliberately concealed resources. The `traceId`
is the support/correlation handle. A controller must deliberately choose concealed
404 mapping where returning 403 would reveal an inaccessible resource.

## Identity cookie and antiforgery behavior

The planned React client is hosted on the same origin and uses the existing ASP.NET
Core Identity application cookie with `credentials: "include"`. The API does not issue
or accept a custom JWT.

Known `/api/v1` endpoints return Problem Details with 401 or 403 instead of redirecting
to Identity HTML pages. Existing MVC routes retain their login/access-denied redirects.

The existing global `AutoValidateAntiforgeryToken` filter protects unsafe methods
(`POST`, `PUT`, `PATCH`, and `DELETE`). A browser client must obtain a server-generated
request token from a future authenticated host bootstrap/token endpoint and send it in
the `X-CSRF-TOKEN` header while also including the Identity cookie. The antiforgery
cookie alone is insufficient. P03-01 configures and tests this header contract but does
not add a token endpoint or weaken validation for API actions.

OpenAPI describes the Identity application cookie as the `IdentityCookie` cookie
security scheme and marks operations carrying authorization metadata. Authorization
and ownership decisions remain server-side and resource-aware.

## Property resource

The property resource is available at `/api/v1/properties`:

| Method and route | Access | Success |
|---|---|---|
| `GET /api/v1/properties` | Public | `200` paged property summaries |
| `GET /api/v1/properties/{propertyId}` | Public | `200` property detail |
| `POST /api/v1/properties` | `Landlord`, `Admin` | `201` property detail and `Location` |
| `PUT /api/v1/properties/{propertyId}` | Owning `Landlord`, `Admin` | `204` |
| `DELETE /api/v1/properties/{propertyId}` | Owning `Landlord`, `Admin` | `204` |

The list accepts these query parameters:

- `status`: optional, currently only `published` (the default) is valid.
- `page` and `pageSize`: the shared bounds and defaults described above.
- `search`: optional, at most 200 characters, matched against title and address.
- `minimumRent` and `maximumRent`: optional inclusive decimal bounds; both must be
  non-negative and the minimum cannot exceed the maximum.

Results are ordered deterministically by title, address, and property ID before paging.
The query performs a no-tracking count and projection and does not load landlord,
tenancy, or maintenance entities.

Create and update JSON accept exactly these writable values:

```json
{
  "title": "City apartment",
  "address": "1 Main Street",
  "rentAmount": 1250.00,
  "description": "Optional public description"
}
```

Owner/`landlordId`, IDs, roles, timestamps, publication state, concurrency state,
tenancies, and maintenance data are not mutation-contract fields. The owner is always
the authenticated creator; updates preserve the persisted owner. Public list/detail
responses contain only `id`, `title`, `address`, `rentAmount`, and `description`.

An authenticated landlord receives a concealed `404 resource_not_found` when trying
to update or delete another landlord's property. Admin precedence is explicit for a
multi-role caller. Missing resources use the same 404 contract. Validation returns
`400 validation_failed`, persistence/history conflicts return `409 resource_conflict`,
and successful update/delete commands return no response body.

DELETE preserves the current MVC lifecycle rule: an unused property is hard-deleted,
while a property with tenancy history is retained and produces `409`. The current EF
model has no separate unpublished/archive flag, so every persisted property is a
published listing today and public reads use a dedicated published-only service/store
path. Persistent archive/unpublish state and opaque rowversion concurrency are deferred
to Phase 7; no concurrency value is accepted or returned by this API version yet.

POST, PUT, and DELETE use the existing Identity cookie and require the
`X-CSRF-TOKEN` antiforgery header. OpenAPI declares their cookie security requirement,
request/response schemas, query filters, and documented status responses.

## Tenancy lifecycle (P05-01)

All routes below require an Identity application cookie. Every POST requires the
matching `X-CSRF-TOKEN`, as do existing API writes. Responses use `Cache-Control:
no-store`. All input types are dedicated DTOs; actors, ownership, statuses and UTC
timestamps are server-derived. Unrecognized JSON properties cannot override them.

| Method/path | Capability | Success |
| --- | --- | --- |
| GET `/api/v1/tenancies?page=1&pageSize=20` | Admin, owning Landlord, currently active Tenant | 200 paged `TenancyResponse` |
| GET `/api/v1/tenancies/{id}` | Same resource scope | 200 `TenancyResponse` |
| POST `/api/v1/tenancies` | Admin or owning Landlord | 202 `InvitationResponse` |
| POST `/api/v1/tenancies/accept` | Invited Tenant role account | 204 |
| POST `/api/v1/tenancies/{id}/end` | Admin or owning Landlord | 204 |

Invite JSON: `propertyId` (positive integer), `tenantIdentifier` (exact login
identifier, maximum 256 characters), `startDate` and optional `endDate` (ISO calendar
dates). End must not precede start or today's UTC date. Identity's normalizer is
used with exact equality against unique NormalizedUserName; no substring search,
email directory, or global account listing exists. The identifier is resolved once
at invitation time, and Tenant role is rechecked against SQL on acceptance.

`InvitationResponse` contains **only** `token` (64 uppercase hexadecimal characters,
256 random bits) and `expiresUtc` (seven days after issue). This is the only response
that returns the raw token, for private manual delivery. A receipt has identical
shape/status for known, unknown and non-Tenant accounts. Each reserves the property;
the latter two cannot be accepted. There is deliberately no invitation ID/target in
the receipt. Token loss requires cancelling the reservation through the manager
list and issuing another. There is no recovery or delivery provider.

Acceptance JSON: `token` only. It must match both the hash and the persisted invited
tenant. A wrong tenant, invalid token, removed Tenant role, or consumed token returns
concealed 404. A correctly matched but expired invitation, or a tenancy whose end
date has passed, returns 409. Expiry is exclusive: equality with expiresUtc is expired.
No token appears in URLs. Malformed DTOs return validation 400; tokens are never
echoed into errors. Successful acceptance clears the hash and records actor/time.
Admin cannot consent for another tenant, including a multi-role Admin.

`TenancyResponse` contains `id`, `propertyTitle`, `propertyAddress`, `startDate`,
`endDate`, and string `status` (`Pending`, `Active`, `Ended`). No account identity,
token/hash, audit IDs, or rowversion is exposed. Lists use standard paging limits
(1–100 items; maximum page 10,000) and deterministic newest-ID ordering. Tenant
queries include only their currently active, date-eligible tenancies. Manager
queries include owned history; Admin explicitly overrides ownership. Inaccessible
and missing IDs both return 404. An authenticated account without a supported role receives 403; anonymous receives 401.

Only one Pending or Active row can reserve a property. An expired Pending row is
retained as Ended with reason Expired and a system time (no invented human actor)
when a new invitation replaces it. Ending Pending means cancellation; ending Active
records the UTC calendar end date, bounded by a future start date. Repeated ending
returns 409 and leaves the original audit untouched. All mutations lock the property
inside a serializable transaction, re-read state, and use rowversion/unique-index
protection. Concurrent commands are ordered by this lock: a stale or losing command
returns 404/409 as appropriate. There is no client If-Match/version contract because
commands have immutable targets, explicit state preconditions and no editable fields.
A sequential accept followed by end can legitimately both succeed.

Failures use existing Problem Details codes (`validation_failed`,
`authentication_required`, `access_denied`, `resource_not_found`, `resource_conflict`)
and trace IDs. OpenAPI exposes token fields intentionally on issuance/acceptance
DTOs only, without token examples or persistence entities.
