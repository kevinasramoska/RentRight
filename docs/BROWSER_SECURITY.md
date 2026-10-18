# P08-02 browser-security checkpoint

External sign-in is **deferred by user choice**. Email/password Identity login,
local role assignment, recent-password verification and usable-local-admin rules
remain unchanged. No Google/Microsoft/Auth0 registration, credentials, OIDC package,
external account linking or callback claims are introduced. P08-02 is therefore
**partially delivered**, not complete against its original OIDC acceptance criteria.
There are no schema, migration, package or rental-domain changes.

## Same-origin browser boundary

The published React app uses the same origin as MVC and `/api/v1`. No CORS service,
wildcard origin or credentialed cross-origin policy is installed. A preflight from
an unrelated origin gets no CORS permission. CORS is not write authorization:
server resource policies and antiforgery still protect requests, including simple
cross-origin requests that browsers can send without a preflight.

The existing global MVC antiforgery filter and Razor Pages antiforgery protection
stay enabled. The antiforgery cookie is now explicitly Secure Always, HttpOnly and
SameSite Strict; the application cookie remains Secure/HttpOnly/SameSite Lax.
Use HTTPS for the local app and Vite proxy. Reads do not require a request token.
Before each unsafe API request, React fetches `/api/v1/session/antiforgery` with
same-origin credentials and no-store caching and submits the returned token in
`X-CSRF-TOKEN`. It is a CSRF request token, not a browser-stored login token.
Anonymous or another user's token cannot authorize the current user's write.
Refresh it after a login/account switch; neither cookie alone suffices for writes.

The client rejects external URLs, traversal/normalized paths, encoded path
separators, fragments, backslashes, line breaks and tabs before either token
bootstrap or fetch. Credentials and tokens stay on `/api/v1/` paths. Redirects are
errors, and writes are never automatically retried. No auth/CSRF token is stored in
localStorage or sessionStorage. Existing local-return-URL validation remains in
the account pages; no external redirect/login flow is added.

## Baseline response headers

Middleware runs before HTTPS redirection/static files and registers `OnStarting`,
covering MVC, Identity, JSON, React/static files, redirects, handled errors and 429s:

| Header | Value |
| --- | --- |
| Content-Security-Policy | `base-uri 'self'; object-src 'none'; frame-ancestors 'none'; form-action 'self'` |
| X-Content-Type-Options | `nosniff` |
| X-Frame-Options | `DENY` |
| Referrer-Policy | `strict-origin-when-cross-origin` |
| Permissions-Policy | `camera=(), microphone=(), geolocation=()` |

This CSP restricts base URLs, plugins, framing and form destinations. It is **not a
full script/style/resource CSP** and does not claim to prevent all XSS. The existing
Razor import map/framework Identity scripts and CDN icon styles are not broken by
silently imposing a strict `script-src`/`default-src`. A stricter policy needs an
explicit asset/nonce audit and browser testing. Existing non-Development HSTS and
HTTPS redirects remain unchanged. Server-side output encoding remains essential.

## Rate limits

The built-in ASP.NET Core limiter runs after routing/authentication and before
authorization/actions. There is no queue. Fixed-window defaults per app process:

| Requests | Shared budget | Default |
| --- | --- | --- |
| Unsafe methods under `/Identity/Account` (including login, registration, MFA, profile, password verification and logout) | Remote connection IP, across all account paths | 20/minute |
| Other unsafe MVC/API methods | Authenticated local user ID; anonymous falls back to connection IP | 60/minute |
| GET, HEAD, OPTIONS | Not limited by this policy | Normal navigation/CSRF bootstrap remain available |

Invalid/denied writes consume permits too. Changing query strings, account paths,
submitted email or `X-Forwarded-For` does not reset the account IP budget. An absent
connection address shares an `unknown` budget rather than disabling protection.
Authenticated users have independent mutation budgets, separate from account POSTs.

Rejection returns `429`, a positive `Retry-After` delta in seconds and `Cache-Control:
no-store`. APIs receive Problem Details with `code: rate_limited` and a trace ID;
MVC/Identity receive a fixed plain-text wait message. React displays a fixed safe
wait message, preserves submission text and does not automatically replay writes.
Wait as directed, then explicitly retry; normal reads are still available.

Override through configuration or environment variables:

```text
Security__RateLimits__AuthenticationPermitLimit=20
Security__RateLimits__MutationPermitLimit=60
Security__RateLimits__WindowSeconds=60
```

Permit counts must be 1–10000 and windows 1–3600 seconds. Invalid values fail startup,
not silently disable protection. These are starting limits, not production load
measurements. Shared-NAT users share an account budget. Before deployment, tune
against representative traffic and configure only known/trusted proxies to supply
the real connection IP/scheme. The app does not trust arbitrary forwarding headers.
Limits are in-memory, per process and reset on restart; replicas multiply budgets.
They run after cookie validation and are not a substitute for edge/DDoS controls,
distributed limits or the existing per-account password lockout.

## Persistent data-protection keys

The opt-in configuration supports an externally provisioned persistent filesystem
volume and a separately mounted RSA PFX certificate. It does not provision a host,
volume, certificate, Azure account, Blob Storage or Key Vault. With no settings,
platform defaults remain; persistence/encryption of those defaults is **not** claimed
to be configured or production-ready by this checkpoint.

Host/secret configuration (example paths only; never place the password in source):

```text
Security__DataProtection__KeyRingPath=/var/lib/rentright/keys
Security__DataProtection__ApplicationName=RentRight-demo
Security__DataProtection__CertificatePath=/run/secrets/rentright-key-encryption.pfx
```

