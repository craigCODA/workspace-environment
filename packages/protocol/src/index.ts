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

export function isProtocolEnvelope(value: unknown): value is ProtocolEnvelope {
  if (typeof value !== 'object' || value === null) return false;
  const candidate = value as Record<string, unknown>;
  if (candidate.protocol !== PROTOCOL_VERSION) return false;

  switch (candidate.type) {
    case 'command':
      return isNonEmptyString(candidate.id)
        && isNonEmptyString(candidate.operation)
        && (candidate.target === undefined || typeof candidate.target === 'string');
    case 'result':
      return isNonEmptyString(candidate.id) && candidate.success === true;
    case 'event':
      return isNonEmptyString(candidate.event);
    case 'snapshot':
      return Array.isArray(candidate.entities);
    case 'error':
      return (candidate.id === undefined || typeof candidate.id === 'string')
        && isNonEmptyString(candidate.code)
        && typeof candidate.message === 'string';
    default:
      return false;
  }
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0;
}
