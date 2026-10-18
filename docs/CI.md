# Pull-request quality gate (P11-01)

`Continuous Integration` runs on every pull request and push to `main`.
Three quality jobs run on Ubuntu 24.04; `Required quality gate` succeeds
only when every job succeeds. Failed, skipped and cancelled dependencies cannot
produce a green aggregate. Superseded runs for the same PR/ref are cancelled.

| Job | Blocking checks |
| --- | --- |
| React quality | Patch whitespace, pinned workflow syntax/context lint, release-authorization unit tests, `npm ci`, all-dependency high/critical audit, ESLint, TypeScript, all Vitest tests, production build |
| .NET and SQL quality | Locked restore and audit of all three projects, warnings-as-errors Release build, pinned EF model-drift check, fast tests with production-assembly coverage, complete SQL Server integration suite |
| Container quality | Locked production Docker build, high/critical vulnerability scan, real local-registry push/digest-pull/Production smoke/SBOM rehearsal, actual isolated HTTPS Compose lifecycle |

`RentRight.CI.slnx` includes the SQL project that the ordinary `RentRight.slnx`
intentionally excludes. The backend restores/builds all three projects once;
test runs use `--no-build --no-restore`. The frontend publishes its real React host
assets; the backend downloads that same-run artifact **before** building so the
HTTP/static-host tests exercise real assets, not fixtures or a missing frontend.
Frontend and container jobs run concurrently; backend follows frontend. Container builds run on a separate runner
and intentionally reproduce publication from source. Compose rebuilds reuse that
runner's Docker layer cache. No shared database or SQL credential is configured.
The SQL fixture and Compose use the same digest-pinned SQL Server image. Every
SQL case owns a fresh database; migrations apply through EF, not the known-broken
historical raw SQL bootstrap scripts. Docker absence fails; it never skips tests.

The existing opt-in `QueryPerformanceTests` measurement experiment remains skipped
unless explicitly enabled. Its deterministic SQL/query-count regression tests do
run. Wall-clock/plan collection is not a stable PR gate.

## Security and failure policy

- Every action is pinned to a full upstream commit. Refresh the SHA and adjacent
  version comment together after reviewing upstream changes. All actions use
  their supported Node 24 runtime. Node 24 and the
  repository's SDK pin are used. Application and scanner manifests are digest-pinned.
- Permissions are `contents: read`. Checkout does not persist credentials.
  There is no `pull_request_target`, registry publication, deployment or secret
  reference. Fork PRs run without repository secrets. Do not add real data or
  privileged credentials to these runners; install/build steps execute PR code.
- NuGet locks capture direct/transitive versions and content hashes. Restore audits
  all dependencies, reports low/moderate advisories, and blocks high/critical
  advisories (`NU1903`/`NU1904`). Audit-feed/restore failures are not suppressed.
  CI also treats compiler/MSBuild warnings as errors; no baseline warnings are hidden.
- `npm audit --audit-level=high` includes development dependencies. A failed audit
  request is also a failure. Low/moderate findings need review but do not block.
- Trivy 0.76.0 scans the exported application image for OS/library HIGH/CRITICAL
  vulnerabilities with `--exit-code 1`. Unfixed findings are not hidden. The scanner
  reads only a temporary image archive, without the Docker socket or registry
  credentials; the archive is removed on exit. Scanner/database-download errors
  fail the job. This is a point-in-time, severity-filtered check, not a claim of
  zero vulnerabilities or a scan of the development SQL Server image.
- For a finding: inspect the advisory and resolved dependency/runtime path, update
  the smallest relevant dependency or pinned base, and rerun the gates. Do not use
  `continue-on-error`, disable auditing, automatically force-fix dependencies, or
  silently add ignore rules. Any future exception needs a reviewed, bounded decision
  with advisory, reachability rationale, owner and expiry; no exceptions exist now.

Reports uploaded even after failure, when present, are frontend JUnit, backend
TRX/Cobertura, and the container vulnerability JSON. Retention is seven days.
The public React asset transfer is kept for one day; it is not a release artifact.
Release rehearsals upload only the explicit `release.json`, CycloneDX SBOM,
vulnerability JSON and checksum metadata (seven days), never private image archives.
PR/local manifests cannot publish. See [RELEASES.md](RELEASES.md) for the separately
approved private GHCR publisher, deployment contract and live-activation boundary.
Only explicit report patterns are uploaded, not environment files, certificate
exports, whole workspaces, database backups, core dumps or application logs.
Reports contain test names, source paths and synthetic test output; review them
before sharing publicly. Coverage is diagnostic, with the existing production-only
settings, not an arbitrary percentage threshold. Frontend coverage is not collected.

Formatting is deliberately incremental: patch whitespace is checked against the
PR base (or the previous main commit), and React uses its existing ESLint rules.
A trial full `dotnet format whitespace --verify-no-changes` found pre-existing
formatting violations. This task does not reformat unrelated code or pretend to
enforce whole-repository C# formatting; that needs a separately reviewed baseline.

## Local reproduction

Use Node 24, the pinned SDK, Docker/Linux amd64 support and enough memory for SQL.
These tests create only disposable SQL containers/databases and Compose volumes.

