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
