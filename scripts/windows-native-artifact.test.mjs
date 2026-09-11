import assert from 'node:assert/strict';
import fs from 'node:fs';
import test from 'node:test';

const workflow = fs.readFileSync('.github/workflows/windows-ci.yml', 'utf8');

test('windows CI publishes a native Coda artifact separately from Electron fallback', () => {
  assert.match(workflow, /Stage native Windows bundle/);
  assert.match(workflow, /workspace-environment-native-coda/);
  assert.match(workflow, /\.native-package/);
  assert.match(workflow, /workspace-environment-electron-fallback/);
});
