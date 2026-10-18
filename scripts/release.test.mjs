import assert from 'node:assert/strict';
import test from 'node:test';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import { eligibility, imageRepository, registryDigest, releaseTag, repository, validateEligibility, validatePackage, verifyImage } from './release.mjs';

const sha = 'a'.repeat(40);
function fixture() {
  return {
    context: { repository, event: 'workflow_dispatch', ref: 'refs/heads/main', sha },
    repo: { id: 123, full_name: repository, default_branch: 'main' },
    workflow: { id: 456, path: '.github/workflows/ci.yml' },
    branch: { protected: true, commit: { sha } },
    run: { id: 789, run_attempt: 2, workflow_id: 456, repository: { id: 123 }, head_repository: { id: 123 },
      event: 'push', head_branch: 'main', head_sha: sha, status: 'completed', conclusion: 'success' },
    jobs: [{ name: 'Required quality gate', status: 'completed', conclusion: 'success' }],
  };
}
const validate = value => validateEligibility(value.context, value.repo, value.workflow, value.branch, value.run, value.jobs);

test('successful protected-main push CI authorizes exactly its source and attempt', () => {
  const result = validate(fixture());
  assert.equal(result.commit, sha);
  assert.equal(result.ciRunId, 789);
  assert.equal(result.ciAttempt, 2);
});

for (const [name, mutate] of [
  ['a fork dispatch', value => { value.context.repository = 'attacker/RentRight'; }],
  ['a PR trigger', value => { value.context.event = 'pull_request'; }],
  ['a feature dispatch', value => { value.context.ref = 'refs/heads/feature'; }],
  ['an unprotected main', value => { value.branch.protected = false; }],
  ['a stale main source', value => { value.branch.commit.sha = 'b'.repeat(40); }],
  ['another CI workflow', value => { value.run.workflow_id = 999; }],
  ['a renamed workflow path', value => { value.workflow.path = '.github/workflows/other.yml'; }],
  ['a foreign repository run', value => { value.run.repository.id = 999; }],
  ['a fork head named main', value => { value.run.head_repository.id = 999; }],
  ['passing PR CI', value => { value.run.event = 'pull_request'; }],
  ['a different branch run', value => { value.run.head_branch = 'feature'; }],
  ['a different source SHA', value => { value.run.head_sha = 'b'.repeat(40); }],
  ['in-progress CI', value => { value.run.status = 'in_progress'; }],
  ['failed CI', value => { value.run.conclusion = 'failure'; }],
  ['cancelled CI', value => { value.run.conclusion = 'cancelled'; }],
  ['missing aggregate gate', value => { value.jobs = []; }],
  ['duplicate aggregate gates', value => { value.jobs.push({ ...value.jobs[0] }); }],
  ['failed aggregate', value => { value.jobs[0].conclusion = 'failure'; }],
  ['skipped aggregate', value => { value.jobs[0].conclusion = 'skipped'; }],
  ['pending aggregate', value => { value.jobs[0].status = 'queued'; }],
]) test(`refuses ${name} before publication`, () => {
  const value = fixture(); mutate(value);
  assert.throws(() => validate(value));
});

test('CI API validation reads the requested run attempt, not previous successful jobs', async () => {
  const value = fixture(), paths = [];
  const get = async path => {
    paths.push(path);
    if (path.endsWith('/actions/workflows/ci.yml')) return value.workflow;
    if (path.endsWith('/branches/main')) return value.branch;
    if (path.endsWith('/actions/runs/789')) return value.run;
    if (path.includes('/attempts/2/jobs?')) return { jobs: value.jobs };
    return value.repo;
  };
  const result = await eligibility('789', get, { GITHUB_REPOSITORY: repository, GITHUB_EVENT_NAME: 'workflow_dispatch', GITHUB_REF: 'refs/heads/main', GITHUB_SHA: sha });
  assert.equal(result.ciAttempt, 2);
  assert.ok(paths.some(path => path.includes('/attempts/2/jobs?')));
});

test('untrusted run identifiers never reach GitHub or a shell', async () => {
  for (const id of ['../1', '1\nsha=forged', '1;echo secret', '0', '-1', '', 'https://attacker.example'])
    await assert.rejects(eligibility(id, () => { throw new Error('Unexpected network call'); }), /numeric successful main CI run ID/);
});

test('tags include the full source, run and attempt without a mutable latest alias', () => {
  assert.equal(releaseTag(sha, 12, 3), `sha-${sha}-r12-a3`);
  assert.notEqual(releaseTag(sha, 12, 3), releaseTag(sha, 12, 4));
  for (const value of ['main', 'latest', 'x'.repeat(40), `${sha}\n`]) assert.throws(() => releaseTag(value, 12, 3));
});

function image() {
  return { Os: 'linux', Architecture: 'amd64', Id: `sha256:${'b'.repeat(64)}`, Config: { User: '1654', Labels: {
    'org.opencontainers.image.revision': sha, 'org.opencontainers.image.source': `https://github.com/${repository}`,
  } } };
}
test('image checks reject wrong ownership, source, architecture or identity', () => {
  assert.equal(verifyImage(image(), sha), image().Id);
  for (const mutate of [value => { value.Config.User = '0'; }, value => { value.Architecture = 'arm64'; },
    value => { value.Config.Labels['org.opencontainers.image.revision'] = 'b'.repeat(40); },
    value => { value.Config.Labels['org.opencontainers.image.source'] = 'https://attacker.example'; }]) {
    const value = image(); mutate(value); assert.throws(() => verifyImage(value, sha));
  }
});

test('deployment digest must belong to exactly the selected registry repository', () => {
  const digest = `sha256:${'c'.repeat(64)}`;
  assert.equal(registryDigest([`unrelated/repo@${digest}`, `${imageRepository}@${digest}`], imageRepository), digest);
  for (const values of [[], ['other/repo@sha256:bad'], [`${imageRepository}@bad`], [`${imageRepository}@${digest}`, `${imageRepository}@${digest}`]])
    assert.throws(() => registryDigest(values, imageRepository));
});

test('private-only publication refuses visibility changes and existing tags', () => {
  assert.doesNotThrow(() => validatePackage(null, 'new-tag'));
  assert.doesNotThrow(() => validatePackage({ visibility: 'private' }, 'new-tag', [{ metadata: { container: { tags: ['old-tag'] } } }]));
  for (const visibility of ['public', 'internal', undefined]) assert.throws(() => validatePackage({ visibility }, 'new-tag'));
  assert.throws(() => validatePackage({ visibility: 'private' }, 'new-tag', [{ metadata: { container: { tags: ['new-tag'] } } }]));
});

test('invalid CLI inputs are rejected without echoing their contents', () => {
  const sentinel = 'not-a-run-id-sensitive-input';
  const result = spawnSync(process.execPath, [fileURLToPath(new URL('./release.mjs', import.meta.url)), 'validate', sentinel], { encoding: 'utf8' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /numeric successful main CI run ID/);
  assert.ok(!result.stderr.includes(sentinel));
});
