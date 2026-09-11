import assert from 'node:assert/strict';
import test from 'node:test';
import type { WorkspaceEntity } from '@workspace/world-schema';
import { WorldReplica } from './WorldReplica.ts';

const entity: WorkspaceEntity = {
  id: 'pc.application:notepad',
  kind: 'pc.application',
  name: 'Notepad',
  properties: {},
  relationships: [],
  capabilities: ['open', 'focus'],
  hostBinding: { type: 'application', locator: 'Notepad' },
  presentation: {
    position: { x: 0, y: 0, z: 0 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 1, y: 1, z: 1 },
  },
};

test('rejects an unsupported protocol version', () => {
  const replica = new WorldReplica();

  assert.throws(
    () => replica.apply({ protocol: 99, type: 'snapshot', entities: [] } as never),
    /protocol/i,
  );
});

test('snapshot replaces the local read model with host entities', () => {
  const replica = new WorldReplica();

  replica.apply({ protocol: 1, type: 'snapshot', entities: [entity] });

  assert.deepEqual(replica.entities, [entity]);
});

test('presentation events update the replica without changing semantic identity', () => {
  const replica = new WorldReplica();
  replica.apply({ protocol: 1, type: 'snapshot', entities: [entity] });

  replica.apply({
    protocol: 1,
    type: 'event',
    event: 'PRESENTATION_UPDATED',
    payload: {
      entityId: entity.id,
      presentation: {
        ...entity.presentation,
        position: { x: 2, y: 1, z: -3 },
      },
    },
  });

  assert.equal(replica.entities[0]?.id, entity.id);
  assert.deepEqual(replica.entities[0]?.presentation.position, { x: 2, y: 1, z: -3 });
});
