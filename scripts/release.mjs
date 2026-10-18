// Manual release tooling. No PR artifact/code is consumed by the publisher.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { appendFileSync, mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export const repository = 'kevinasramoska/RentRight';
export const imageRepository = 'ghcr.io/kevinasramoska/rentright';
export const scanner = 'aquasec/trivy:0.76.0@sha256:2416d1f74045b57274df1a340eee6b510cacd73c66a6ccb8da092b117eb7d222';
const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = resolve(root, 'artifacts/release');
const shaPattern = /^[a-f0-9]{40}$/;
const digestPattern = /^sha256:[a-f0-9]{64}$/;
const numberPattern = /^[1-9][0-9]{0,15}$/;
const execute = (command, args, options = {}) => {
  const environment = { ...process.env };
  delete environment.GH_TOKEN;
  delete environment.GITHUB_TOKEN;
  return execFileSync(command, args, { cwd: root, env: environment, stdio: 'inherit', timeout: 600_000, ...options });
};
const capture = (command, args) => execute(command, args, { encoding: 'utf8', stdio: 'pipe' }).trim();
const hash = path => createHash('sha256').update(readFileSync(path)).digest('hex');
const save = (name, value) => writeFileSync(resolve(output, name), `${JSON.stringify(value, null, 2)}\n`);
const read = name => JSON.parse(readFileSync(resolve(output, name), 'utf8'));

export function releaseTag(sha, runId, attempt) {
  assert.match(sha, shaPattern, 'Expected a full commit SHA.');
  assert.match(String(runId), numberPattern, 'Expected a numeric workflow run ID.');
  assert.match(String(attempt), numberPattern, 'Expected a numeric attempt.');
  return `sha-${sha}-r${runId}-a${attempt}`;
}

export function validateEligibility(context, repo, workflow, branch, run, jobs) {
  assert.equal(context.repository, repository, 'Only the configured source repository may release.');
  assert.equal(context.event, 'workflow_dispatch', 'Publication requires a manual dispatch.');
  assert.equal(context.ref, 'refs/heads/main', 'Only main may release.');
  assert.match(context.sha, shaPattern);
  assert.equal(repo.full_name, repository);
  assert.equal(repo.default_branch, 'main');
  assert.equal(branch.protected, true, 'Main must remain protected.');
  assert.equal(branch.commit.sha, context.sha, 'The selected source must still be the main tip.');
  assert.equal(workflow.path, '.github/workflows/ci.yml');
  assert.equal(run.workflow_id, workflow.id, 'Use the actual repository CI workflow.');
  assert.equal(run.repository.id, repo.id);
  assert.equal(run.head_repository.id, repo.id, 'Fork runs cannot authorize publication.');
  assert.equal(run.event, 'push', 'PR checks cannot authorize publication.');
  assert.equal(run.head_branch, 'main');
  assert.equal(run.head_sha, context.sha, 'CI must cover exactly the selected source.');
  assert.equal(run.status, 'completed');
  assert.equal(run.conclusion, 'success', 'Failed, pending or cancelled CI cannot publish.');
  assert.ok(Number.isSafeInteger(run.id) && run.id > 0);
  assert.ok(Number.isSafeInteger(run.run_attempt) && run.run_attempt > 0);
  const gates = jobs.filter(job => job.name === 'Required quality gate');
  assert.equal(gates.length, 1, 'The successful CI attempt must include exactly one aggregate gate.');
  assert.equal(gates[0].status, 'completed');
  assert.equal(gates[0].conclusion, 'success');
  return { repository, commit: context.sha, ciRunId: run.id, ciAttempt: run.run_attempt,
    ciUrl: `https://github.com/${repository}/actions/runs/${run.id}` };
}

async function api(path, { missing = false } = {}) {
  assert.ok(process.env.GH_TOKEN, 'A short-lived workflow token is required.');
  const response = await fetch(`https://api.github.com${path}`, {
    headers: { Accept: 'application/vnd.github+json', Authorization: `Bearer ${process.env.GH_TOKEN}`, 'X-GitHub-Api-Version': '2026-03-10' },
    signal: AbortSignal.timeout(30_000), redirect: 'error',
  });
  if (missing && response.status === 404) return null;
  assert.ok(response.ok, `GitHub API request failed (${response.status}); no response body or credential is logged.`);
  return response.json();
}

