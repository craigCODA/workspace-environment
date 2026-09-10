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
  const position = record(source.position);
  const rotation = record(source.rotation);
  const size = record(source.size);
  if (!position || !rotation || !size) throw new Error('presentation is invalid.');
  hasOnlyKeys(source, new Set(['position', 'rotation', 'size', 'parentPresentationId', 'representation']));
  hasOnlyKeys(position, new Set(['x', 'y', 'z']));
  hasOnlyKeys(rotation, new Set(['x', 'y', 'z', 'w']));
  hasOnlyKeys(size, new Set(['x', 'y', 'z']));
  return {
    position: { x: finite(position.x, 'presentation.position.x'), y: finite(position.y, 'presentation.position.y'), z: finite(position.z, 'presentation.position.z') },
    rotation: { x: finite(rotation.x, 'presentation.rotation.x'), y: finite(rotation.y, 'presentation.rotation.y'), z: finite(rotation.z, 'presentation.rotation.z'), w: finite(rotation.w, 'presentation.rotation.w') },
    size: { x: finite(size.x, 'presentation.size.x'), y: finite(size.y, 'presentation.size.y'), z: finite(size.z, 'presentation.size.z') },
    ...(source.parentPresentationId === undefined ? {} : { parentPresentationId: text(source.parentPresentationId, 'presentation.parentPresentationId') }),
    ...(source.representation === undefined ? {} : { representation: text(source.representation, 'presentation.representation') }),
  };
}

function absoluteWindowsPath(value: unknown, label: string): string {
  const path = text(value, label);
  if (!/^(?:[a-zA-Z]:[\\/]|\\\\)/.test(path)) {
    throw new Error(`${label} must be an absolute Windows path.`);
  }
  return path;
}

function launchPolicy(value: unknown): 'reuseOrLaunch' | 'newInstance' {
  if (value !== 'reuseOrLaunch' && value !== 'newInstance') throw new Error('launchPolicy is invalid.');
  return value;
}

function zero(value: number): number {
  return Object.is(value, -0) ? 0 : value;
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
      if (!id || id.trim().length === 0) throw new Error('A nonblank request id is required.');
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
      return { id, ok: false, error: 'workspace_command_failed' };
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
      payload.launchPolicy = launchPolicy(args.launchPolicy);
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
      'application.search': ['query', 'limit'],
      'application.profile.list': [],
      'application.profile.save': ['id', 'displayName', 'applicationId', 'arguments', 'workingDirectory', 'launchPolicy', 'preferredSurfaceId', 'preferredPresentation'],
      'application.profile.delete': ['profileId'],
      'application.close': ['windowEntityId', 'approvalSource'],
      'application.restart': ['windowEntityId', 'profileId', 'approvalSource'],
      'surface.bindWindow': ['surfaceEntityId', 'windowEntityId', 'replaceOccupied'],
    };
    hasOnlyKeys(args, new Set(allowed[command] ?? []));
    if (command === 'application.search') {
      text(args.query, 'query');
      const limit = args.limit;
      if (limit !== undefined && (typeof limit !== 'number' || !Number.isInteger(limit) || limit < 1 || limit > 10)) {
        throw new Error('limit must be an integer from 1 through 10.');
      }
    }
    if (command === 'application.profile.delete') text(args.profileId, 'profileId');
    if (command === 'application.close') text(args.windowEntityId, 'windowEntityId');
    if (command === 'application.restart') {
      if ((args.windowEntityId === undefined) === (args.profileId === undefined)) {
        throw new Error('application.restart requires exactly one windowEntityId or profileId.');
      }
      if (args.windowEntityId !== undefined) text(args.windowEntityId, 'windowEntityId');
      if (args.profileId !== undefined) text(args.profileId, 'profileId');
    }
    if (command === 'application.profile.save') this.#validateProfile(args);
    if (command === 'surface.bindWindow') {
      const surfaceId = text(args.surfaceEntityId, 'surfaceEntityId');
      if (!this.#knownSurfaceIds().has(surfaceId)) throw new Error(`Unknown display surface: ${surfaceId}.`);
      text(args.windowEntityId, 'windowEntityId');
    }
    return { ...args };
  }

  #validateProfile(args: Record<string, unknown>): void {
    text(args.id, 'id');
    text(args.displayName, 'displayName');
    text(args.applicationId, 'applicationId');
    if (!Array.isArray(args.arguments) || !args.arguments.every((argument) => typeof argument === 'string')) {
      throw new Error('arguments must be a string array.');
    }
    if (args.workingDirectory !== undefined && args.workingDirectory !== null) {
      absoluteWindowsPath(args.workingDirectory, 'workingDirectory');
    }
    launchPolicy(args.launchPolicy);
    if (args.preferredSurfaceId !== undefined && args.preferredSurfaceId !== null) {
      const surfaceId = text(args.preferredSurfaceId, 'preferredSurfaceId');
      if (!this.#knownSurfaceIds().has(surfaceId)) throw new Error(`Unknown display surface: ${surfaceId}.`);
    }
    if (args.preferredPresentation !== undefined && args.preferredPresentation !== null) {
      presentation(args.preferredPresentation);
    }
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
    const forward = {
      x: -Math.sin(camera.yaw) * Math.cos(camera.pitch),
      y: Math.sin(camera.pitch),
      z: -Math.cos(camera.yaw) * Math.cos(camera.pitch),
    };
    const base = {
      x: camera.position.x + forward.x * 3,
      y: camera.position.y + forward.y * 3,
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
    const halfPitch = camera.pitch / 2;
    return {
      position: { x: base.x + offset, y: base.y, z: base.z + offset },
      rotation: {
        x: Math.cos(halfYaw) * Math.sin(halfPitch),
        y: Math.sin(halfYaw) * Math.cos(halfPitch),
        z: zero(-Math.sin(halfYaw) * Math.sin(halfPitch)),
        w: Math.cos(halfYaw) * Math.cos(halfPitch),
      },
      size: { ...CAMERA_PRESENTATION_SIZE },
    };
  }
}
