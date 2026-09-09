import { existsSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

test('durable entity ids are stable for the same semantic key', async () => {
  const modulePath = fileURLToPath(new URL('./index.ts', import.meta.url));
  assert.equal(existsSync(modulePath), true, 'world schema implementation should exist');
  if (!existsSync(modulePath)) return;

  const { createEntityId } = await import('./index.ts');
  const first = createEntityId('pc.application', 'Microsoft Edge');
  const second = createEntityId('pc.application', 'Microsoft Edge');
  assert.equal(first, second);
  assert.equal(first.includes('HWND'), false);
});
