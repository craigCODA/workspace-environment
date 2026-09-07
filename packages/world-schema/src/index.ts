export type Vec3 = {
  x: number;
  y: number;
  z: number;
};

export type Quaternion = {
  x: number;
  y: number;
  z: number;
  w: number;
};

export type PresentationState = {
  position: Vec3;
  rotation: Quaternion;
  size: Vec3;
  parentPresentationId?: string;
  representation?: string;
};

export type HostBinding = {
  type: 'application' | 'file' | 'folder' | 'project' | 'device';
  locator: string;
  launchDescriptor?: string;
};

export type Relationship = {
  type: string;
  targetId: string;
};

export type WorkspaceEntity = {
  id: string;
  kind: string;
  name: string;
  properties: Record<string, unknown>;
  relationships: Relationship[];
  capabilities: string[];
  hostBinding?: HostBinding;
  presentation: PresentationState;
};

export const DEFAULT_PRESENTATION: PresentationState = {
  position: { x: 0, y: 0, z: 0 },
  rotation: { x: 0, y: 0, z: 0, w: 1 },
  size: { x: 1, y: 1, z: 1 },
};

export function createEntityId(kind: string, stableKey: string): string {
  const normalizedKind = normalizeSegment(kind);
  const normalizedKey = stableKey.normalize('NFKC').trim().toLocaleLowerCase('en-US');
  return `${normalizedKind}:${encodeURIComponent(normalizedKey)}`;
}

function normalizeSegment(value: string): string {
  return value.normalize('NFKC').trim().toLocaleLowerCase('en-US').replace(/\s+/g, '-');
}