export async function eligibility(runId, get = api, environment = process.env) {
  assert.match(String(runId), numberPattern, 'Select a numeric successful main CI run ID.');
  const prefix = `/repos/${repository}`;
  const [repo, workflow, branch, run] = await Promise.all([
    get(prefix), get(`${prefix}/actions/workflows/ci.yml`), get(`${prefix}/branches/main`), get(`${prefix}/actions/runs/${runId}`),
  ]);
  assert.ok(Number.isSafeInteger(run.run_attempt) && run.run_attempt > 0);
  const jobs = [];
  for (let page = 1; ; page++) {
    assert.ok(page <= 10, 'Unexpectedly large CI job listing.');
    const result = await get(`${prefix}/actions/runs/${runId}/attempts/${run.run_attempt}/jobs?per_page=100&page=${page}`);
    jobs.push(...result.jobs);
    if (result.jobs.length < 100) break;
  }
  return validateEligibility({ repository: environment.GITHUB_REPOSITORY, event: environment.GITHUB_EVENT_NAME,
    ref: environment.GITHUB_REF, sha: environment.GITHUB_SHA }, repo, workflow, branch, run, jobs);
}

export function verifyImage(image, sha) {
  assert.equal(image.Os, 'linux');
  assert.equal(image.Architecture, 'amd64');
  assert.equal(image.Config.User, '1654');
  assert.equal(image.Config.Labels['org.opencontainers.image.revision'], sha);
  assert.equal(image.Config.Labels['org.opencontainers.image.source'], `https://github.com/${repository}`);
  assert.match(image.Id, digestPattern);
  return image.Id;
}

export function registryDigest(repoDigests, name) {
  const matches = repoDigests.filter(value => value.startsWith(`${name}@`));
  assert.equal(matches.length, 1, 'Expected exactly one digest for the publication repository.');
  const digest = matches[0].slice(name.length + 1);
  assert.match(digest, digestPattern);
  return digest;
}

export function validatePackage(existing, tag, versions = []) {
  if (existing === null) return;
  assert.equal(existing.visibility, 'private', 'Refusing to publish into a non-private package.');
  assert.ok(!versions.some(version => version.metadata.container.tags.includes(tag)), 'Refusing to overwrite an existing release tag.');
}

// Shared by the private publisher and a loopback-only disposable-registry test.
// The CLI publisher never accepts a registry destination from user input.
export function pushAndPull(imageId, name, tag, sha) {
  assert.match(imageId, digestPattern);
  const target = `${name}:${tag}`;
  execute('docker', ['tag', imageId, target]);
  execute('docker', ['push', target]);
  const pushed = JSON.parse(capture('docker', ['image', 'inspect', target]))[0];
  const digest = registryDigest(pushed.RepoDigests ?? [], name);
  const reference = `${name}@${digest}`;
  execute('docker', ['pull', reference]);
  const pulled = JSON.parse(capture('docker', ['image', 'inspect', reference]))[0];
  assert.equal(verifyImage(pulled, sha), imageId, 'The pulled image must be the exact image tested before pushing.');
  return { digest, reference };
}

function checksums() {
  const names = ['release.json', 'sbom.cdx.json', 'vulnerabilities.json'];
  writeFileSync(resolve(output, 'SHA256SUMS'), names.map(name => `${hash(resolve(output, name))}  ${name}\n`).join(''));
}

async function prepare(imageName, verifiedCi = null) {
  const commit = capture('git', ['rev-parse', 'HEAD']);
  assert.match(commit, shaPattern);
  if (verifiedCi) {
    assert.equal(verifiedCi.commit, commit, 'Build only the validated source.');
    assert.equal(capture('git', ['status', '--porcelain']), '', 'Release source must be clean.');
  }
  const runId = process.env.GITHUB_RUN_ID ?? '1', attempt = process.env.GITHUB_RUN_ATTEMPT ?? '1';
  const tag = releaseTag(commit, runId, attempt);
  mkdirSync(output, { recursive: true });
  const inspected = JSON.parse(capture('docker', ['image', 'inspect', imageName]))[0];
  const imageId = verifyImage(inspected, commit);
  execute('bash', ['scripts/verify-container.sh', imageId]);
  execute('bash', ['scripts/scan-image.sh', imageId]);
  writeFileSync(resolve(output, 'vulnerabilities.json'), readFileSync(resolve(root, 'TestResults/container-vulnerabilities.json')));
  // Image bytes remain local. Only explicit metadata/report patterns are uploaded.
  execute('bash', ['scripts/release-sbom.sh', imageId]);
  const migrations = readdirSync(resolve(root, 'Migrations')).filter(name => /^\d+_.+\.cs$/.test(name) && !name.endsWith('.Designer.cs')).sort();
  const manifest = { schemaVersion: 1, state: 'prepared', source: verifiedCi ?? { repository, commit, ciRunId: null },
    publicationEligible: Boolean(verifiedCi), build: { runId, attempt, platform: 'linux/amd64', imageConfigDigest: imageId },
    image: { repository: imageRepository, tag, digest: null, pullReference: null, visibility: 'private' },
    inputs: Object.fromEntries(['Dockerfile', 'global.json', 'Directory.Build.props', 'packages.lock.json', 'ClientApp/package-lock.json'].map(path => [path, hash(resolve(root, path))])),
    migrations: { strategy: 'separate-reviewed-ef-job-required', applied: false, sourceCommit: commit,
      files: migrations.map(file => ({ file, sha256: hash(resolve(root, 'Migrations', file)) })) },
    deployment: { environment: null, authorized: false },
    rollback: { method: 'select-previous-verified-image-digest', databaseRollbackIncluded: false },
    approval: { environment: 'ghcr-release', recordUrl: process.env.GITHUB_RUN_ID ? `https://github.com/${repository}/actions/runs/${runId}` : null },
  };
  save('release.json', manifest); checksums();
  console.log(`Prepared ${commit}; publication eligible: ${manifest.publicationEligible}. No database deployment was performed.`);
  return manifest;
}

