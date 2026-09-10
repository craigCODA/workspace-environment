import { existsSync } from 'node:fs';
import { test } from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import type { WorkspaceEntity } from './index.ts';

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

test('resolves the displayed window only from a window or display-surface entity', async () => {
  const { displayedWindowId } = await import('./index.ts');
  const surface: WorkspaceEntity = {
    id: 'spatial.surface:right',
    kind: 'spatial.surface',
    name: 'Right',
    properties: {},
    relationships: [{ type: 'displays', targetId: 'pc.window:notepad' }],
    capabilities: ['select'],
    presentation: {
      position: { x: 0, y: 0, z: 0 },
      rotation: { x: 0, y: 0, z: 0, w: 1 },
      size: { x: 1, y: 1, z: 1 },
    },
  };

  assert.equal(displayedWindowId(surface), 'pc.window:notepad');
  assert.equal(displayedWindowId({ ...surface, relationships: [] }), null);
  assert.equal(displayedWindowId({ ...surface, id: 'pc.window:notepad', kind: 'pc.window' }), 'pc.window:notepad');
  assert.equal(displayedWindowId({ ...surface, kind: 'workspace.project' }), null);
});
