import type { PresentationState } from '@workspace/world-schema';
import type { CameraPose } from './CameraNavigator.ts';
import type { SurfaceCommandClient } from '../surfaces/SurfaceStream.ts';

const ALLOWED_WORKSPACE_COMMANDS = new Set([
  'application.search',
  'application.profile.list',
  'application.profile.save',
  'application.profile.delete',
  'application.open',
  'application.close',
  'application.restart',
  'window.focus',
  'surface.bindWindow',
]);

const OPEN_KEYS = new Set([
  'applicationId', 'profileId', 'launchPolicy', 'targetSurfaceId', 'presentation', 'replaceOccupied',
]);
const CAMERA_PRESENTATION_SIZE = { x: 3.2, y: 1.8, z: 0.035 };

export type WorkspaceCommandResult = Readonly<{
  id: string | null;
  ok: boolean;
  payload?: unknown;
  error?: string;
}>;

export type WorkspaceCommandContext = Readonly<{
  surfaceIds?: () => readonly string[];
  cameraPose?: () => CameraPose;
  occupiedPresentations?: () => readonly Pick<PresentationState, 'position'>[];
}>;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

function text(value: unknown, label: string): string {
  if (typeof value !== 'string' || value.trim().length === 0) throw new Error(`${label} is required.`);
  return value;
}

function hasOnlyKeys(value: Record<string, unknown>, allowed: ReadonlySet<string>): void {
  for (const key of Object.keys(value)) {
    if (!allowed.has(key)) throw new Error(`Unsupported argument: ${key}.`);
  }
}

function finite(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error(`${label} must be finite.`);
  return value;
}

function presentation(value: unknown): PresentationState {
  const source = record(value);
  if (!source) throw new Error('presentation is required.');
  hasOnlyKeys(source, new Set(['position', 'rotation', 'size']));
  const position = record(source.position);
  const rotation = record(source.rotation);
  const size = record(source.size);
  if (!position || !rotation || !size) throw new Error('presentation is invalid.');
  hasOnlyKeys(position, new Set(['x', 'y', 'z']));
  hasOnlyKeys(rotation, new Set(['x', 'y', 'z', 'w']));
  hasOnlyKeys(size, new Set(['x', 'y', 'z']));
  return {
    position: { x: finite(position.x, 'presentation.position.x'), y: finite(position.y, 'presentation.position.y'), z: finite(position.z, 'presentation.position.z') },
    rotation: { x: finite(rotation.x, 'presentation.rotation.x'), y: finite(rotation.y, 'presentation.rotation.y'), z: finite(rotation.z, 'presentation.rotation.z'), w: finite(rotation.w, 'presentation.rotation.w') },
    size: { x: finite(size.x, 'presentation.size.x'), y: finite(size.y, 'presentation.size.y'), z: finite(size.z, 'presentation.size.z') },
  };
}

export class WorkspaceCommandController {
  readonly #client: SurfaceCommandClient;
  readonly #selectedSurfaceId: () => string | null;
  readonly #context: WorkspaceCommandContext;

  constructor(
    client: SurfaceCommandClient,
    selectedSurfaceId: () => string | null,
    context: WorkspaceCommandContext = {},
  ) {
    this.#client = client;
    this.#selectedSurfaceId = selectedSurfaceId;
    this.#context = context;
  }

