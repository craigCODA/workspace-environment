export type RendererDescriptor = {
  kind: 'application-surface' | 'semantic-marker';
};

const APPLICATION_SURFACE: RendererDescriptor = Object.freeze({
  kind: 'application-surface',
});

const SEMANTIC_MARKER: RendererDescriptor = Object.freeze({
  kind: 'semantic-marker',
});

export class RendererRegistry {
  resolve(entityKind: string): RendererDescriptor {
    return entityKind === 'pc.window' ? APPLICATION_SURFACE : SEMANTIC_MARKER;
  }
}
