import assert from 'node:assert/strict';
import test from 'node:test';
import {
  WorkspaceNativeBridge,
  type NativeMessageTransport,
  type WorkspaceNativeEnvelope,
} from './WorkspaceNativeBridge.ts';

class FakeTransport implements NativeMessageTransport {
  readonly posted: unknown[] = [];
  listener: ((message: unknown) => void) | null = null;

  postMessage(message: unknown): void {
    this.posted.push(message);
  }

  subscribe(listener: (message: unknown) => void): () => void {
    this.listener = listener;
    return () => {
      this.listener = null;
    };
  }

  send(message: unknown): void {
    this.listener?.(message);
  }
}

test('bridge posts a versioned renderer envelope', () => {
  const transport = new FakeTransport();
  const bridge = new WorkspaceNativeBridge(transport);

  bridge.post('renderer.ready', { surface: 'spatial' });

  assert.deepEqual(transport.posted, [{
    version: 1,
    type: 'renderer.ready',
    payload: { surface: 'spatial' },
  }]);
  bridge.destroy();
});

test('bridge routes only well-formed version-one native messages', () => {
  const transport = new FakeTransport();
  const bridge = new WorkspaceNativeBridge(transport);
  const received: WorkspaceNativeEnvelope[] = [];
  bridge.subscribe('voice.caption', (message) => received.push(message));

  transport.send({ version: 2, type: 'voice.caption', payload: { text: 'old' } });
  transport.send({ version: 1, payload: { text: 'missing type' } });
  transport.send({ version: 1, type: 'voice.caption', payload: { text: 'Hello.' } });

  assert.deepEqual(received, [{
    version: 1,
    type: 'voice.caption',
    payload: { text: 'Hello.' },
  }]);
  bridge.destroy();
});
