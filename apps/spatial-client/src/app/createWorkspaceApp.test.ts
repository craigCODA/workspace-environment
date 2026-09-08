import assert from 'node:assert/strict';
import test from 'node:test';
import { initializeWorkspaceConnection } from './createWorkspaceApp.ts';

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
