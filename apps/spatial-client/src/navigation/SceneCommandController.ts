import type { PresentationState, Relationship } from '@workspace/world-schema';
import {
  CameraNavigator,
  type CameraPose,
  type CameraPoseTarget,
  type NavigationOptions,
  type Vec3,
} from './CameraNavigator.ts';

export type SceneSnapshot = Readonly<{
  camera: CameraPose;
  entities: readonly Readonly<{
    id: string;
    kind: string;
    name: string;
    presentation: PresentationState;
    relationships: readonly Relationship[];
    selected: boolean;
  }>[];
}>;

export interface SceneControlTarget extends CameraPoseTarget {
  lookBy(deltaX: number, deltaY: number): void;
  move(forward: number, right: number): void;
  snapshot(selectedEntityId?: string | null): SceneSnapshot;
  presentationFor(entityId: string): PresentationState | null;
  commitPresentation(entityId: string, presentation: PresentationState): Promise<void>;
}

export type SceneCommandResult = Readonly<{
  id: string | null;
  ok: boolean;
  payload?: unknown;
  error?: string;
}>;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null ? value as Record<string, unknown> : null;
}

function finite(value: unknown, label: string): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new Error(`${label} must be finite.`);
  }
  return value;
}

function vec3(value: unknown, label: string): Vec3 {
  const source = record(value);
  if (!source) throw new Error(`${label} is required.`);
  return {
    x: finite(source.x, `${label}.x`),
    y: finite(source.y, `${label}.y`),
    z: finite(source.z, `${label}.z`),
  };
}

function pose(value: unknown): CameraPose {
  const source = record(value);
  if (!source) throw new Error('Camera pose is required.');
  return {
    position: vec3(source.position, 'Camera position'),
    yaw: finite(source.yaw, 'Camera yaw'),
    pitch: finite(source.pitch, 'Camera pitch'),
  };
}

function navigationOptions(value: unknown): NavigationOptions {
  const source = record(value);
  if (!source) return {};
  const mode = source.mode;
  if (mode !== undefined && mode !== 'glide' && mode !== 'jump') {
    throw new Error('Navigation mode must be glide or jump.');
  }
  return {
    mode,
    durationMs: source.durationMs === undefined
      ? undefined
      : finite(source.durationMs, 'Navigation duration'),
  };
}

const DEFAULT_HOME: CameraPose = {
  position: { x: 0, y: 1.65, z: 4 },
  yaw: 0,
  pitch: 0,
};

export class SceneCommandController {
  readonly #target: SceneControlTarget;
  readonly #navigator: CameraNavigator;
  readonly #selection: () => string | null;
  readonly #home: CameraPose;

  constructor(
    target: SceneControlTarget,
    navigator: CameraNavigator,
    selection: () => string | null = () => null,
    home: CameraPose = DEFAULT_HOME,
  ) {
    this.#target = target;
    this.#navigator = navigator;
    this.#selection = selection;
    this.#home = home;
  }

  tick(deltaMs: number): void {
    this.#navigator.tick(deltaMs);
  }

  manualLook(deltaX: number, deltaY: number): void {
    this.#navigator.cancel('manual-input');
    this.#target.lookBy(deltaX, deltaY);
  }

  manualMove(forward: number, right: number): void {
    this.#navigator.cancel('manual-input');
    this.#target.move(forward, right);
  }

  cancel(reason = 'manual-input'): boolean {
    return this.#navigator.cancel(reason);
  }

  async handle(value: unknown): Promise<SceneCommandResult> {
    const source = record(value);
    const id = typeof source?.id === 'string' ? source.id : null;
    const command = typeof source?.command === 'string' ? source.command : '';

    try {
      const args = record(source?.args) ?? {};
      switch (command) {
        case 'scene.inspect':
          return { id, ok: true, payload: this.#target.snapshot(this.#selection()) };
        case 'camera.navigate': {
          this.#navigator.navigate(pose(args.pose), navigationOptions(args.options));
          return { id, ok: true, payload: { navigating: this.#navigator.isNavigating } };
        }
        case 'camera.focus': {
          const entityId = typeof args.entityId === 'string' ? args.entityId : '';
          const presentation = this.#target.presentationFor(entityId);
          if (!presentation) throw new Error(`Unknown scene entity: ${entityId || '(missing)'}`);
          const distance = args.distance === undefined ? 3 : finite(args.distance, 'Focus distance');
          if (distance <= 0) throw new Error('Focus distance must be positive.');
          const position = {
            x: presentation.position.x,
            y: presentation.position.y,
            z: presentation.position.z + distance,
          };
          const vertical = presentation.position.y - position.y;
          this.#navigator.navigate({
            position,
            yaw: 0,
            pitch: Math.atan2(vertical, distance),
          }, navigationOptions(args.options));
          return { id, ok: true, payload: { entityId, navigating: this.#navigator.isNavigating } };
        }
        case 'camera.stop':
          return { id, ok: true, payload: { cancelled: this.#navigator.cancel('command') } };
        case 'camera.return-home':
          this.#navigator.navigate(this.#home, navigationOptions(args.options));
          return { id, ok: true, payload: { navigating: this.#navigator.isNavigating } };
        case 'surface.move': {
          const entityId = typeof args.entityId === 'string' ? args.entityId : '';
          const current = this.#target.presentationFor(entityId);
          if (!current) throw new Error(`Unknown scene entity: ${entityId || '(missing)'}`);
          const next = { ...current, position: vec3(args.position, 'Surface position') };
          await this.#target.commitPresentation(entityId, next);
          return { id, ok: true, payload: { entityId, presentation: next } };
        }
        case 'surface.resize': {
          const entityId = typeof args.entityId === 'string' ? args.entityId : '';
          const current = this.#target.presentationFor(entityId);
          if (!current) throw new Error(`Unknown scene entity: ${entityId || '(missing)'}`);
          const size = vec3(args.size, 'Surface size');
          if (size.x <= 0 || size.y <= 0 || size.z <= 0) {
            throw new Error('Surface dimensions must be positive.');
          }
          const next = { ...current, size };
          await this.#target.commitPresentation(entityId, next);
          return { id, ok: true, payload: { entityId, presentation: next } };
        }
        default:
          return { id, ok: false, error: `Unsupported scene command: ${command || '(missing)'}` };
      }
    } catch (error) {
      return {
        id,
        ok: false,
        error: error instanceof Error ? error.message : String(error),
      };
    }
  }
}
