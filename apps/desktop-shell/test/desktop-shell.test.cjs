const assert = require('node:assert/strict');
const { EventEmitter } = require('node:events');
const { mkdtemp, writeFile, mkdir, rm } = require('node:fs/promises');
const http = require('node:http');
const os = require('node:os');
const path = require('node:path');
const { PassThrough } = require('node:stream');
const test = require('node:test');

const {
  resolveHostCommand,
  waitForHostReady,
} = require('../lib/host-process.cjs');
const { startStaticServer } = require('../lib/static-server.cjs');

test('packaged host command resolves only inside Electron resources', () => {
  const command = resolveHostCommand({
    isPackaged: true,
    resourcesPath: 'C:\\Program Files\\Workspace Environment\\resources',
    repoRoot: 'D:\\source\\workspace-environment',
    parentPid: 4321,
  });

  assert.deepEqual(command, {
    executable: path.resolve(
      'C:\\Program Files\\Workspace Environment\\resources',
      'host',
      'Workspace.Host.exe'),
    args: ['--parent-pid', '4321'],
    cwd: path.resolve(
      'C:\\Program Files\\Workspace Environment\\resources',
      'host'),
  });
});

test('development host command preserves direct dotnet workflow', () => {
  const command = resolveHostCommand({
    isPackaged: false,
    resourcesPath: 'ignored',
    repoRoot: 'D:\\source\\workspace-environment',
    parentPid: 99,
  });

  assert.equal(command.executable, 'dotnet');
  assert.deepEqual(command.args, [
    'run',
    '--project',
    path.resolve(
      'D:\\source\\workspace-environment',
      'apps',
      'host-windows',
      'src',
      'Workspace.Host',
      'Workspace.Host.csproj'),
    '--configuration',
    'Release',
    '--no-launch-profile',
    '--',
    '--parent-pid',
    '99',
  ]);
  assert.equal(command.cwd, path.resolve('D:\\source\\workspace-environment'));
});

test('host readiness accepts a listening message split across output chunks', async () => {
  const child = createChild();
  const ready = waitForHostReady(child, { timeoutMs: 1_000 });

  child.stdout.write('Workspace Host list');
  child.stdout.write('ening at ws://127.0.0.1:41771/workspace\n');

  await ready;
});

test('host readiness fails with captured diagnostics when the child exits early', async () => {
  const child = createChild();
  const ready = waitForHostReady(child, { timeoutMs: 1_000 });

  child.stderr.write('Address already in use');
  child.exitCode = 1;
  child.emit('exit', 1, null);

  await assert.rejects(ready, /Address already in use/);
});

test('static server binds to loopback and serves only renderer assets', async (t) => {
  const root = await mkdtemp(path.join(os.tmpdir(), 'workspace-shell-'));
  const outside = path.join(root, '..', `outside-${path.basename(root)}.txt`);
  await mkdir(path.join(root, 'assets'));
  await writeFile(path.join(root, 'index.html'), '<main>workspace</main>');
  await writeFile(path.join(root, 'assets', 'app.js'), 'export default 1;');
  await writeFile(outside, 'secret');

  const server = await startStaticServer(root);
  t.after(async () => {
    await server.close();
    await rm(root, { recursive: true, force: true });
    await rm(outside, { force: true });
  });

  assert.match(server.origin, /^http:\/\/127\.0\.0\.1:\d+$/);

  const index = await request(server.origin, '/');
  assert.equal(index.statusCode, 200);
  assert.equal(index.body, '<main>workspace</main>');
  assert.match(index.headers['content-security-policy'], /ws:\/\/127\.0\.0\.1:41771/);

  const script = await request(server.origin, '/assets/app.js');
  assert.equal(script.statusCode, 200);
  assert.equal(script.headers['content-type'], 'text/javascript; charset=utf-8');

  const traversal = await request(
    server.origin,
    `/%2e%2e%2f${encodeURIComponent(path.basename(outside))}`);
  assert.equal(traversal.statusCode, 403);
  assert.notEqual(traversal.body, 'secret');

  const missingAsset = await request(server.origin, '/assets/missing.js', {
    Accept: 'text/html',
  });
  assert.equal(missingAsset.statusCode, 404);

  const routeFallback = await request(server.origin, '/workspace/welcome', {
    Accept: 'text/html',
  });
  assert.equal(routeFallback.statusCode, 200);
  assert.equal(routeFallback.body, '<main>workspace</main>');
});

function createChild() {
  const child = new EventEmitter();
  child.stdout = new PassThrough();
  child.stderr = new PassThrough();
  child.exitCode = null;
  return child;
}

function request(origin, requestPath, headers = {}) {
  return new Promise((resolve, reject) => {
    const outgoing = http.get(`${origin}${requestPath}`, { headers }, (response) => {
      const chunks = [];
      response.on('data', (chunk) => chunks.push(chunk));
      response.on('end', () => resolve({
        statusCode: response.statusCode,
        headers: response.headers,
        body: Buffer.concat(chunks).toString('utf8'),
      }));
    });
    outgoing.on('error', reject);
  });
}
