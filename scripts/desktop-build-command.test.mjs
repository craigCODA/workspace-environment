import assert from 'node:assert/strict';
import test from 'node:test';

import { createNpmInvocation } from './desktop-build-command.mjs';
import { resolveElectronProvisioning } from './electron-provisioning.mjs';

test('npm build invocation uses the Node executable instead of spawning npm.cmd', () => {
  const invocation = createNpmInvocation({
    nodeExecutable: 'C:\\Program Files\\nodejs\\node.exe',
    npmCliPath: 'C:\\Program Files\\nodejs\\node_modules\\npm\\bin\\npm-cli.js',
    args: ['run', 'build'],
  });

  assert.deepEqual(invocation, {
    executable: 'C:\\Program Files\\nodejs\\node.exe',
    args: [
      'C:\\Program Files\\nodejs\\node_modules\\npm\\bin\\npm-cli.js',
      'run',
      'build',
    ],
  });
});

test('npm build invocation rejects an unavailable npm CLI path', () => {
  assert.throws(
    () => createNpmInvocation({
      nodeExecutable: 'node.exe',
      npmCliPath: '',
      args: ['run', 'build'],
    }),
    /npm CLI path/);
});

test('Electron provisioning runs the package installer when the binary is absent', () => {
  const provisioning = resolveElectronProvisioning({
    packageDirectory: 'D:\\repo\\node_modules\\electron',
    platform: 'win32',
    binaryExists: false,
  });

  assert.deepEqual(provisioning, {
    installerPath: 'D:\\repo\\node_modules\\electron\\install.js',
    executablePath: 'D:\\repo\\node_modules\\electron\\dist\\electron.exe',
    installRequired: true,
  });
});

test('Electron provisioning leaves an existing binary intact', () => {
  const provisioning = resolveElectronProvisioning({
    packageDirectory: '/repo/node_modules/electron',
    platform: 'linux',
    binaryExists: true,
  });

  assert.equal(provisioning.installRequired, false);
  assert.equal(provisioning.executablePath, '/repo/node_modules/electron/dist/electron');
});
