import assert from 'node:assert/strict';
import test from 'node:test';
import {
  BrowserFallbackTransport,
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

test('browser fallback rejects agent instructions visibly instead of swallowing them', async () => {
  const transport = new BrowserFallbackTransport();
  const bridge = new WorkspaceNativeBridge(transport);
  const events: WorkspaceNativeEnvelope[] = [];
  bridge.subscribe('agent.event', (message) => events.push(message));
  bridge.subscribe('voice.caption', (message) => events.push(message));

  bridge.post('agent.instruction', { text: 'hello' });
  await new Promise<void>((done) => queueMicrotask(done));

  assert.equal(events.length, 2);
  assert.equal(events[0]?.type, 'agent.event');
  assert.equal((events[0]?.payload as any).level, 'error');
  assert.match((events[0]?.payload as any).summary, /native.*runtime/i);
  assert.equal(events[1]?.type, 'voice.caption');
  assert.match((events[1]?.payload as any).text, /native.*runtime/i);
  bridge.destroy();
});
