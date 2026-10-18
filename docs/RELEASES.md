# Manual private GHCR releases (P11-02)

The chosen image repository is `ghcr.io/kevinasramoska/rentright`, initially
private. Publishing is **manual**, not a side effect of a PR, merge, tag or CI
completion. Nothing here deploys the application, applies a shared database
migration, creates Azure resources or changes account/role behavior.

## Release boundary

1. Merge reviewed code through the protected `main` PR gate. Wait for the new
   **main push** CI run to pass, including `Required quality gate`.
2. On `main`, manually run **Manual private GHCR release**, supplying that numeric
   CI run ID. Leave `publish=false` for a metadata-only rehearsal, or select true
   to request publication. Do not select a PR CI run, even if green.
3. Validation checks the real CI workflow ID/path, repository/head-repository IDs,
   push event, protected main tip, exact commit, completed successful run and
   successful aggregate from that run's latest attempt. The owner-review and
   protected-branch approval policy must still exist; a deleted/relaxed approval
   environment is rejected rather than silently recreated without safeguards.
   Stale, fork, PR, failed,
   cancelled, pending or wrong-workflow evidence is rejected before publishing.
4. Publication additionally waits on the `ghcr-release` environment approval.
   After approval, build the pinned/locked image, verify non-root runtime identity,
   run the real Production SQL/HTTP smoke, scan HIGH/CRITICAL vulnerabilities and
   generate a CycloneDX SBOM. The exact source is checked again against CI/main.
5. Only then log in with that job's short-lived `GITHUB_TOKEN`, refuse a non-private
   existing package or an existing release tag, and push. Pull by the returned
   registry digest, compare the image config identity/source labels, and run the
   Production SQL/HTTP smoke again against the pulled image. A successful release
   records `state=verified`; partial failures never authorize deployment.

The `ghcr-release` environment was created with the owner as required reviewer,
protected-branch restriction and self-review allowed for this single-owner repo.
The workflow and validator further restrict execution to exact `main` in the
source repository. The owner must explicitly approve; there is no automatic
review/bypass step. Environment policy is administered outside the workflow.

Use the Actions UI or, after this workflow is merged, from a trusted checkout:

```bash
# Replace CI_RUN_ID with the successful push-to-main CI run, not the PR run.
gh workflow run release.yml --ref main -f ci_run_id=CI_RUN_ID -F publish=false
gh workflow run release.yml --ref main -f ci_run_id=CI_RUN_ID -F publish=true
```

The second command requests publication; it still waits for environment approval.
If `main` advances or CI is rerun while approval/build is waiting, validation
fails closed. Start a fresh dispatch using the latest passing main CI run.
The first live rehearsal/publication cannot run before the workflow exists on
the default branch. Creating this PR is not evidence that GHCR push/authentication
or package visibility has already been verified in GitHub Actions.

## Identity, privacy and immutability

- The rehearsal and validation jobs are read-only. Only the manual `publish` job
  has `packages: write`; PR CI never receives package-write permission. All
  actions and application/scanner/test-registry images are pinned by commit/digest.
- There is no `workflow_run`/`pull_request_target` privilege bridge, download of PR
  image archives, personal access token, registry password secret, `latest` alias,
  automatic publication, or arbitrary source/registry input.
- Tags are `sha-FULL_COMMIT-rWORKFLOW_RUN_ID-aATTEMPT`. Reruns get a different tag,
  and this publisher refuses to overwrite existing tags. Registry administrators
  can still change/delete tags outside this workflow: deployments must use
  `ghcr.io/kevinasramoska/rentright@sha256:...`, not a tag, for immutable selection.
- New GHCR packages default to private. An existing non-private package is refused;
  visibility is checked again after publishing. No visibility change is automated.
  Package metadata and version checks use the authenticated `/user/packages`
  endpoints, not the public-only `/users/{username}/packages` endpoints.
  Repository/namespace ownership and the existing OCI source label link the image
  to the repository. If a pre-existing package is not linked/permitted, fix its
  Actions access rather than creating a broad PAT or making it public.
- Registry authentication is passed through standard input, never arguments or
  build inputs. Login uses a temporary runner-specific Docker configuration and
  is logged out on completion/failure. Child build/scan/smoke processes do not
  inherit `GH_TOKEN`/`GITHUB_TOKEN`; only the metadata/API process needs the token.
- This is a **public source repository**. Uploaded workflow artifacts contain only
  explicit JSON reports, the SBOM and checksums, not image archives, credentials,
  certificates, database files or entire directories. Image bytes stay on the
  ephemeral runner until pushed to private GHCR. The SBOM exposes dependency
  versions already derivable from source; do not put private runtime settings in
  the image or build arguments. Private packages still need explicit host access
  before deployment; local Docker login is not configured by this task.

## Deployment and rollback contract

The metadata artifact contains exactly:

| File | Meaning |
| --- | --- |
| `release.json` | Source commit and passing CI/attempt, build run/attempt, config identity, registry tag/digest/pull reference, input hashes, migration source fingerprints, approval record and deployment/rollback boundary |
| `sbom.cdx.json` | CycloneDX dependency inventory from the exact tested image |
| `vulnerabilities.json` | Point-in-time HIGH/CRITICAL Trivy report; scanner/feed errors or findings stop publication |
| `SHA256SUMS` | Integrity checksums for those three metadata files |

Rehearsals retain metadata for 30 days; publication metadata for 90 days; PR
dry-run metadata for seven days. Retain a verified release's metadata privately
before expiry if it is selected for a long-lived deployment. No registry-version
deletion or retention cleanup is automated. SBOM/checksums are not signed
attestations and do not claim SLSA provenance or cryptographic authenticity.