async function publish() {
  assert.equal(process.env.RENTRIGHT_PUBLISH_APPROVED, 'true', 'Use the manually approved publication job.');
  const manifest = read('release.json');
  assert.equal(manifest.state, 'prepared');
  assert.equal(manifest.publicationEligible, true, 'A PR/local dry run can never publish.');
  assert.equal(manifest.image.repository, imageRepository);
  assert.equal(manifest.image.tag, releaseTag(process.env.GITHUB_SHA, process.env.GITHUB_RUN_ID, process.env.GITHUB_RUN_ATTEMPT));
  const fresh = await eligibility(String(manifest.source.ciRunId));
  assert.deepEqual(fresh, manifest.source, 'CI must still be successful on the same attempt/source after approval and build.');
  const packagePath = '/users/kevinasramoska/packages/container/rentright';
  const existing = await api(packagePath, { missing: true });
  validatePackage(existing, manifest.image.tag);
  if (existing) {
    for (let page = 1; ; page++) {
      assert.ok(page <= 100, 'Review package retention before publishing more versions.');
      const versions = await api(`${packagePath}/versions?per_page=100&page=${page}`);
      validatePackage(existing, manifest.image.tag, versions);
      if (versions.length < 100) break;
    }
  }
  execute('docker', ['login', 'ghcr.io', '--username', process.env.GITHUB_ACTOR, '--password-stdin'], { input: process.env.GH_TOKEN, stdio: ['pipe', 'inherit', 'inherit'] });
  try {
    const { digest, reference } = pushAndPull(manifest.build.imageConfigDigest, imageRepository, manifest.image.tag, manifest.source.commit);
    Object.assign(manifest.image, { digest, pullReference: reference });
    manifest.state = 'published-unverified'; save('release.json', manifest); checksums();
    assert.equal((await api(packagePath)).visibility, 'private', 'Package must remain private.');
    execute('bash', ['scripts/verify-container.sh', reference]);
    manifest.state = 'verified'; save('release.json', manifest); checksums();
    console.log(`Verified private release: ${reference}`);
    if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY,
      `Private image: \`${reference}\`\n\nSource: \`${manifest.source.commit}\`\n\n[Passing CI](${manifest.source.ciUrl})\n\nNo application or database deployment performed.\n`);
  } finally {
    execute('docker', ['logout', 'ghcr.io']);
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const [command, argument] = process.argv.slice(2);
    if (command === 'validate') {
      const result = await eligibility(argument);
      if (process.env.GITHUB_OUTPUT) appendFileSync(process.env.GITHUB_OUTPUT, `sha=${result.commit}\n`);
      console.log(`Eligible source: ${result.commit}; CI ${result.ciRunId}, attempt ${result.ciAttempt}.`);
    } else if (command === 'prepare') {
      const ci = process.env.RENTRIGHT_CI_RUN_ID ? await eligibility(process.env.RENTRIGHT_CI_RUN_ID) : null;
      await prepare(argument, ci);
    } else if (command === 'publish') await publish();
    else throw new Error('Usage: node scripts/release.mjs {validate CI_RUN_ID|prepare LOCAL_IMAGE|publish}');
  } catch (error) {
    // Assertions contain only source/check metadata; provider response bodies/tokens are never logged.
    console.error(error.message.split('\n')[0]);
    process.exitCode = 1;
  }
}
