// Isolated local/CI lifecycle test. Never targets an existing Compose project.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { createHash, randomBytes } from 'node:crypto';
import { existsSync, mkdtempSync, readFileSync, readdirSync, rmSync, statSync } from 'node:fs';
import { request } from 'node:https';
import { tmpdir } from 'node:os';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const project = `rentright-verify-${randomBytes(6).toString('hex')}`;
const config = mkdtempSync(join(tmpdir(), 'rentright-compose-'));
const environment = { ...process.env, RENTRIGHT_COMPOSE_PROJECT: project, RENTRIGHT_LOCAL_CONFIG_DIR: config, RENTRIGHT_HTTPS_PORT: '0' };
for (const key of ['RENTRIGHT_SQL_PASSWORD', 'RENTRIGHT_ADMIN_PASSWORD', 'RENTRIGHT_ADMIN_EMAIL', 'RENTRIGHT_CERT_DIR']) delete environment[key];
const helper = join(repo, 'scripts/local-dev.sh');
const envFile = join(config, '.env');
let initialized = false;
let cleaned = false;
function execute(command, args, { quiet = false, failure = false } = {}) {
  const result = spawnSync(command, args, { cwd: repo, env: environment, encoding: 'utf8', timeout: 600_000 });
  if (!quiet) { process.stdout.write(result.stdout ?? ''); process.stderr.write(result.stderr ?? ''); }
  if (failure) { assert.notEqual(result.status, 0, 'Expected the unsafe/incomplete operation to be refused.'); return result; }
  if (result.error || result.status !== 0) throw new Error(`${command} failed (${result.status ?? 'not started'}). No credentials are included in this diagnostic.`);
  return result.stdout.trim();
}
const local = (...args) => execute('bash', [helper, ...args]);
const compose = (...args) => execute('docker', ['compose', '--project-name', project, '--env-file', envFile, '--file', join(repo, 'compose.yml'), ...args], { quiet: true });
function cleanup() {
  if (cleaned) return;
  if (initialized) local('reset', '--confirm', project);
  rmSync(config, { recursive: true, force: true }); // Only this test's mkdtemp directory.
  cleaned = true;
}
for (const [signal, code] of [['SIGINT', 130], ['SIGTERM', 143]]) process.once(signal, () => {
  try { cleanup(); } catch { console.error(`Cleanup needs attention for test project ${project}; private settings retained at ${config}.`); }
  process.exit(code);
});

// An existing project/volume is never adopted or deleted by this test.
for (const volume of ['sql-data', 'data-protection']) {
  const result = spawnSync('docker', ['volume', 'inspect', `${project}_${volume}`], { encoding: 'utf8' });
  assert.notEqual(result.status, 0, 'Refusing to reuse an existing volume.');
}
assert.equal(execute('docker', ['ps', '--all', '--quiet', '--filter', `label=com.docker.compose.project=${project}`], { quiet: true }), '');