Phase 12 must require `state=verified`, its immutable image pull reference, an
explicitly chosen deployment environment, separate approval and a separately
reviewed EF migration job/artifact. `deployment.authorized=false`, environment
null and `migrations.applied=false` intentionally remain in this release record.
It is an image release, not authorization to deploy or a database compatibility
promise. Migration source hashes include migration code, designers and snapshot
and identify the latest migration; no executable migration bundle is produced or
applied here. The historical raw SQL bootstrap remains unverified/broken as
documented in [DEPLOYMENT.md](DEPLOYMENT.md); do not publish it as a working path.

For image rollback, choose a previous **verified digest** with compatible schema,
retain its CI/source/SBOM evidence, obtain deployment approval and use that digest
without rebuilding it. Rolling back an image does not revert a database. Backups,
schema compatibility, restore/forward-fix decisions and persistent keys remain
Phase 12 deployment gates. A failed post-push verification may leave a private
package version; its record is `publishing` or `published-unverified`, never
`verified`. Do not promote it or silently retry by moving its tag.

## Local/PR verification

Use Node 24, the pinned SDK/EF tool and Docker/Linux amd64 support. No GHCR login
or personal credential is needed for this rehearsal:

```bash
node --test scripts/release.test.mjs
dotnet tool restore
docker build --build-arg VERSION=local --build-arg REVISION="$(git rev-parse HEAD)" --tag rentright:release-test .
node scripts/verify-release.mjs rentright:release-test
```

The rehearsal generates a non-publication-eligible manifest, confirms that even
setting its approval flag cannot publish it, verifies the actual SBOM/checksums,
checks that ordinary `dotnet publish` excludes generated diagnostic/release reports,
pushes to a fresh digest-pinned registry bound only to kernel-assigned random
loopback ports for both its API and debug listener in the Docker daemon's host
network namespace (the Linux VM on Docker Desktop, without changing daemon settings),
pulls by digest and checks the exact config/labels. Both pre-push and post-pull
smokes apply real EF migrations only to newly created disposable SQL containers,
seed one fictional property without a usable login, run read-only Production
containers and exercise real static/React/MVC/API responses and private-route
denials. Temporary registry/SQL/application containers and scratch files are
removed; unrelated Docker projects and configured databases are never adopted.
Docker images and ignored diagnostic metadata may remain for inspection.

PR CI runs these release-rule tests and the real local-registry rehearsal in
addition to the existing full-stack gate. Pinned Actionlint validates both workflow
files and expression contexts on every PR, with only `.github` mounted read-only
and all container capabilities dropped. Failed-CI/non-publication rules are
covered using explicit metadata fixtures; the local registry test is not claimed
as proof of live GHCR authentication, private visibility or environment approval.

## Verification and files

Local checks on 2026-10-18: 32 release-policy tests passed, locked Release build
passed with zero warnings/errors, all 596 fast backend tests passed and EF reported
no model changes. Both workflows pass Actionlint 1.7.12 (upstream archive checksum
verified; no global tool installation). Actual current-main CI evidence and the
configured owner approval policy were validated through read-only GitHub API calls.
The complete disposable registry push/digest-pull/config comparison and both
Production SQL/HTTP smokes passed. The initial Docker Desktop published-port
failure was resolved with daemon-local loopback listeners; no daemon settings
were changed. Generated-report exclusion was checked against actual MSBuild
publish inputs, not just project-file text. No live GHCR image is claimed.
Fresh hosted verification also exposed EF's metadata evaluation requiring an
existing assets file. The smoke now performs an explicit locked restore/build
before EF's `--no-build` migration command, without relying on a developer cache.

Release-rule tests cover source/repository/ref/workflow isolation, exact CI attempts,
failed/pending/cancelled CI, missing/failed/skipped aggregate checks, input redaction,
private visibility, authenticated package lookups and version pagination, no tag
replacement, config identity and repository-bound digests.
The PR's immutable tested head, full CI totals and real registry rehearsal results
are recorded in its checks. Live GHCR verification remains an after-merge manual
approval step; no website is deployed by this work.

| Changed file | Reason |
| --- | --- |
| `.github/workflows/release.yml` | Manual read-only rehearsal and isolated environment-approved private publisher |
| `.github/workflows/ci.yml` | Gate release rules and real disposable registry rehearsal; upload metadata only |
| `RentRight.csproj` | Exclude generated `artifacts`/`TestResults` JSON from ordinary application publication |
| `scripts/release.mjs` | CI authorization, source/image checks, private/no-overwrite publication and digest metadata |
| `scripts/release.test.mjs` | Deterministic positive/negative release-policy tests, without network credentials |
| `scripts/release-sbom.sh` | Pinned scanner-generated CycloneDX inventory; no archive upload |
| `scripts/verify-release.mjs` | Actual non-publication, SBOM/checksum, registry push/pull/start rehearsal and cleanup |
| `scripts/verify-container.sh` | Pin the already-used SQL test image consistently with Compose/integration tests |
| `README.md` | Explain the current manual private-image release boundary |
| `docs/CI.md` | Record release tests, Production smoke and safe metadata reports in the PR gate |
| `docs/DEPLOYMENT.md` | Use a verified image digest; separate image release from deployment/migrations |
| `docs/TASKS.md` | Honest P11-02 implementation/activation status |
| `docs/RELEASES.md` | This runbook, deployment contract, privacy limits, inventory and verification |

Primary references: [GHCR authentication/privacy and digest pulls](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry),
[job permissions](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax#jobsjob_idpermissions),
[environment approval](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/manage-environments)
and [Trivy SBOMs](https://trivy.dev/docs/latest/supply-chain/sbom/).
