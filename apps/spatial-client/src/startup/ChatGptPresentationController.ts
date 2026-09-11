import type { PresentationState } from '@workspace/world-schema';
import type { CameraPose } from '../navigation/CameraNavigator.ts';

export type ChatGptPresentationMode = 'spatial' | 'docked';

export type ChatGptPresentationControllerOptions = Readonly<{
  cameraPose(): CameraPose;
  presentationFor(surfaceEntityId: string): PresentationState | null;
  preview(surfaceEntityId: string, presentation: PresentationState | null): void;
  commit(surfaceEntityId: string, presentation: PresentationState): Promise<void>;
}>;

function zero(value: number): number {
  return Object.is(value, -0) ? 0 : value;
}

export function dockedPresentationFor(camera: CameraPose): PresentationState {
  const depth = 2.4;
  const rightOffset = 0.95;
  const cosYaw = Math.cos(camera.yaw);
  const sinYaw = Math.sin(camera.yaw);
  const cosPitch = Math.cos(camera.pitch);
  const sinPitch = Math.sin(camera.pitch);
  const forward = {
    x: -sinYaw * cosPitch,
    y: sinPitch,
    z: -cosYaw * cosPitch,
  };
  const right = { x: cosYaw, y: 0, z: -sinYaw };
  const position = {
    x: camera.position.x + forward.x * depth + right.x * rightOffset,
    y: camera.position.y + forward.y * depth,
    z: camera.position.z + forward.z * depth + right.z * rightOffset,
  };
  const halfYaw = camera.yaw / 2;
  const halfPitch = camera.pitch / 2;
  return {
    position,
    rotation: {
      x: zero(Math.cos(halfYaw) * Math.sin(halfPitch)),
      y: zero(Math.sin(halfYaw) * Math.cos(halfPitch)),
      z: zero(-Math.sin(halfYaw) * Math.sin(halfPitch)),
      w: zero(Math.cos(halfYaw) * Math.cos(halfPitch)),
    },
    size: { x: 1.05, y: 1.8, z: 0.035 },
  };
}

export class ChatGptPresentationController {
  readonly #options: ChatGptPresentationControllerOptions;
  #surfaceEntityId: string | null = null;
  #mode: ChatGptPresentationMode = 'spatial';

  constructor(options: ChatGptPresentationControllerOptions) {
    this.#options = options;
  }

  get mode(): ChatGptPresentationMode {
    return this.#mode;
  }

  attach(surfaceEntityId: string): void {
    this.#surfaceEntityId = surfaceEntityId;
  }

  dock(): void {
    if (!this.#surfaceEntityId) return;
    this.#mode = 'docked';
    this.tick();
  }

  undock(): void {
    if (!this.#surfaceEntityId) return;
    this.#mode = 'spatial';
    this.#options.preview(this.#surfaceEntityId, null);
  }

  tick(): void {
    if (this.#mode !== 'docked' || !this.#surfaceEntityId) return;
    this.#options.preview(
      this.#surfaceEntityId,
      dockedPresentationFor(this.#options.cameraPose()),
    );
  }
}
