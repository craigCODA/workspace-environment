import { existsSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';

test('protocol envelopes require explicit protocol version 1', async () => {
  const modulePath = fileURLToPath(new URL('./index.ts', import.meta.url));
  assert.equal(existsSync(modulePath), true, 'protocol implementation should exist');
  if (!existsSync(modulePath)) return;

  const { PROTOCOL_VERSION, isProtocolEnvelope } = await import('./index.ts');
  assert.equal(PROTOCOL_VERSION, 1);
  assert.equal(isProtocolEnvelope({ protocol: 1, type: 'event', event: 'ENTITY_CREATED', payload: {} }), true);
  assert.equal(isProtocolEnvelope({ type: 'event', event: 'ENTITY_CREATED', payload: {} }), false);
});
