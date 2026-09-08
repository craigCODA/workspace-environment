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