```bash
npm ci --prefix ClientApp
npm audit --prefix ClientApp --audit-level=high
npm run lint --prefix ClientApp
npm run type-check --prefix ClientApp
npm test --prefix ClientApp -- --reporter=default --reporter=junit --outputFile=../TestResults/frontend.xml
npm run build --prefix ClientApp
dotnet restore RentRight.CI.slnx --locked-mode /p:ContinuousIntegrationBuild=true
dotnet build RentRight.CI.slnx --no-restore --configuration Release /p:ContinuousIntegrationBuild=true
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project RentRight.csproj --configuration Release --no-build
dotnet test RentRight.Tests/RentRight.Tests.csproj --configuration Release --no-build --no-restore --settings coverage.runsettings --collect:"Code Coverage;Format=Cobertura" --logger "trx;LogFileName=fast.trx" --results-directory TestResults/fast
dotnet test RentRight.Tests/SqlIntegration/RentRight.SqlIntegration.Tests.csproj --configuration Release --no-build --no-restore --logger "trx;LogFileName=sql.trx" --results-directory TestResults/sql --blame-hang-timeout 5m
docker build --build-arg VERSION=local --tag rentright:quality .
bash scripts/scan-image.sh rentright:quality
node scripts/verify-compose.mjs
```

The local EF manifest matches EF runtime/package 10.0.10; it does not update the
global CLI. Updating it belongs with a reviewed package/tool alignment change.
The production container's independently patched runtime remains 10.0.13.

For intentional NuGet dependency updates, edit the relevant project, run
`dotnet restore RentRight.CI.slnx --force-evaluate` **without** the CI property,
review all affected `packages.lock.json` changes and rerun locked restore/build/tests.
Commit the locks together with project changes. Docker copies the web lock and
shared build settings before restore, so lock mismatches also fail publication.
For npm, update `package.json`/`package-lock.json` together and verify with `npm ci`.
Do not regenerate locks in CI to conceal drift. No new application package is added.

## Required branch protection

After a successful PR run, configure a `main` branch ruleset/protection requiring:

1. A pull request and an up-to-date branch (or the repository's chosen merge-queue policy).
2. **Required quality gate**, from GitHub Actions. Its exact displayed name is
   stable even if underlying jobs change. Remove the obsolete `build-and-test`
   requirement if previously configured.
3. No bypass/force-push/deletion except an explicitly agreed emergency process.

The workflow does not edit repository rules. After explicit owner authorization
on 2026-10-18, `main` protection was enabled and read back through GitHub's API:
a PR and an up-to-date passing `Required quality gate` specifically from GitHub
Actions (app 15368) are required, including for administrators. Force pushes and
branch deletion are blocked. No additional reviewer approval is required for the
single-owner workflow. At initial implementation the branch was unprotected;
the protection configuration was a separately authorized follow-up.

## Verification record

The initial hosted run [39150743815](https://github.com/kevinasramoska/RentRight/actions/runs/39150743815)
included one explicitly temporary failing React test: React failed and `Required
quality gate` failed; Container quality passed. That run also exposed three real
HTTP-test failures because React assets had not crossed the new job boundary.
The workflow now transfers the actual built frontend before backend build/tests;
no behavioral assertion was removed or weakened. The deliberate probe was removed
after confirming failure, and a clean rerun is required before marking ready.

Local verification on 2026-10-18: locked restore of all three projects, warning-free
Release build, 596 fast tests with a real Cobertura artifact, all 85 React tests
with JUnit, lint/type-check/production build, clean npm/NuGet audits, and no EF
model drift. The digest-pinned empty-database SQL migration smoke passed. The
production Docker build and Trivy scan passed (three .NET dependency manifests,
97 Ubuntu packages, zero HIGH/CRITICAL findings). A temporary real project-version
mismatch failed locked restore with `NU1004` for all three projects; restoring
the original dependency restored a successful forced locked restore. Merely
changing informational lock metadata was not used as evidence for that check.
Scanning a nonexistent image failed rather than reporting success.

The final immutable tested head, complete SQL-suite/Compose outcomes and downloadable
reports are available in [PR #27's checks](https://github.com/kevinasramoska/RentRight/pull/27/checks).
The PR was kept draft until those checks passed. Protection was enabled separately
as recorded above; no registry publication or Azure resource is configured here.

Review corrected the local command order so the real React assets exist before
the .NET build/HTTP tests on a fresh checkout. This does not change the workflow
or application behavior; CI already transfers those assets before backend build.

Underlying policies follow the official [NuGet lock-file guidance](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#locking-dependencies),
[NuGet audit guidance](https://learn.microsoft.com/en-us/nuget/concepts/auditing-packages)
and [GitHub secure-use guidance](https://docs.github.com/en/actions/reference/security/secure-use).

## Changed files

| File | Purpose |
| --- | --- |
| `.github/workflows/ci.yml` | Pinned read-only parallel checks, cancellation, reports and fail-closed aggregate |
| `Directory.Build.props` | NuGet lock/audit defaults and CI warning policy |
| `RentRight.CI.slnx` | Build all three existing projects without changing the local solution |
| `.config/dotnet-tools.json` | Repository-local EF tool version |
| `packages.lock.json` | Application direct/transitive dependency closure |
| `RentRight.Tests/packages.lock.json` | Fast-test dependency closure |
| `RentRight.Tests/SqlIntegration/packages.lock.json` | SQL-test dependency closure |
| `RentRight.Tests/SqlIntegration/SqlServerFixture.cs` | Pin the already-used SQL image by digest |
| `Dockerfile` | Honor the web lock, build settings and warnings policy during publication |
| `scripts/scan-image.sh` | Fail-closed pinned image scan with temporary-archive cleanup |
| `README.md` | Link the current CI behavior/runbook |
| `docs/SQL_INTEGRATION_TESTS.md` | Explain actual lock/CI solution and pinned SQL behavior |
| `docs/TASKS.md` | Record the delivered P11-01 scope and enabled protection boundary |
| `docs/CI.md` | This runbook, inventory, response rules and verification evidence |
