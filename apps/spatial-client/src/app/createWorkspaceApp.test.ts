import assert from 'node:assert/strict';
import test from 'node:test';
import {
  initializeWorkspaceConnection,
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

test('an Alt-reserved keyup stays isolated after Alt is released first', () => {
  const tracker = new SuppressedKeyReleaseTracker();

  tracker.reserve('ArrowRight');

  assert.equal(tracker.consume('ArrowRight'), true);
  assert.equal(tracker.consume('ArrowRight'), false);
});
