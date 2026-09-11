import { spawnSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

import { createNpmInvocation } from './desktop-build-command.mjs';

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptDirectory, '..');
const stagingRoot = path.resolve(repoRoot, '.desktop-build');
const hostOutput = path.join(stagingRoot, 'host');
const rendererOutput = path.join(repoRoot, 'apps', 'spatial-client', 'dist');
const hostProject = path.join(
  repoRoot,
  'apps',
  'host-windows',
  'src',
  'Workspace.Host',
  'Workspace.Host.csproj');

if (process.platform !== 'win32') {
  throw new Error('Workspace Environment desktop packaging currently supports Windows only.');
}
if (path.dirname(stagingRoot) !== repoRoot || path.basename(stagingRoot) !== '.desktop-build') {
  throw new Error(`Refusing to clean unexpected staging path: ${stagingRoot}`);
}

rmSync(stagingRoot, { recursive: true, force: true });
mkdirSync(hostOutput, { recursive: true });

const npm = createNpmInvocation({
  nodeExecutable: process.execPath,
  npmCliPath: process.env.npm_execpath,
  args: [
    'run',
    'build',
    '--workspace',
    '@workspace/spatial-client',
  ],
});
run(npm.executable, npm.args);
run('dotnet', [
  'publish',
  hostProject,
  '--configuration',
  'Release',
  '--runtime',
  'win-x64',
  '--self-contained',
  'true',
  '--output',
  hostOutput,
  '/p:DebugType=None',
  '/p:DebugSymbols=false',
]);

requireOutput(path.join(rendererOutput, 'index.html'));
requireOutput(path.join(hostOutput, 'Workspace.Host.exe'));

function run(executable, args) {
  const result = spawnSync(executable, args, {
    cwd: repoRoot,
    env: process.env,
    stdio: 'inherit',
    windowsHide: true,
  });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${executable} exited with code ${result.status}.`);
  }
}

function requireOutput(filePath) {
  if (!existsSync(filePath)) {
    throw new Error(`Expected desktop build output is missing: ${filePath}`);
  }
}
