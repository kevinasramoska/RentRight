# Local request diagnostics — P13-01 foundation

RentRight writes structured UTC JSON to the existing console output. Request
summaries, safe unexpected-failure events, SQL timings and optional geocoding
spans share W3C trace context. No telemetry account, package, remote exporter,
database table, public diagnostics endpoint or paid resource is added.

This is the local diagnostics foundation, not completion of every Phase 13 goal.
Workflow-specific audit/metric expansion, a selected collector, dashboards,
automated alerts and retention/access controls for hosted telemetry remain separate
work. The incremental P13-02 [runbook](RUNBOOK.md) now covers liveness/readiness and
manual recovery actions; it does not complete deployment probes or load testing.
SQL status history is still the audit record; logs are not a substitute for it.

## Events and correlation

| Event ID | Name | Fields/purpose |
| --- | --- | --- |
| 13000 | DiagnosticsStarted | Enabled flag and built assembly informational version; no configuration values |
| 13001 | HttpRequestCompleted | Method, route template, status, elapsed milliseconds, outcome, trace ID and span ID |
| 13002 | UnexpectedRequestFailure | Method, original route template and trace ID; no exception object/message/stack |
| 13003 | DatabaseOperationFailed | Bounded operation name, elapsed milliseconds and trace ID; no provider payload |

Look for the request's `TraceId`, then find related failures and spans. The
existing API Problem Details `traceId` and MVC error-page request ID retain their
opaque correlation contract; a W3C request ID contains the same trace ID. Trace
IDs may originate in valid incoming trace context: they are not authentication,
proof of ownership or trusted user input. Arbitrary correlation headers are not
echoed. Invalid trace parents are ignored. No baggage/tracestate is copied into
RentRight event fields or metric dimensions.
The startup version is assembly metadata, not an assertion of a deployed commit;
use the release manifest and OCI digest for immutable image identification.

Normally ASP.NET owns the request activity. When no server activity exists,
RentRight creates a W3C fallback for the duration of that request, including in a
collector-free test host or when the ambient activity uses a legacy format. It is
disposed on success, failure or cancellation and restores the previous activity.
Diagnostics wrap the error handler, so an MVC error re-execution produces one
completion event for the original route, not another event for `/Home/Error`.
Only the error action skips antiforgery validation during re-execution: normal
login and write endpoints keep their existing antiforgery protection.

API failures still return safe JSON, including when an unsupported Accept header
prevents the default Problem Details writer from responding. Production MVC
failures keep the HTML error view. Local Development MVC exception pages retain
their existing debugging behavior; never expose Development publicly.

Client-request cancellation is classified as `cancelled` with diagnostic status
499, without writing a new response. A failure after response headers have been
sent is `aborted`, retains the actual status and is rethrown; a 200 status alone
therefore does not mean a streamed response completed. Unexpected failures are
recorded once and omit the exception payload even on this path.

## Configuration and sampling

The defaults are equivalent to this non-secret configuration fragment:

```json
{
  "Diagnostics": {
    "Enabled": true,
    "SuccessfulRequestLogSampleRate": 1.0
  }
}
```

Environment equivalents are `Diagnostics__Enabled` and
`Diagnostics__SuccessfulRequestLogSampleRate`. Sampling must be finite and between
0 and 1 or startup fails. At 0, normal 2xx/3xx summaries are omitted; at 1 they are
all written. Intermediate rates sample successful summaries only. HTTP 4xx/5xx,
cancellation and aborted outcomes are not sampled. Metrics count every observed
request while enabled, independently of log sampling.

`Enabled=false` stops RentRight request-summary logs, database timing logs,
custom metrics and dependency spans. Safe unexpected-failure logging and safe
error responses remain. It does not disable framework trace context or the
pre-existing geocoding lookup counter. No hidden fallback sends telemetry to a
third party. A collector's own trace sampling is separate from log sampling.

## Metrics and dependency spans

The DI-owned meter is `RentRight`. Its instruments are:

| Instrument | Unit | Dimensions |
| --- | --- | --- |
| rentright.http.requests | request count | method, route template, status, outcome |
| rentright.http.duration | seconds | same bounded HTTP dimensions |
| rentright.database.operations | operation count | SQL Server, operation, outcome |
| rentright.database.duration | seconds | same bounded database dimensions |

The ActivitySource is also `RentRight`. Spans are `database.connect`,
`database.reader`, `database.scalar`, `database.non_query` (or bounded `other`),
and `geocoding.lookup`. EF command/connection spans use its measured interval;
they do not hold commands/readers or modify AsyncLocal state across async EF
callbacks. They describe command/connection execution, not reader enumeration,
transactions, all SaveChanges processing or a production capacity claim.
Failures set an error status without recording an exception event/description.
Retries are separate dependency attempts, not extra logical HTTP requests.

Geocoding spans record only the bounded result enum. They do not record an
address, API key, normalized-address hash, coordinates, URI, response or headers.
Existing optional-provider timeout/retry/cache behavior remains unchanged.

No collector/exporter is installed by this change. The tests attach in-process
ActivityListener and MeterListener observers to verify actual spans/measurements.
If the optional official `dotnet-counters` tool is already available, replace
`APP_PID` with the running app's numeric process ID:

```sh
dotnet-counters monitor --process-id APP_PID --counters RentRight
```

Attach only to a process you own, keep diagnostic sockets private, and do not
open an unsecured diagnostic port to a network. Container attachment and a
hosted collection pipeline are not configured here.

