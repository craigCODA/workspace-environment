import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import path from 'node:path';
import { createRequire } from 'node:module';

import { resolveElectronProvisioning } from './electron-provisioning.mjs';

const require = createRequire(import.meta.url);
const packageDirectory = path.dirname(require.resolve('electron/package.json'));
const initial = resolveElectronProvisioning({
  packageDirectory,
  platform: process.platform,
  binaryExists: existsSync(path.join(
    packageDirectory,
    'dist',
    process.platform === 'win32' ? 'electron.exe' : 'electron')),
});

if (initial.installRequired) {
  const result = spawnSync(process.execPath, [initial.installerPath], {
    cwd: packageDirectory,
    env: process.env,
    stdio: 'inherit',
    windowsHide: true,
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`Electron provisioning exited with code ${result.status}.`);
  }
}

if (!existsSync(initial.executablePath)) {
  throw new Error(`Electron executable is missing after provisioning: ${initial.executablePath}`);
}
