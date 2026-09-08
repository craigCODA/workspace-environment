import type { ProtocolEnvelope } from '@workspace/protocol';
import type { WorkspaceEntity } from '@workspace/world-schema';
import { WorldReplica } from './WorldReplica.ts';

export interface SceneReplicaTarget {
  upsert(entity: WorkspaceEntity): void;
  remove(entityId: string): void;
}

export class SceneReplicaSynchronizer {
  readonly #renderedIds = new Set<string>();
  readonly #replica: WorldReplica;
  readonly #scene: SceneReplicaTarget;

  constructor(
    replica: WorldReplica,
    scene: SceneReplicaTarget,
  ) {
    this.#replica = replica;
    this.#scene = scene;
  }

  apply(envelope: ProtocolEnvelope): void {
    if (envelope.type !== 'snapshot' && envelope.type !== 'event') return;

    this.#replica.apply(envelope);
    const entities = this.#replica.entities;
    const currentIds = new Set(entities.map((entity) => entity.id));

    for (const entityId of this.#renderedIds) {
      if (currentIds.has(entityId)) continue;
      this.#scene.remove(entityId);
      this.#renderedIds.delete(entityId);
    }

    for (const entity of entities) {
      this.#scene.upsert(entity);
      this.#renderedIds.add(entity.id);
    }
  }
}
