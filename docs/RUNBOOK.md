# Local operational runbook

## Scope

This is a local reliability foundation, not a hosted-service SLA or a completed
production operations setup. Azure, image publication, telemetry collectors,
dashboards and automated alerts remain deferred. No account or paid resource is
needed. Pair this with [observability](OBSERVABILITY.md), [local Compose](LOCAL_COMPOSE.md)
and [release boundaries](RELEASES.md).

## Health contract

| Endpoint | Success | Failure | Meaning |
| --- | --- | --- | --- |
| `GET /health/live` | 200 `Healthy` | No response if the process cannot serve HTTP | No dependencies; does not validate a session cookie |
| `GET /health/ready` | 200 `Healthy` | 503 `Unhealthy` | Can connect to the configured application database and run `SELECT 1` |

Both endpoints allow anonymous GET/HEAD only, use plain text and `Cache-Control:
no-store`; HEAD has no body. They never expose a dependency list, hostname,
credentials, SQL, exception, stack or configuration dump. Query parameters do not
enable extra details. They do not create a database, apply migrations or write
application records. A healthy SQL probe does **not** prove the schema is current,
all application tables/permissions work, data is correct or writes will succeed.

Readiness opens a separate, unpooled SQL connection to the same configured target:
two-second connect/command timeouts, no connection retry, and a three-second linked
cancellation budget including caller cancellation. The health framework also
sets a three-second timeout. These are cancellation budgets, not hard real-time
guarantees; driver/OS cleanup and scheduling can add latency. No EF execution
strategy or automatic replay of application writes is added. Probe SQL is not
reported by EF interceptors; the HTTP request status/timing remains observable.

At most two readiness requests execute per application process; excess requests
return the same safe 503 immediately, with no queued SQL work. A 503 can therefore
mean dependency failure **or probe saturation**. This bounds simultaneous work,
not total probe frequency or network attacks. Restrict management endpoints at a
future ingress/firewall; don't use an untrusted Host header as the only boundary.
Use cookie-free probes: readiness traverses normal authentication, and a supplied
session cookie may trigger its existing SQL validation before the probe limiter.
Liveness intentionally short-circuits after routing, before authentication; outer
security headers and safe request diagnostics still apply.

Geocoding is optional and never part of readiness. Provider outages cannot make
these checks unhealthy. Enabled-provider configuration still validates at startup;
readiness does not bypass invalid application configuration. Startup also retains
the existing SQL role seed: if SQL is down **before startup**, the host may exit
before either endpoint exists. The outage/recovery guarantee applies to an already
started host; startup retry/seed restructuring is not implemented here.

## Probe use and container boundaries

For the default local Compose URL (trust the local certificate explicitly):

```bash
curl --fail --max-time 5 --cacert .local/compose/rentright-local/certs/localhost.pem https://localhost:7443/health/live
curl --fail --max-time 5 --cacert .local/compose/rentright-local/certs/localhost.pem https://localhost:7443/health/ready
```

Use the actual printed port/private certificate location if customized. Do not
use `--insecure`, send authentication cookies or copy resolved configuration into
an issue. The image HTTP smoke and Compose HTTPS lifecycle smoke assert both
endpoints. The Compose ongoing healthcheck is **still a TLS socket check**, not
HTTP or SQL readiness. Docker does not itself restart an unhealthy container.
No ongoing HTTP probe or Azure probe has been configured by this change. When
wiring a future orchestrator, use liveness for restart decisions, readiness for
traffic admission, an interval of at least 10 seconds, a client timeout longer
than the SQL budget (for example five seconds), and multiple failures before
eviction. Validate thresholds against the actual topology before deploying them.

## Failure actions

These are manual checks for the developer/operator, not installed alert rules.
Do not restart continuously during an outage, increase timeouts without evidence,
or turn on sensitive-data logging to obtain more detail.

| Signal | Checks and action |
| --- | --- |
| Live 200, ready 503 | Confirm probes are cookie-free and not excessive; inspect safe HTTP 503 timing and container status. Check SQL service/network availability and private configuration locally. Restore the dependency, then verify ready returns 200 and an application read works. Do not run migrations as an availability fix. |
| Neither probe responds | Check process/container state, startup configuration, SQL role-seed availability, TLS and the published loopback port. Correct the cause before starting the same version again. A missing response is not proof of a SQL-only failure. |
| Optional map provider unavailable | Consult the existing bounded timeout, daily limit, cache and cooldown behavior in [GEOCODING.md](GEOCODING.md). Keep maps disabled if no key is configured; core property/maintenance workflows stay available. Never retry a user write to recover a map lookup. |
| Elevated application errors | Correlate event 13002 with 13001 by trace ID and route template. Event 13003 identifies safe EF operation failures. Do not request passwords, bodies, SQL or connection strings. Reproduce with synthetic data and the same version. |
| Initialization/migration failure | Stop the rollout/job, retain the previous application and private backup, and identify the first failing migration. Never edit an applied migration, delete history, automatically reset a database or blindly repeat a non-idempotent command. Local initialization is explicit and Development-only; shared/production changes need a separately reviewed migration plan. |
| Suspected credential exposure | Restrict access, rotate the affected credential using the owning service's private controls, update configuration outside Git, restart only affected services and recheck readiness plus authenticated behavior. Revoke sessions as appropriate. Rotation and incident containment are manual; do not paste old/new values into logs or a PR. |
| Regression after a release | Stop promotion and retain evidence. If compatible with the current schema, restore the previously verified immutable image and smoke-test health, login, CSRF and role workflows. Destructive/down migrations are not an automatic rollback. Live publication/deployment and a rehearsed restore remain deferred. |

## Verification and changed-file inventory

- `Health/SqlReadinessHealthCheck.cs`: bounded, read-only SQL connectivity check;
  safe results do not retain exception payloads.
- `Health/HealthEndpoints.cs`: registration, anonymous GET/HEAD contracts,
  liveness short-circuit, readiness concurrency cap and minimal status writer.
- `Program.cs`: register/map these endpoints; business routes and schema unchanged.
- `RentRight.Tests/Health/HealthEndpointTests.cs`: HTTP contracts, timeout,
  cancellation, safe configuration failures, method restrictions and overload.
- `RentRight.Tests/SqlIntegration/HealthSqlTests.cs`: real SQL success and offline/
  online recovery, persisted-cookie liveness and unavailable optional-provider isolation.
- `scripts/verify-container.sh` and `scripts/verify-compose.mjs`: existing disposable
  image/HTTPS smoke checks now assert both endpoints.
- `README.md`, `docs/LOCAL_COMPOSE.md`, `docs/OBSERVABILITY.md`, `docs/TASKS.md` and this file: current contract,
  operating actions and explicit incomplete deployment/reliability gates.

SQL tests generate and validate a unique `RentRightTests_<random>` database name
before taking **that test-owned database only** offline, always bring it online in
`finally`, and remove it through the existing fixture cleanup. They do not accept
a configured developer/shared database. No production outage or load test is run.