Set `Security:DataProtection:CertificatePassword` with user secrets locally or the
host's secret settings when the PFX requires one. The existing key directory and
certificate path must be absolute and outside the app content root/checkout. Paths
are operator-controlled; do not use symlinks into source/static directories. Restrict
directory access to the app identity, keep the certificate mount read-only, and
verify volume ownership permits writing keys as the eventual non-root container
user. Do not share keys/certificates between unrelated apps or environments.

An explicit ApplicationName is required and must stay stable across restarts and
replicas. Staging/production explicit key stores require a currently valid RSA
certificate of at least 2048 bits with its private key. Development/Testing may
explicitly use a directory without certificate encryption, never a production
recommendation. Partial/invalid paths, missing private keys, non-RSA/weak/expired/
future certificates and bad passwords fail closed. Password errors are not echoed.
Linux/Windows use memory-only certificate imports; macOS requires its framework
temporary-keychain import mode. No certificate is installed in a permanent store.

The framework handles key generation/rotation; encrypted keys and the wrapping
certificate/private key must both be backed up securely. Losing either prevents
cookie/antiforgery decryption. Switching from the default discriminator/key ring to
this explicit configuration requires a one-time sign-in again. Subsequent restarts
need the same ring, application name and decrypting certificate. Never delete old
keys or replace the wrapping certificate blindly. Multi-certificate wrapping-key
rotation and managed Azure key storage remain a Phase 12 deployment task; do not
rotate the certificate with this single-certificate configuration until that path
is implemented and recovery is verified. Provision/monitor expiry before rollout.

## Verification

New HTTP tests exercise headers across real endpoints, denial paths, account/IP and
user/anonymous rate partitions, all unsafe verbs, spoofed forwarding headers, safe
navigation, 429 contracts, invalid limit configuration, foreign-origin preflights,
CSRF account changes and secure cookie attributes. Data-protection tests use fresh
temporary directories and generated test-only certificates, assert encrypted XML,
recover data through a fresh provider, isolate app discriminators and reject unsafe
configuration/certificates. Test-owned directories are removed on disposal.

| Check | Result |
| --- | --- |
| Release solution build | Passed, 0 warnings/errors. |
| Fast backend suite | 499 passed, 0 failed/skipped, including 40 new security cases and updated OpenAPI assertions. |
| Node 24 frontend | Lint, type check/build and all 53 tests passed. |
| Focused real SQL/HTTP regression | 68 passed, 0 failed/skipped, 6m41s: existing `AccountSecuritySqlTests` and `SqlPipelineTests`. Real login, CSRF, account/MFA/session/role guards and property authorization/write pipeline. |
| EF pending-model check | No model changes since the last migration. |
| Scope/whitespace review | 19 changed files, matching the inventory below; no schema, packages or generated assets committed. |

The SQL run is targeted, not a rerun of every SQL test. It uses disposable
Testcontainers databases only, never the developer/shared database. Ignored TRX
evidence is in `artifacts/test-results/p08-02/` (`p08-02-reviewed-fast.trx` and
`p08-02-security-sql.trx`). Existing unrelated Docker containers are not modified.

The first certificate tests exposed macOS's unsupported memory-only import flag;
the platform-specific loading fix passed the same assertions plus the full fast
suite. Tests were not weakened to accommodate it. There is no live deployment or
real-browser CSP audit claim, and no OIDC acceptance claim.

## Files changed

| File | Purpose |
| --- | --- |
| `Security/BrowserSecurity.cs` | Antiforgery-cookie flags and baseline headers. |
| `Security/BrowserRateLimiting.cs` | Validated configurable partitions and safe 429 responses. |
| `Security/PersistentDataProtection.cs` | Explicit external persistent ring and certificate encryption/validation. |
| `Program.cs` | Register security and place headers/limiter in the request pipeline. |
| `Api/ApiConventions.cs` | Stable `rate_limited` error code. |
| `Api/ApiServiceCollectionExtensions.cs` | Describe 429/wait behavior on unsafe OpenAPI operations. |
| `ClientApp/src/api/client.ts` | Reject normalized/ambiguous API paths before fetching. |
| `ClientApp/src/Portal.tsx` | Safe, explicit rate-limit feedback. |
| `ClientApp/src/api/client.test.ts` | Path-denial and no-retry 429 regression tests. |
| `ClientApp/src/App.test.tsx` | User-facing 429 handling without raw server details. |
| `RentRight.Tests/Api/BrowserSecurityTests.cs` | Actual HTTP boundary/header/rate-limit/CSRF coverage. |
| `RentRight.Tests/Api/ApiConventionsTests.cs` | Verify unsafe OpenAPI operations advertise 429 without changing GET contracts. |
| `RentRight.Tests/Authorization/PersistentDataProtectionTests.cs` | Encrypted persistence/restart/isolation and configuration/certificate tests. |
| `README.md` | Current partial checkpoint and configuration/report link. |
| `docs/API.md` | Browser protocol, cookie and 429 contract. |
| `docs/ARCHITECTURE.md` | Implemented browser-security boundary versus deferred OIDC. |
| `docs/DEPLOYMENT.md` | Key-volume/certificate rollout and proxy gates. |
| `docs/TASKS.md` | Honest partial P08-02 status and external-login deferral. |
| `docs/BROWSER_SECURITY.md` | Design, operational limits, verification and file inventory. |

## Primary references

- [ASP.NET Core rate limiting](https://learn.microsoft.com/en-us/aspnet/core/performance/rate-limit?view=aspnetcore-10.0)
- [Data-protection configuration and certificate rotation](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0)
- [.NET cross-platform certificate behavior](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography)
- [OWASP response-header guidance](https://cheatsheetseries.owasp.org/cheatsheets/HTTP_Headers_Cheat_Sheet.html)
