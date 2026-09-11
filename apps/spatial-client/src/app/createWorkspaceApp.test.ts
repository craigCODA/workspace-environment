import assert from 'node:assert/strict';
import test from 'node:test';
import {
  canEditDurablePresentation,
  initializeReadyWorkspace,
  initializeWorkspaceConnection,
  NativeCommandResultRelay,
  shouldRequestPointerLock,
  SuppressedKeyReleaseTracker,
} from './createWorkspaceApp.ts';

test('initial connection agreement requests application inventory without user action', async () => {
  const calls: string[] = [];
  const socket = {
    async waitUntilOpen(): Promise<void> {
      calls.push('ready');
    },
    async sendCommand(operation: string): Promise<void> {
      calls.push(operation);
    },
  };

  await initializeWorkspaceConnection(socket);

  assert.deepEqual(calls, ['ready', 'application.list']);
});

test('workspace readiness is posted before optional default application startup', async () => {
  const calls: string[] = [];
  const socket = {
    async waitUntilOpen(): Promise<void> {
      calls.push('connected');
    },
    async sendCommand(operation: string): Promise<void> {
      calls.push(operation);
    },
  };

  await initializeReadyWorkspace(
    socket,
    () => calls.push('ready'),
    async () => { calls.push('default-app'); },
  );
  await new Promise<void>((done) => queueMicrotask(done));

  assert.deepEqual(calls, ['connected', 'application.list', 'ready', 'default-app']);
});

test('optional default application failure does not reject workspace readiness', async () => {
  const calls: string[] = [];
  const socket = {
    async waitUntilOpen(): Promise<void> {
      calls.push('connected');
    },
    async sendCommand(operation: string): Promise<void> {
      calls.push(operation);
    },
  };

  await assert.doesNotReject(() => initializeReadyWorkspace(
    socket,
    () => calls.push('ready'),
    async () => { throw new Error('optional app failed'); },
  ));
  await new Promise<void>((done) => queueMicrotask(done));

  assert.deepEqual(calls, ['connected', 'application.list', 'ready']);
});

test('an Alt-reserved keyup stays isolated after Alt is released first', () => {
  const tracker = new SuppressedKeyReleaseTracker();

  tracker.reserve('ArrowRight');

  assert.equal(tracker.consume('ArrowRight'), true);
  assert.equal(tracker.consume('ArrowRight'), false);
});

test('does not post a late workspace command result after the app is destroyed', async () => {
  let resolve!: (value: { id: string; ok: boolean }) => void;
  const pending = new Promise<{ id: string; ok: boolean }>((done) => { resolve = done; });
  const posted: unknown[] = [];
  const relay = new NativeCommandResultRelay((result) => posted.push(result));

  relay.forward(pending);
  relay.destroy();
  resolve({ id: 'native-late', ok: false });
  await new Promise<void>((done) => queueMicrotask(() => done()));

  assert.deepEqual(posted, []);
});

test('camera capture is reserved for primary clicks on empty workspace', () => {
  assert.equal(shouldRequestPointerLock({
    primaryButton: true,
    interactiveUi: false,
    surfaceHit: false,
  }), true);
  assert.equal(shouldRequestPointerLock({
    primaryButton: false,
    interactiveUi: false,
    surfaceHit: false,
  }), false);
  assert.equal(shouldRequestPointerLock({
    primaryButton: true,
    interactiveUi: true,
    surfaceHit: false,
  }), false);
  assert.equal(shouldRequestPointerLock({
    primaryButton: true,
    interactiveUi: false,
    surfaceHit: true,
  }), false);
});

test('durable placement editing is disabled while a surface is docked', () => {
  const docked = new Set(['spatial.surface:chatgpt']);
  const isDocked = (id: string): boolean => docked.has(id);

  assert.equal(canEditDurablePresentation('spatial.surface:chatgpt', isDocked), false);
  assert.equal(canEditDurablePresentation('spatial.surface:terminal', isDocked), true);
});
