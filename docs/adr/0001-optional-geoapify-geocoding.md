# ADR 0001 — Optional Geoapify address enrichment

Date: 2026-10-18. Status: accepted for implementation; live activation requires
operator credentials/privacy review. User selected optional Geoapify maps, left off
until configured. No account, live lookup or production database change was made.

## Decision

Use one server-side typed HTTP adapter for Geoapify forward geocoding. Keep Identity,
SQL Server and existing property/API contracts. An explicit owner/Admin retry uses
the saved address, never a posted address or arbitrary URL. Enrich after successful
create/address edits; unavailable optional enrichment cannot undo the core save.

Provider facts checked against primary sources on the date above:

- The free plan lists 3000 credits/day, up to five requests/second and no card
  requirement; a basic geocode uses one credit. Attribution is required. These are
  provider limits, not a guarantee against future pricing/terms changes.
  [Pricing](https://www.geoapify.com/pricing/)
- Geoapify permits storing results with source attribution. The implementation
  accepts only OpenStreetMap-backed address-level matches and renders Geoapify/OSM
  attribution alongside reused coordinates.
  [Geocoding FAQ](https://www.geoapify.com/geocoding-api/)
- The provider describes EU data centers and API-request retention/technical
  metadata, with CDN/subprocessors also involved. This is not a conclusion that
  RentRight or all processing is automatically GDPR-compliant. Review the actual
  deployment, terms, DPA needs and address handling before enabling real data.
  [Privacy policy](https://www.geoapify.com/privacy-policy/),
  [terms](https://www.geoapify.com/terms-and-conditions/)
- Requests use the fixed HTTPS search endpoint with escaped text, JSON format,
  one result and country bias disabled. Only bounded coordinates/reference are
  retained, never the response address/profile payload.
  [Protocol](https://apidocs.geoapify.com/docs/geocoding/)
- Pre-merge review aligned reference storage and both validation boundaries to
  2048 characters. The provider documents place IDs returned by its APIs as
  accepted through that length. A separate widening migration preserves the
  original migration and existing values.
  [Place-ID range](https://apidocs.geoapify.com/docs/places/index.html#structured-filters)

## Alternatives and tradeoffs

The public Nominatim endpoint requires a strict shared-resource usage policy and
discourages personal/confidential submissions. We do not use it as an implicit
production dependency. [Policy](https://operations.osmfoundation.org/policies/nominatim/)
Self-hosting would add operational work outside this portfolio phase. A browser map
SDK/tile service would add credentials, request volume and automatic third-party
browser traffic. Instead, an approximate location card opens OpenStreetMap only
when the user follows the map link; no iframe, tiles or new client library is added.

Keep provider access disabled by default. An in-process 256-entry hash-keyed cache,
single active lookup, one-request/second pace and default 2500-attempt UTC daily
budget bound normal use. Retries consume budget too. These controls reset on
restart, are not shared across replicas, and do not guarantee billing limits or
match the provider's accounting window. Use a separate project/key and monitor
provider quotas; distributed quota controls belong to a scaled deployment.

No new resilience/cache package, generic repository, background job or architecture
layer is needed. A bounded two-attempt lookup (default three-second total budget),
shared Retry-After cooldown, validated response and failure result are sufficient
for this optional enhancement. Long provider cooldowns are not shortened.

## Consequences

The schema migration is required even with lookup disabled. Existing rows remain
address-only; there is no backfill. Editing an address atomically clears old
enrichment, including case/trailing-space edits. A separate rowversion/owner/address
conditional write prevents late responses from overwriting newer property changes.
Location refresh advances the property version, so open edit/delete forms may need
reloading. A successful core save can exist without a point or after an aborted
request; automatic mutation retries remain forbidden.

The public REST DTOs, tenant React portal and role rules remain unchanged. Rendering
and anonymous reads never geocode. The UI labels points as estimates, not verified
boundaries. The HTTP adapter deliberately declines low-confidence, coarse or
unsupported-source matches rather than inventing a location. Live coverage/accuracy
and hosting-region obligations remain operational checks, not fake-test claims.