  async handle(value: unknown): Promise<WorkspaceCommandResult> {
    const source = record(value);
    const id = typeof source?.id === 'string' ? source.id : null;
    try {
      const command = text(source?.command, 'command');
      if (!ALLOWED_WORKSPACE_COMMANDS.has(command)) {
        throw new Error(`Unsupported workspace command: ${command}.`);
      }
      const args = record(source?.args);
      if (!args) throw new Error('Command args must be an object.');
      if (command === 'window.focus') {
        return {
          id,
          ok: true,
          payload: await this.#client.sendCommand('window.focus', this.#windowFocusTarget(args)),
        };
      }
      const payload = command === 'application.open' ? this.#openPayload(args) : this.#payload(command, args);
      return { id, ok: true, payload: await this.#client.sendCommand(command, undefined, payload) };
    } catch (error) {
      return { id, ok: false, error: error instanceof Error ? error.message : String(error) };
    }
  }

  #openPayload(args: Record<string, unknown>): Record<string, unknown> {
    hasOnlyKeys(args, OPEN_KEYS);
    const applicationId = args.applicationId;
    const profileId = args.profileId;
    if ((applicationId === undefined) === (profileId === undefined)) {
      throw new Error('application.open requires exactly one applicationId or profileId.');
    }
    const payload: Record<string, unknown> = {
      ...(applicationId === undefined ? { profileId: text(profileId, 'profileId') } : { applicationId: text(applicationId, 'applicationId') }),
    };
    if (args.launchPolicy !== undefined) {
      if (args.launchPolicy !== 'reuseOrLaunch' && args.launchPolicy !== 'newInstance') {
        throw new Error('launchPolicy is invalid.');
      }
      payload.launchPolicy = args.launchPolicy;
    }
    if (args.replaceOccupied !== undefined) {
      if (typeof args.replaceOccupied !== 'boolean') throw new Error('replaceOccupied must be boolean.');
      payload.replaceOccupied = args.replaceOccupied;
    }

    const target = args.targetSurfaceId === undefined ? null : this.#resolveSurfaceId(args.targetSurfaceId);
    if (target) payload.targetSurfaceId = target;
    if (args.presentation !== undefined) {
      if (target) throw new Error('presentation is only valid when creating a new surface.');
      payload.presentation = presentation(args.presentation);
    } else if (!target) {
      payload.presentation = this.#newSurfacePresentation();
    }
    return payload;
  }

  #payload(command: string, args: Record<string, unknown>): Record<string, unknown> {
    const allowed: Record<string, readonly string[]> = {
      'application.search': ['query'],
      'application.profile.list': [],
      'application.profile.save': ['id', 'displayName', 'applicationId', 'arguments', 'workingDirectory', 'launchPolicy', 'preferredSurfaceId', 'preferredPresentation'],
      'application.profile.delete': ['profileId'],
      'application.close': ['windowEntityId', 'approvalSource'],
      'application.restart': ['windowEntityId', 'approvalSource'],
      'surface.bindWindow': ['surfaceEntityId', 'windowEntityId', 'replaceOccupied'],
    };
    hasOnlyKeys(args, new Set(allowed[command] ?? []));
    if (command === 'application.search') text(args.query, 'query');
    if (command === 'application.profile.delete') text(args.profileId, 'profileId');
    if (command === 'application.close' || command === 'application.restart') text(args.windowEntityId, 'windowEntityId');
    if (command === 'surface.bindWindow') {
      const surfaceId = text(args.surfaceEntityId, 'surfaceEntityId');
      if (!this.#knownSurfaceIds().has(surfaceId)) throw new Error(`Unknown display surface: ${surfaceId}.`);
      text(args.windowEntityId, 'windowEntityId');
    }
    return { ...args };
  }

  #windowFocusTarget(args: Record<string, unknown>): string {
    hasOnlyKeys(args, new Set(['windowEntityId']));
    return text(args.windowEntityId, 'windowEntityId');
  }

  #resolveSurfaceId(value: unknown): string {
    const requested = text(value, 'targetSurfaceId');
    const surfaceId = requested === '$selected' ? this.#selectedSurfaceId() : requested;
    if (!surfaceId || !this.#knownSurfaceIds().has(surfaceId)) {
      throw new Error(`Unknown display surface: ${requested}.`);
    }
    return surfaceId;
  }

  #knownSurfaceIds(): Set<string> {
    return new Set(this.#context.surfaceIds?.() ?? []);
  }

  #newSurfacePresentation(): PresentationState {
    const camera = this.#context.cameraPose?.() ?? { position: { x: 0, y: 1.65, z: 4 }, yaw: 0, pitch: 0 };
    const forward = { x: -Math.sin(camera.yaw), z: -Math.cos(camera.yaw) };
    const base = {
      x: camera.position.x + forward.x * 3,
      y: camera.position.y,
      z: camera.position.z + forward.z * 3,
    };
    const occupied = this.#context.occupiedPresentations?.() ?? [];
    let offset = 0;
    while (occupied.some((candidate) => candidate.position.x === base.x + offset
      && candidate.position.y === base.y
      && candidate.position.z === base.z + offset)) {
      offset += 0.25;
    }
    const halfYaw = camera.yaw / 2;
    return {
      position: { x: base.x + offset, y: base.y, z: base.z + offset },
      rotation: { x: 0, y: Math.sin(halfYaw), z: 0, w: Math.cos(halfYaw) },
      size: { ...CAMERA_PRESENTATION_SIZE },
    };
  }
}
