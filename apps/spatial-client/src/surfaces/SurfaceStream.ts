export type SurfaceFrame = {
  streamId: string;
  sequence: number;
  width: number;
  height: number;
  mimeType: string;
  dataBase64: string;
};

export type SurfaceStreamHandle = {
  streamId: string;
  width: number;
  height: number;
};

export interface SurfaceStream {
  open(): Promise<void>;
  readFrame(): Promise<SurfaceFrame | null>;
  close(): Promise<void>;
}

export interface SurfaceCommandClient {
  sendCommand(operation: string, target?: string, payload?: unknown): Promise<unknown>;
}

export type PointerPhase = 'move' | 'down' | 'up';
export type PointerButton = 'primary' | 'secondary';
export type KeyPhase = 'down' | 'up';

export interface WindowInputSink {
  pointer(
    phase: PointerPhase,
    u: number,
    v: number,
    button?: PointerButton,
  ): Promise<unknown>;
  wheel(u: number, v: number, deltaX: number, deltaY: number): Promise<unknown>;
  key(phase: KeyPhase, key: string): Promise<unknown>;
  text(text: string): Promise<unknown>;
}

type PendingMove = {
  u: number;
  v: number;
  waiters: Array<{
    resolve(value: unknown): void;
    reject(error: unknown): void;
  }>;
};

export class ProtocolWindowInputSink implements WindowInputSink {
  readonly #client: SurfaceCommandClient;
  readonly #windowEntityId: string;
  #queue: Promise<void> = Promise.resolve();
  #pendingMove: PendingMove | null = null;

  constructor(client: SurfaceCommandClient, windowEntityId: string) {
    this.#client = client;
    this.#windowEntityId = windowEntityId;
  }

  pointer(
    phase: PointerPhase,
    u: number,
    v: number,
    button?: PointerButton,
  ): Promise<unknown> {
    const safeU = normalized(u);
    const safeV = normalized(v);
    if (phase !== 'move') {
      return this.#enqueue(() => this.#sendPointer(phase, safeU, safeV, button));
    }

    return new Promise<unknown>((resolve, reject) => {
      if (this.#pendingMove) {
        this.#pendingMove.u = safeU;
        this.#pendingMove.v = safeV;
        this.#pendingMove.waiters.push({ resolve, reject });
        return;
      }

      this.#pendingMove = {
        u: safeU,
        v: safeV,
        waiters: [{ resolve, reject }],
      };
      void this.#enqueue(async () => {
        const pending = this.#pendingMove;
        this.#pendingMove = null;
        if (!pending) return;
        try {
          const result = await this.#sendPointer('move', pending.u, pending.v);
          for (const waiter of pending.waiters) waiter.resolve(result);
        } catch (error) {
          for (const waiter of pending.waiters) waiter.reject(error);
        }
      });
    });
  }

  wheel(u: number, v: number, deltaX: number, deltaY: number): Promise<unknown> {
    const safeU = normalized(u);
    const safeV = normalized(v);
    return this.#enqueue(() => this.#client.sendCommand('window.input', this.#windowEntityId, {
      kind: 'wheel',
      x: safeU,
      y: 1 - safeV,
      deltaX,
      deltaY,
    }));
  }

  key(phase: KeyPhase, key: string): Promise<unknown> {
    return this.#enqueue(() => this.#client.sendCommand('window.input', this.#windowEntityId, {
      kind: 'key',
      phase,
      key,
    }));
  }

  text(text: string): Promise<unknown> {
    return this.#enqueue(() => this.#client.sendCommand('window.input', this.#windowEntityId, {
      kind: 'text',
      text,
    }));
  }

  #sendPointer(
    phase: PointerPhase,
    u: number,
    v: number,
    button?: PointerButton,
  ): Promise<unknown> {
    return this.#client.sendCommand('window.input', this.#windowEntityId, {
      kind: 'pointer',
      phase,
      x: u,
      y: 1 - v,
      ...(button ? { button } : {}),
    });
  }

  #enqueue(run: () => Promise<unknown>): Promise<unknown> {
    const result = this.#queue.then(run, run);
    this.#queue = result.then(
      () => undefined,
      () => undefined,
    );
    return result;
  }
}

export class SurfaceUnavailableError extends Error {
  constructor() {
    super('Surface capture is unavailable.');
    this.name = 'SurfaceUnavailableError';
  }
}

export class ProtocolSurfaceStream implements SurfaceStream {
  readonly #client: SurfaceCommandClient;
  readonly #windowEntityId: string;
  #handle: SurfaceStreamHandle | null = null;
  #lastSequence = -1;

  constructor(
    client: SurfaceCommandClient,
    windowEntityId: string,
  ) {
    this.#client = client;
    this.#windowEntityId = windowEntityId;
  }

  async open(): Promise<void> {
    if (this.#handle) return;
    const value = await this.#client.sendCommand('surface.open', this.#windowEntityId);
    this.#handle = parseHandle(value);
  }

  async readFrame(): Promise<SurfaceFrame | null> {
    if (!this.#handle) {
      throw new Error('Surface stream is not open.');
    }

    const value = await this.#client.sendCommand(
      'surface.frame',
      this.#handle.streamId,
      { afterSequence: this.#lastSequence },
    );
    if (!isRecord(value) || typeof value.available !== 'boolean') {
      throw new Error('Host returned an invalid surface frame response.');
    }
    if (!value.available) throw new SurfaceUnavailableError();
    if (value.frame === null) return null;
    const frame = parseFrame(value.frame);
    if (frame.streamId !== this.#handle.streamId) {
      throw new Error('Surface frame stream id does not match the open stream.');
    }
    this.#lastSequence = frame.sequence;
    return frame;
  }

  async close(): Promise<void> {
    const handle = this.#handle;
    this.#handle = null;
    this.#lastSequence = -1;
    if (!handle) return;
    await this.#client.sendCommand('surface.close', handle.streamId);
  }
}

function parseHandle(value: unknown): SurfaceStreamHandle {
  if (!isRecord(value)
    || typeof value.streamId !== 'string'
    || !Number.isInteger(value.width)
    || !Number.isInteger(value.height)) {
    throw new Error('Host returned an invalid surface stream handle.');
  }
  return value as SurfaceStreamHandle;
}

function parseFrame(value: unknown): SurfaceFrame {
  if (!isRecord(value)
    || typeof value.streamId !== 'string'
    || !Number.isInteger(value.sequence)
    || !Number.isInteger(value.width)
    || !Number.isInteger(value.height)
    || typeof value.mimeType !== 'string'
    || typeof value.dataBase64 !== 'string') {
    throw new Error('Host returned an invalid surface frame.');
  }
  return value as SurfaceFrame;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function normalized(value: number): number {
  if (!Number.isFinite(value) || value < 0 || value > 1) {
    throw new RangeError('Surface coordinates must be between 0 and 1.');
  }
  return value;
}
