# RentRight API conventions

## Current scope

Phase 3 starts with a versioned transport contract under `/api/v1`. P03-01 establishes
the conventions described here; it does not expose a property, tenancy, maintenance,
health, or test resource. The first resource API remains P03-02.

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
