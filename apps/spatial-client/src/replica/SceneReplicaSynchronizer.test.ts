import assert from 'node:assert/strict';
import test from 'node:test';
import type { WorkspaceEntity } from '@workspace/world-schema';
import { WorldReplica } from './WorldReplica.ts';
import { SceneReplicaSynchronizer } from './SceneReplicaSynchronizer.ts';

const entity: WorkspaceEntity = {
  id: 'pc.application:notepad',
  kind: 'pc.application',
  name: 'Notepad',
  properties: {},
  relationships: [],
  capabilities: ['open'],
  hostBinding: { type: 'application', locator: 'Notepad' },
  presentation: {
    position: { x: 0, y: 0, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 1, y: 1, z: 1 },
  },
};

const presentation = entity.presentation;

function windowEntity(id: string): WorkspaceEntity {
  return {
    ...entity,
    id,
    kind: 'pc.window',
    relationships: [],
    presentation,
  };
}

function surfaceEntity(id: string, windowId: string | null): WorkspaceEntity {
  return {
    ...entity,
    id,
    kind: 'spatial.surface',
    relationships: windowId ? [{ type: 'displays', targetId: windowId }] : [],
    capabilities: ['select'],
    presentation,
  };
}

test('synchronizes snapshot replacements into scene upserts and removals', () => {
  const upserted: string[] = [];
  const removed: string[] = [];
  const synchronizer = new SceneReplicaSynchronizer(new WorldReplica(), {
    upsert(value) {
      upserted.push(value.id);
    },
    remove(entityId) {
      removed.push(entityId);
    },
  });

  synchronizer.apply({ protocol: 1, type: 'snapshot', entities: [entity] });
  synchronizer.apply({ protocol: 1, type: 'snapshot', entities: [] });

  assert.deepEqual(upserted, [entity.id]);
  assert.deepEqual(removed, [entity.id]);
});

test('synchronizes entity lifecycle events into scene upserts and removals', () => {
  const upserted: string[] = [];
  const removed: string[] = [];
  const synchronizer = new SceneReplicaSynchronizer(new WorldReplica(), {
    upsert(value) {
      upserted.push(value.id);
    },
    remove(entityId) {
      removed.push(entityId);
    },
  });

  synchronizer.apply({
    protocol: 1,
    type: 'event',
    event: 'ENTITY_CREATED',
    payload: entity,
  });
  synchronizer.apply({
    protocol: 1,
    type: 'event',
    event: 'ENTITY_REMOVED',
    payload: { entityId: entity.id },
  });

  assert.deepEqual(upserted, [entity.id]);
  assert.deepEqual(removed, [entity.id]);
});

test('renders a bound spatial surface once and suppresses its legacy window regardless of event order', () => {
  const upserted: string[] = [];
  const removed: string[] = [];
  const synchronizer = new SceneReplicaSynchronizer(new WorldReplica(), {
    upsert(value) { upserted.push(value.id); },
    remove(entityId) { removed.push(entityId); },
  });
  const window = windowEntity('pc.window:notepad');
  const surface = surfaceEntity('spatial.surface:right', window.id);

  synchronizer.apply({ protocol: 1, type: 'event', event: 'ENTITY_CREATED', payload: window });
  synchronizer.apply({ protocol: 1, type: 'event', event: 'ENTITY_CREATED', payload: surface });

  assert.deepEqual(upserted, [window.id, surface.id]);
  assert.deepEqual(removed, [window.id]);
});

test('restores legacy window rendering when a surface unbinds it', () => {
  const upserted: string[] = [];
  const removed: string[] = [];
  const synchronizer = new SceneReplicaSynchronizer(new WorldReplica(), {
    upsert(value) { upserted.push(value.id); },
    remove(entityId) { removed.push(entityId); },
  });
  const window = windowEntity('pc.window:notepad');
  const surface = surfaceEntity('spatial.surface:right', window.id);
  synchronizer.apply({ protocol: 1, type: 'snapshot', entities: [window, surface] });
  synchronizer.apply({
    protocol: 1,
    type: 'event',
    event: 'ENTITY_UPDATED',
    payload: surfaceEntity(surface.id, null),
  });

  assert.deepEqual(upserted, [surface.id, window.id, surface.id]);
  assert.deepEqual(removed, []);
});