try {
  execute('docker', ['compose', '--project-name', project, '--env-file', join(repo, '.env.example'), '--file', join(repo, 'compose.yml'), 'config', '--quiet'], { quiet: true, failure: true });
  console.log('PASS: empty example secrets fail before any service starts.');
  local('init'); initialized = true;
  assert.equal(statSync(envFile).mode & 0o777, 0o600);
  assert.equal(statSync(config).mode & 0o777, 0o700);
  const digest = () => createHash('sha256').update(readFileSync(envFile)).digest('hex');
  const originalDigest = digest();
  execute('bash', [helper, 'init'], { quiet: true, failure: true });
  assert.equal(digest(), originalDigest, 'Repeated initialization must not replace credentials.');
  const settings = JSON.parse(compose('config', '--format', 'json'));
  assert.equal(settings.services.sql.ports, undefined, 'SQL must have no host port.');
  assert.equal(settings.services.app.ports[0].host_ip, '127.0.0.1');
  assert.equal(settings.services.app.depends_on.initialize.condition, 'service_completed_successfully');
  assert.equal(settings.services.app.environment.LocalDevelopment__AllowInitialization, undefined);
  assert.equal(settings.services.app.environment.SeedAdmin__Password, undefined);
  console.log('PASS: local configuration is private, not overwritten, and SQL is not published.');
  local('up');
  const runningApp = compose('ps', '--quiet', 'app');
  assert.equal(execute('docker', ['inspect', runningApp, '--format', '{{.Config.User}}'], { quiet: true }), '1654');
  assert.equal(execute('docker', ['inspect', runningApp, '--format', '{{.HostConfig.ReadonlyRootfs}}'], { quiet: true }), 'true');
  assert.deepEqual(JSON.parse(execute('docker', ['inspect', runningApp, '--format', '{{json .HostConfig.CapDrop}}'], { quiet: true })), ['ALL']);
  const rejectedProductionJob = execute('docker', ['compose', '--project-name', project, '--env-file', envFile, '--file', join(repo, 'compose.yml'), 'run', '--rm', '--no-deps', '--env', 'ASPNETCORE_ENVIRONMENT=Production', 'initialize'], { quiet: true, failure: true });
  assert.match(rejectedProductionJob.stdout + rejectedProductionJob.stderr, /Local initialization requires Development/);

  const certificate = readFileSync(join(config, 'certs/localhost.pem'));
  const values = Object.fromEntries(readFileSync(envFile, 'utf8').trim().split('\n').map(line => {
    const equals = line.indexOf('='); return [line.slice(0, equals), line.slice(equals + 1)];
  }));
  let port;
  function readPort() {
    const address = compose('port', 'app', '8443');
    const match = /^127\.0\.0\.1:(\d+)$/.exec(address);
    assert.ok(match, 'Expected a loopback-only HTTPS binding.');
    port = Number(match[1]);
  }
  readPort();
  const cookies = new Map();
  function http(method, path, body, extraHeaders = {}) {
    return new Promise((resolveResponse, reject) => {
      const headers = { Cookie: [...cookies].map(([key, value]) => `${key}=${value}`).join('; '), ...extraHeaders };
      if (body !== undefined) headers['Content-Length'] = Buffer.byteLength(body);
      const outgoing = request({ hostname: '127.0.0.1', port, servername: 'localhost', ca: certificate, path, method, headers, timeout: 15_000 }, response => {
        const chunks = [];
        for (const entry of response.headers['set-cookie'] ?? []) {
          const pair = entry.split(';', 1)[0], equals = pair.indexOf('=');
          if (pair.slice(equals + 1)) cookies.set(pair.slice(0, equals), pair.slice(equals + 1)); else cookies.delete(pair.slice(0, equals));
        }
        response.on('data', chunk => chunks.push(chunk));
        response.on('end', () => resolveResponse({ status: response.statusCode, headers: response.headers, text: Buffer.concat(chunks).toString('utf8') }));
      });
      outgoing.on('error', reject);
      outgoing.on('timeout', () => outgoing.destroy(new Error('Local HTTPS request timed out.')));
      outgoing.end(body);
    });
  }
  const json = (method, path, value, headers) => http(method, path, value === undefined ? undefined : JSON.stringify(value), { 'Content-Type': 'application/json', ...headers });
  const query = sql => Number(compose('exec', '-T', 'sql', 'sh', '-c', 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -I -b -d RentRightLocal -h -1 -W -Q "$1"', 'sh', `SET NOCOUNT ON; ${sql}`));
  const migrationCount = readdirSync(join(repo, 'Migrations')).filter(file => file.endsWith('.cs') && !file.endsWith('.Designer.cs') && !file.endsWith('ModelSnapshot.cs')).length;
  assert.equal(query('SELECT COUNT(*) FROM __EFMigrationsHistory'), migrationCount);
  assert.equal(query('SELECT COUNT(*) FROM AspNetUsers'), 1);
  assert.equal(query('SELECT COUNT(*) FROM AspNetRoles'), 3);
  for (const probe of ['live', 'ready']) {
    const response = await http('GET', `/health/${probe}`);
    assert.equal(response.status, 200);
    assert.equal(response.text, 'Healthy');
    assert.match(response.headers['cache-control'], /no-store/);
  }
  assert.equal((await http('GET', '/app/properties')).status, 200);
  assert.equal((await http('GET', '/api/v1/workspace')).status, 401);
  const loginPage = await http('GET', '/Identity/Account/Login');
  assert.equal(loginPage.status, 200);
  const token = /name="__RequestVerificationToken"[^>]*value="([^"]+)"/.exec(loginPage.text)?.[1];
  assert.ok(token, 'Identity login must render antiforgery.');
  const form = new URLSearchParams({ 'Input.Email': values.RENTRIGHT_ADMIN_EMAIL, 'Input.Password': values.RENTRIGHT_ADMIN_PASSWORD, __RequestVerificationToken: token });
  const login = await http('POST', '/Identity/Account/Login', form.toString(), { 'Content-Type': 'application/x-www-form-urlencoded' });
  assert.equal(login.status, 302, 'Generated development administrator must be able to sign in.');
  assert.ok((login.headers['set-cookie'] ?? []).some(cookie => cookie.startsWith('.AspNetCore.Identity.Application=') && /;\s*secure/i.test(cookie)));
  const session = await http('GET', '/api/v1/session');
  assert.equal(JSON.parse(session.text).isAuthenticated, true);
  assert.ok(JSON.parse(session.text).roles.includes('Admin'));
  const property = { title: 'Compose verification home', address: 'Synthetic street', rentAmount: 1200, description: 'Disposable local lifecycle verification' };
  assert.equal((await json('POST', '/api/v1/properties', property)).status, 400, 'Authenticated writes must still require CSRF.');
  const csrf = JSON.parse((await http('GET', '/api/v1/session/antiforgery')).text).requestToken;
  const created = await json('POST', '/api/v1/properties', property, { 'X-CSRF-TOKEN': csrf });
  assert.equal(created.status, 201, 'A real authenticated API write must persist.');
  const id = JSON.parse(created.text).id;
  async function assertPersisted() {
    const current = await http('GET', '/api/v1/session');
    assert.equal(JSON.parse(current.text).isAuthenticated, true, 'Cookie/data-protection keys must survive application recreation.');
    const detail = await http('GET', `/api/v1/workspace/properties/${id}`);
    assert.equal(detail.status, 200);
    assert.equal(JSON.parse(detail.text).title, property.title);
  }
  console.log('PASS: validated HTTPS, real Identity login, Secure cookie, CSRF denial and SQL-backed write.');
  local('restart'); readPort(); await assertPersisted();
  local('migrate');
  assert.equal(query('SELECT COUNT(*) FROM __EFMigrationsHistory'), migrationCount);
  assert.equal(query('SELECT COUNT(*) FROM AspNetUsers'), 1);
  assert.equal(query('SELECT COUNT(*) FROM Properties'), 1);
  console.log('PASS: restart preserves cookies/data, explicit migration and admin seed are repeatable.');
  execute('bash', [helper, 'reset'], { quiet: true, failure: true });
  execute('bash', [helper, 'reset', '--confirm', 'rentright-wrong-project'], { quiet: true, failure: true });
  await assertPersisted();
  const application = compose('ps', '--quiet', 'app'), database = compose('ps', '--quiet', 'sql');
  compose('stop', '--timeout', '30');
  for (const container of [application, database]) assert.equal(execute('docker', ['inspect', container, '--format', '{{.State.ExitCode}}'], { quiet: true }), '0', 'Service must stop gracefully.');
  local('stop');
  for (const volume of ['sql-data', 'data-protection']) execute('docker', ['volume', 'inspect', `${project}_${volume}`], { quiet: true });
  local('up'); readPort(); await assertPersisted();
  assert.equal(query('SELECT COUNT(*) FROM AspNetUsers'), 1);
  assert.equal(query('SELECT COUNT(*) FROM Properties'), 1);
  console.log('PASS: unconfirmed resets are denied; graceful down/up preserves named volumes, cookie and listing.');
  if (process.env.RENTRIGHT_COMPOSE_VERIFY_PAUSE === '1') {
    console.log(`Optional real browser preview: https://localhost:${port}/app/properties (60 seconds).`);
    await new Promise(done => setTimeout(done, 60_000));
  }
  cleanup();
  for (const volume of ['sql-data', 'data-protection']) {
    const result = spawnSync('docker', ['volume', 'inspect', `${project}_${volume}`], { encoding: 'utf8' });
    assert.notEqual(result.status, 0, 'Confirmed reset must remove only the owned test volumes.');
  }
  assert.equal(existsSync(config), false);
  console.log('PASS: confirmed reset removed this test project and its data; all lifecycle checks passed.');
} finally {
  cleanup();
}
