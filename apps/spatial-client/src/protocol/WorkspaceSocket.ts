import {
  PROTOCOL_VERSION,
  isProtocolEnvelope,
  type CommandEnvelope,
  type ProtocolEnvelope,
} from '@workspace/protocol';

export const WORKSPACE_ENDPOINT = 'ws://127.0.0.1:41771/workspace';

export interface SocketLike {
  readonly readyState: number;
  onopen: (() => void) | null;
  onmessage: ((event: { data: string }) => void) | null;
  onclose: (() => void) | null;
  onerror: (() => void) | null;
  send(data: string): void;
  close(code?: number, reason?: string): void;
}

export type SocketFactory = (url: string) => SocketLike;
export type EnvelopeListener = (envelope: ProtocolEnvelope) => void;

type PendingCommand = {
  resolve(value: unknown): void;
  reject(error: Error): void;
};

export class WorkspaceSocket {
  readonly #socket: SocketLike;
  readonly #openPromise: Promise<void>;
  readonly #pending = new Map<string, PendingCommand>();
  readonly #listeners = new Set<EnvelopeListener>();
  #nextId = 1;

  constructor(
    socketFactory: SocketFactory = (url) => new WebSocket(url) as unknown as SocketLike,
    endpoint = WORKSPACE_ENDPOINT,
  ) {
    this.#socket = socketFactory(endpoint);
    let rejectOpening: (error: Error) => void = () => {};
    this.#openPromise = this.#socket.readyState === 1
      ? Promise.resolve()
      : new Promise<void>((resolve, reject) => {
        let settled = false;
        this.#socket.onopen = () => {
          if (settled) return;
          settled = true;
          resolve();
        };
        rejectOpening = (error) => {
          if (settled) return;
          settled = true;
          reject(error);
        };
      });
    this.#socket.onmessage = (event) => this.#receive(event.data);
    this.#socket.onclose = () => {
      const error = new Error('Workspace connection closed.');
      rejectOpening(error);
      this.#rejectAll(error);
    };
    this.#socket.onerror = () => {
      const error = new Error('Workspace connection failed.');
      rejectOpening(error);
      this.#rejectAll(error);
    };
  }

  waitUntilOpen(): Promise<void> {
    return this.#openPromise;
  }

  sendCommand(operation: string, target?: string, payload?: unknown): Promise<unknown> {
    if (this.#socket.readyState !== 1) {
      return Promise.reject(new Error('Workspace connection is not open.'));
    }

    const id = `command-${this.#nextId++}`;
    const command: CommandEnvelope = {
      protocol: PROTOCOL_VERSION,
      type: 'command',
      id,
      operation,
      ...(target === undefined ? {} : { target }),
      ...(payload === undefined ? {} : { payload }),
    };

    const pending = new Promise<unknown>((resolve, reject) => {
      this.#pending.set(id, { resolve, reject });
    });
    try {
      this.#socket.send(JSON.stringify(command));
    } catch (error) {
      this.#pending.delete(id);
      const failure = error instanceof Error ? error : new Error(String(error));
      return Promise.reject(failure);
    }
    return pending;
  }

  subscribe(listener: EnvelopeListener): () => void {
    this.#listeners.add(listener);
    return () => this.#listeners.delete(listener);
  }

  close(): void {
    this.#rejectAll(new Error('Workspace connection closed.'));
    this.#socket.close(1000, 'Spatial client closed.');
  }

  #receive(raw: string): void {
    let value: unknown;
    try {
      value = JSON.parse(raw);
    } catch {
      this.#rejectAll(new Error('Host sent invalid workspace protocol JSON.'));
      return;
    }

    if (!isProtocolEnvelope(value)) {
      const version = typeof value === 'object' && value !== null
        ? (value as { protocol?: unknown }).protocol
        : undefined;
      this.#rejectAll(new Error(`Unsupported or invalid workspace protocol version: ${String(version)}`));
      return;
    }

    if ((value.type === 'result' || value.type === 'error') && value.id) {
      const pending = this.#pending.get(value.id);
      if (pending) {
        this.#pending.delete(value.id);
        if (value.type === 'result') {
          pending.resolve(value.payload);
        } else {
          pending.reject(new Error(`${value.code}: ${value.message}`));
        }
      }
    }

    for (const listener of this.#listeners) {
      listener(value);
    }
  }

  #rejectAll(error: Error): void {
    for (const pending of this.#pending.values()) {
      pending.reject(error);
    }
    this.#pending.clear();
  }
}
