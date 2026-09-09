export type Vec3 = Readonly<{ x: number; y: number; z: number }>;

export type CameraPose = Readonly<{
  position: Vec3;
  yaw: number;
  pitch: number;
}>;

export interface CameraPoseTarget {
  getCameraPose(): CameraPose;
  setCameraPose(pose: CameraPose): void;
}

export type NavigationOptions = Readonly<{
  mode?: 'glide' | 'jump';
  durationMs?: number;
}>;

const MAX_PITCH = Math.PI * 0.42;
const MAX_DURATION_MS = 30_000;

function finite(value: number, label: string): number {
  if (!Number.isFinite(value)) throw new Error(`${label} must be finite.`);
  return value;
}

function normalizePose(pose: CameraPose): CameraPose {
  return {
    position: {
      x: finite(pose.position.x, 'Camera x'),
      y: finite(pose.position.y, 'Camera y'),
      z: finite(pose.position.z, 'Camera z'),
    },
    yaw: finite(pose.yaw, 'Camera yaw'),
    pitch: Math.max(-MAX_PITCH, Math.min(MAX_PITCH, finite(pose.pitch, 'Camera pitch'))),
  };
}

function interpolate(start: number, end: number, amount: number): number {
  return start + (end - start) * amount;
}

function easeInOutCubic(amount: number): number {
  return amount < 0.5
    ? 4 * amount * amount * amount
    : 1 - Math.pow(-2 * amount + 2, 3) / 2;
}

type ActiveNavigation = {
  start: CameraPose;
  target: CameraPose;
  elapsedMs: number;
  durationMs: number;
};

export class CameraNavigator {
  readonly #target: CameraPoseTarget;
  #active: ActiveNavigation | null = null;
  #lastCancellation: string | null = null;

  constructor(target: CameraPoseTarget) {
    this.#target = target;
  }

  get isNavigating(): boolean {
    return this.#active !== null;
  }

  get lastCancellation(): string | null {
    return this.#lastCancellation;
  }

  navigate(target: CameraPose, options: NavigationOptions = {}): void {
    const normalized = normalizePose(target);
    const duration = finite(options.durationMs ?? 1_200, 'Navigation duration');
    const durationMs = Math.max(0, Math.min(MAX_DURATION_MS, duration));
    this.#lastCancellation = null;

    if (options.mode === 'jump' || durationMs === 0) {
      this.#active = null;
      this.#target.setCameraPose(normalized);
      return;
    }

    this.#active = {
      start: normalizePose(this.#target.getCameraPose()),
      target: normalized,
      elapsedMs: 0,
      durationMs,
    };
  }

  cancel(reason = 'cancelled'): boolean {
    if (!this.#active) return false;
    this.#active = null;
    this.#lastCancellation = reason;
    return true;
  }

  tick(deltaMs: number): void {
    const active = this.#active;
    if (!active) return;

    active.elapsedMs = Math.min(
      active.durationMs,
      active.elapsedMs + Math.max(0, finite(deltaMs, 'Frame delta')),
    );
    const progress = active.elapsedMs / active.durationMs;
    const amount = easeInOutCubic(progress);
    this.#target.setCameraPose({
      position: {
        x: interpolate(active.start.position.x, active.target.position.x, amount),
        y: interpolate(active.start.position.y, active.target.position.y, amount),
        z: interpolate(active.start.position.z, active.target.position.z, amount),
      },
      yaw: interpolate(active.start.yaw, active.target.yaw, amount),
      pitch: interpolate(active.start.pitch, active.target.pitch, amount),
    });

    if (progress >= 1) {
      this.#target.setCameraPose(active.target);
      this.#active = null;
    }
  }
}
