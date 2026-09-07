import type { EventEnvelope, ProtocolEnvelope, SnapshotEnvelope } from '@workspace/protocol';
import { PROTOCOL_VERSION } from '@workspace/protocol';
import type { PresentationState, WorkspaceEntity } from '@workspace/world-schema';

type PresentationUpdate = {
  entityId: string;
  presentation: PresentationState;
};

export class WorldReplica {
  readonly #entities = new Map<string, WorkspaceEntity>();

  get entities(): readonly WorkspaceEntity[] {
    return [...this.#entities.values()];
  }

  apply(envelope: ProtocolEnvelope): void {
    if (envelope.protocol !== PROTOCOL_VERSION) {
      throw new Error(`Unsupported workspace protocol version: ${String(envelope.protocol)}`);
    }

    if (envelope.type === 'snapshot') {
      this.#applySnapshot(envelope);
      return;
    }

    if (envelope.type === 'event') {
      this.#applyEvent(envelope);
    }
  }

  #applySnapshot(envelope: SnapshotEnvelope): void {
    this.#entities.clear();
    for (const entity of envelope.entities as WorkspaceEntity[]) {
      this.#entities.set(entity.id, entity);
    }
  }

  #applyEvent(envelope: EventEnvelope): void {
    if (envelope.event === 'ENTITY_REMOVED') {
      const { entityId } = envelope.payload as { entityId: string };
      this.#entities.delete(entityId);
      return;
    }

    if (envelope.event === 'ENTITY_CREATED' || envelope.event === 'ENTITY_UPDATED') {
      const entity = envelope.payload as WorkspaceEntity;
      this.#entities.set(entity.id, entity);
      return;
    }

    if (envelope.event === 'PRESENTATION_UPDATED') {
      const update = envelope.payload as PresentationUpdate;
      const entity = this.#entities.get(update.entityId);
      if (entity) {
        this.#entities.set(update.entityId, { ...entity, presentation: update.presentation });
      }
    }
  }
}