## Privacy boundary and operating limits

- RentRight diagnostic fields allow-list code-owned route templates, normalized
  methods, bounded outcomes/statuses, timings and correlation IDs. Raw paths,
  route values, query strings, IPs, usernames/IDs, claims, form/JSON bodies,
  passwords, cookies, tokens, connection strings, SQL and exception payloads are
  excluded. Unknown paths become `unmatched`; custom methods become `OTHER`.
- JSON console scopes are disabled because arbitrary scopes may carry personal
  data. No custom logging provider, generic redaction-by-regex scheme, request
  buffering or sensitive-data logging is introduced.
- EF, ASP.NET diagnostics/hosting, Kestrel and HttpClient log categories capable
  of copying raw provider/URL/exception payloads are suppressed by default. Safe
  request/failure events and EF interceptors replace their application diagnostic
  role. This deliberately loses detailed native server/connection diagnostics.
  Investigate TLS/socket/process failures in a controlled local reproduction;
  do not enable broad production Debug/Trace logging as a quick fix.
- This is not an enforcement boundary against an operator adding other providers,
  EventSource listeners, body logging, more-specific filter overrides or wildcard
  trace collection. Review those changes separately. In particular, the native
  networking/SQL sources are not claimed to have this module's allow-list.
- No persistent log file, external retention/access policy, automatic alert or
  service-availability promise is created. Console/container log retention and
  rotation must be chosen before public hosting. Sample healthy requests and
  retain failure visibility according to the eventual operating policy.

Provisional signals for the repository maintainer to validate after establishing
a baseline: a sustained 5xx/aborted rise with meaningful request volume, elevated
request-duration percentiles, or repeated database failures. First correlate by
trace, then check dependency availability and recent schema/version changes.
Never automatically replay a failed write or treat an image rollback as a database
rollback. Thresholds, delivery destinations and alerts are not active or SLO claims.

## Verification and file inventory

Focused HTTP tests use the real middleware/Identity/API pipeline, synthetic
cookies where needed, and fake geocoding transport (no provider account/key or
Internet call). They assert structured fields, secret omission, trace propagation,
metrics, sampling, disabled behavior, cancellation, post-header failures, API
negotiation and original-route MVC GET/POST errors. SQL tests use the existing
owned disposable SQL Server fixture, real EF commands and forced sync/async SQL
failures; no configured developer/shared database is used.

Local verification on 2026-10-24: **622 fast tests** (including 26 new diagnostics
cases), **35 selected SQL tests** (three new diagnostics cases plus the existing
SQL pipeline matrix), CI-style Release build of all three projects with **zero
warnings/errors**, and **no pending EF model changes**. The SQL fixture cleaned up
its containers/databases. Docker Desktop was started for these checks and left
running; unrelated Docker projects were not stopped or adopted. The full SQL,
frontend and production-image/Compose gates remain part of PR CI, not a claim
that those complete suites were rerun locally in this scope.

```sh
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --filter FullyQualifiedName~RequestDiagnosticsTests
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --filter FullyQualifiedName~DiagnosticsSqlTests
```

| File | Change |
| --- | --- |
| Diagnostics/DiagnosticsOptions.cs | Explicit enable switch and successful-request log sampling |
| Diagnostics/DiagnosticEvents.cs | Stable event IDs/templates and safe original-route/method/trace projection |
| Diagnostics/RentalDiagnostics.cs | DI-owned meter, source, bounded measurements and safe dependency spans |
| Diagnostics/RequestDiagnosticsMiddleware.cs | One request summary, fallback W3C context, cancellation/abort semantics |
| Diagnostics/SafeExceptionHandler.cs | Safe correlated fault and existing API/MVC response boundary |
| Diagnostics/SafeDatabaseInterceptors.cs | Non-mutating sync/async EF command/connection observations |
| Diagnostics/DiagnosticsServiceCollectionExtensions.cs | Validation, UTC JSON, sensitive-category filters and startup event |
| Program.cs | Register diagnostics/interceptors and preserve the MVC error-handler path |
| Api/ApiServiceCollectionExtensions.cs | Suppress duplicate unsafe framework exception diagnostics |
| Controllers/HomeController.cs | Preserve a failed POST's 500 on error-view re-execution |
| Integrations/Geocoding/GeoapifyGeocodingClient.cs | Safe optional lookup span; no URI/payload tags |
| RentRight.Tests/Diagnostics/CapturedLogs.cs | Capture actual structured logger fields/exceptions for assertions |
| RentRight.Tests/Diagnostics/DiagnosticsProbeController.cs | Test-only API/MVC boundaries and fake-provider exercise |
| RentRight.Tests/Diagnostics/RequestDiagnosticsTests.cs | HTTP/privacy/metrics/tracing/configuration regressions |
| RentRight.Tests/SqlIntegration/DiagnosticsSqlTests.cs | Actual request-to-SQL correlation and redacted provider failures |
| README.md, docs/TASKS.md, docs/OBSERVABILITY.md | Setup, explicit completion limits and operating guidance |

Primary references: [ASP.NET Core error handling and .NET 10 suppression](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0),
[ActivitySource instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-instrumentation-walkthroughs),
[DI-owned metrics](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation),
[JSON console formatting](https://learn.microsoft.com/en-us/dotnet/core/extensions/logging/console-log-formatter),
[dotnet-counters](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-counters).
