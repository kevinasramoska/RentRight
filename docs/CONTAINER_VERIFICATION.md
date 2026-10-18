# P10-01 — application image

## Scope

Package the existing React → same-origin API → SQL Server application as one
deployable image. No routes, authorization rules, schema, packages or application
services change. Compose, hosting, TLS/proxy configuration, deployment credentials,
OIDC, health endpoints and CI expansion remain separate tasks.

Three digest-pinned stages separate Node 24 frontend compilation, repository SDK
Release publication and the non-root ASP.NET runtime. Dependency manifests are
copied before sources so dependency layers are reusable. Local frontend output
is excluded from the context: Docker must build its own assets. The explicit
prebuilt-assets publish switch retains normal local publish behavior and fails
when no React entry file exists.

Base image references were verified against upstream registry manifests on
2026-10-18. The Node stage resolves to 24.22.0; the SDK remains 10.0.302, matching
`global.json`; the runtime is 10.0.13. Runtime patch selection follows
[Microsoft's October release](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.13/10.0.13.md).
Review/rebuild pinned images periodically; immutable pins alone do not prevent
future advisories. The earlier SDK is build-only, not a runtime security claim.

## Repeatable smoke

From the repository root:

```bash
docker build --build-arg VERSION=local --build-arg REVISION="$(git rev-parse HEAD)" -t rentright:p10-01 .
bash scripts/verify-container.sh rentright:p10-01
```

The smoke requires Docker Linux containers, .NET SDK/EF CLI matching the repo,
curl and OpenSSL. Do not run another .NET build in the same checkout concurrently.
It owns a randomly named SQL Server container and network, no persistent volume
and a random loopback-only SQL port for the host EF CLI. It applies the repository
EF migrations only there and seeds a fictional public listing with no password/usable account.
The app runs in Production, UID 1654, a read-only root filesystem, no Linux
capabilities, and `no-new-privileges`. Only temporary directories are writable;
its random HTTP host port binds to loopback. Cleanup removes only these generated
resources on success/failure/interruption; images remain available for reuse.
The SQL runner enables quoted identifiers for filtered indexes. Both generated
raw script variants exposed invalid-column compilation errors in historical DDL
and backfill batches. The smoke uses EF's migrator, as the existing SQL tests do,
only against its own newly created database. Raw scripts are not a verified
deployment route. This limitation is recorded in the deployment runbook; applied
migrations are unchanged.

Checks cover the following boundaries:

- Image: non-root default, only exposed port 8080, published React/.NET output,
  no SDK/Node/npm/Git, source-control directory, frontend source/dependencies,
  test project, development configuration or certificate/key files under `/app`.
- HTTP: React fallback and real JS asset, explicit legacy MVC listing, root React
  redirect, SQL-backed public API listing and anonymous session contract.
- Isolation: manager API requires authentication; missing API and static-file
  paths return 404 rather than the SPA shell.

HTTP smoke is not an authenticated browser test. Identity cookies/antiforgery
remain Secure and require HTTPS for real account operations. The tmpfs key ring
is intentionally ephemeral for this disposable smoke, not a production storage
recommendation. No production database, provider key or real address is used.

On 2026-10-18 the Linux amd64 image built successfully, including a clean npm
install/type-check/Vite build inside Docker. Rebuilding reused dependency and
frontend layers. Runtime inspection confirmed .NET/ASP.NET 10.0.13, UID 1654 and
only port 8080. The SQL/HTTP/read-only checks above passed. The default local
publish path also produced React assets; explicitly prebuilt publication without
the entry file failed with the intended error. The final Release solution build
had zero warnings/errors, all **583 backend tests** passed and EF reported no
pending model changes. Frontend lint/build and all **85 frontend tests** passed
during the preceding PR review; no React source changed in P10-01. ARM64 image
execution, authenticated HTTPS browser workflows and public deployment are not
claimed by this smoke.

A background browser check opened the actual container's public catalog and
followed its property link. React rendered the fictional SQL listing, address,
rent and description through the real workspace detail API, without intercepted
responses. A local screenshot is retained under ignored `artifacts/`; the test
tab was closed. This establishes the public UI → API → SQL → UI path, not a login
or authenticated-write claim. Set `RENTRIGHT_SMOKE_PAUSE=1` to keep the disposable
preview available for 60 seconds for the same optional check.

## Vulnerability scan

Docker Scout requires a Docker login on this machine, so it was not used to
claim a clean result. An informational Trivy scan of an exported local image
does not require a registry account or Docker socket access. Example with the
upstream manifest verified on 2026-10-18:

```bash
docker save --output artifacts/rentright-image.tar rentright:p10-01
docker run --rm --mount "type=bind,src=$PWD/artifacts,dst=/scan,readonly" \
  aquasec/trivy:0.76.0@sha256:2416d1f74045b57274df1a340eee6b510cacd73c66a6ccb8da092b117eb7d222 \
  image --input /scan/rentright-image.tar --scanners vuln --severity HIGH,CRITICAL --no-progress
```

The initial .NET 10.0.10 runtime scan reported high-severity base/runtime findings;
the runtime was moved to 10.0.13 before final verification. Scan results are
point-in-time evidence, not a promise that the image is vulnerability-free.
The patched image scan on 2026-10-18 reported **zero HIGH/CRITICAL findings**
across Ubuntu, application dependencies and both .NET shared-framework manifests.
This filtered scan does not claim absence of lower-severity findings.

## Files changed

| File | Reason |
| --- | --- |
| `Dockerfile` | Separate cached frontend/backend builds, immutable bases, patched runtime, non-root port and OCI version/revision metadata. |
| `.dockerignore` | Exclude local secrets, certificates, databases, dependencies, generated assets, tests, scripts and build/IDE output. |
| `RentRight.csproj` | Preserve default frontend publication; allow explicitly prebuilt Docker assets and fail on a missing entry file. |
| `scripts/verify-container.sh` | Repeatable isolated SQL/image/HTTP/read-only smoke and narrowly scoped cleanup. |
| `README.md` | Current packaging status and runbook entry. |
| `docs/DEPLOYMENT.md` | Image/configuration/update policy, isolated smoke, read-only/key-ring and deployment boundaries. |
| `docs/TASKS.md` | P10-01 implementation status and verification scope. |
| `docs/CONTAINER_VERIFICATION.md` | Evidence, reproduction, limitations and complete file inventory. |
