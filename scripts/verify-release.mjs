// Real push/pull/start rehearsal using only a fresh loopback registry and fake SQL data.
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { randomBytes, createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pushAndPull, verifyImage } from './release.mjs';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const run = (command, args, options = {}) => execFileSync(command, args, { cwd: root, stdio: 'inherit', timeout: 600_000, ...options });
const capture = (command, args) => run(command, args, { encoding: 'utf8', stdio: 'pipe' }).trim();
const name = `rentright-registry-${randomBytes(8).toString('hex')}`;
let container;
try {
  const image = process.argv[2]; assert.ok(image, 'Pass the already-built CI image.');
  run(process.execPath, ['scripts/release.mjs', 'prepare', image], { env: { ...process.env, RENTRIGHT_CI_RUN_ID: '' } });
  const manifest = JSON.parse(readFileSync(resolve(root, 'artifacts/release/release.json'), 'utf8'));
  const publishInputs = JSON.parse(capture('dotnet', ['msbuild', 'RentRight.csproj', '-getItem:Content']));
  assert.ok(!publishInputs.Items.Content.some(item => /^(artifacts|TestResults)\//i.test(item.Identity.replaceAll('\\', '/'))),
    'Normal dotnet publish must not copy diagnostic/release reports into the application.');
  assert.equal(manifest.publicationEligible, false);
  const denied = spawnSync(process.execPath, ['scripts/release.mjs', 'publish'], {
    cwd: root, encoding: 'utf8', env: { ...process.env, RENTRIGHT_PUBLISH_APPROVED: 'true' }, timeout: 10_000,
  });
  assert.notEqual(denied.status, 0);
  assert.match(denied.stderr, /PR\/local dry run can never publish/);
  console.log('PASS: an actual dry-run manifest cannot enter publication, even with its approval flag set.');
  const bom = JSON.parse(readFileSync(resolve(root, 'artifacts/release/sbom.cdx.json'), 'utf8'));
  assert.equal(bom.bomFormat, 'CycloneDX'); assert.ok(bom.components.length > 0);
  for (const line of readFileSync(resolve(root, 'artifacts/release/SHA256SUMS'), 'utf8').trim().split('\n')) {
    const [expected, file] = line.split('  ');
    assert.equal(createHash('sha256').update(readFileSync(resolve(root, 'artifacts/release', file))).digest('hex'), expected);
  }
  // Daemon-local loopback works both on Linux and inside Docker Desktop's VM.
  // Kernel-assigned ports avoid adopting another process; debug also stays loopback-only.
  container = capture('docker', ['run', '--detach', '--name', name, '--network', 'host', '--cap-drop', 'ALL', '--security-opt', 'no-new-privileges',
    '--env', 'REGISTRY_HTTP_ADDR=127.0.0.1:0', '--env', 'REGISTRY_HTTP_DEBUG_ADDR=127.0.0.1:0',
    'registry:3@sha256:ddf754342cfc8acc51a56d5d0ab6af06826461864460636d8bd5c546dab2a7b8']);
  let address;
  let ready = false;
  for (let attempt = 0; attempt < 30; attempt++) {
    const logResult = spawnSync('docker', ['logs', container], { encoding: 'utf8', timeout: 3000 });
    const logs = `${logResult.stdout ?? ''}${logResult.stderr ?? ''}`;
    address = /msg="listening on (127\.0\.0\.1:\d+)"/.exec(logs)?.[1];
    if (address) {
      const probe = spawnSync('docker', ['exec', container, 'wget', '-q', '-O', '-', `http://${address}/v2/`], { encoding: 'utf8', timeout: 3000 });
      ready = probe.status === 0;
    }
    if (ready) break;
    await new Promise(done => setTimeout(done, 1000));
  }
  assert.ok(ready, 'Disposable registry must become ready.');
  const localRepository = `${address}/rentright`;
  const pushed = pushAndPull(manifest.build.imageConfigDigest, localRepository, manifest.image.tag, manifest.source.commit);
  assert.match(pushed.reference, /^127\.0\.0\.1:\d+\/rentright@sha256:[a-f0-9]{64}$/);
  const pulled = JSON.parse(capture('docker', ['image', 'inspect', pushed.reference]))[0];
  assert.equal(verifyImage(pulled, manifest.source.commit), manifest.build.imageConfigDigest);
  run('bash', ['scripts/verify-container.sh', pushed.reference]);
  console.log('PASS: real registry push, digest pull, exact image/label verification, SBOM/checksums and Production SQL/HTTP smoke. No GHCR publication occurred.');
} finally {
  if (container) run('docker', ['rm', '--force', '--volumes', container]);
}
