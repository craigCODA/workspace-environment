export const PROTOCOL_VERSION = 1 as const;

export type ProtocolVersion = typeof PROTOCOL_VERSION;

export type CommandEnvelope = {
  protocol: ProtocolVersion;
  type: 'command';
  id: string;
  operation: string;
  target?: string;
  payload?: unknown;
};

export type ResultEnvelope = {
  protocol: ProtocolVersion;
  type: 'result';
  id: string;
  success: true;
  payload?: unknown;
};

export type EventEnvelope = {
  protocol: ProtocolVersion;
  type: 'event';
  event: string;
  payload?: unknown;
};

export type SnapshotEnvelope = {
  protocol: ProtocolVersion;
  type: 'snapshot';
  entities: unknown[];
};

export type ErrorEnvelope = {
  protocol: ProtocolVersion;
  type: 'error';
  id?: string;
  code: string;
  message: string;
};

export type ProtocolEnvelope =
  | CommandEnvelope
  | ResultEnvelope
  | EventEnvelope
  | SnapshotEnvelope
  | ErrorEnvelope;

const ENVELOPE_TYPES = new Set(['command', 'result', 'event', 'snapshot', 'error']);

export function isProtocolEnvelope(value: unknown): value is ProtocolEnvelope {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  return candidate.protocol === PROTOCOL_VERSION
    && typeof candidate.type === 'string'
    && ENVELOPE_TYPES.has(candidate.type);
}
