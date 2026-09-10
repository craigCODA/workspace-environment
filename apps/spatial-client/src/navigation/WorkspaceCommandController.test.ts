import assert from 'node:assert/strict';
import test from 'node:test';
import { WorkspaceCommandController } from './WorkspaceCommandController.ts';

test('open forwards a selected spatial surface using only its stable ID', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return { disposition: 'launched' }; } },
    () => 'spatial.surface:right',
    { surfaceIds: () => ['spatial.surface:right'] },
  );

  const result = await controller.handle({
    id: 'native-1', command: 'application.open',
    args: { applicationId: 'app:notepad', targetSurfaceId: '$selected' },
  });

  assert.deepEqual(result, { id: 'native-1', ok: true, payload: { disposition: 'launched' } });
  assert.deepEqual(calls, [[
    'application.open', undefined,
    { applicationId: 'app:notepad', targetSurfaceId: 'spatial.surface:right' },
  ]]);
});

test('open creates a non-overlapping camera-relative presentation when no surface is selected', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } },
    () => null,
    {
      cameraPose: () => ({ position: { x: 0, y: 1.65, z: 4 }, yaw: 0, pitch: 0 }),
      surfaceIds: () => [],
      occupiedPresentations: () => [{ position: { x: 0, y: 1.65, z: 1 } }],
    },
  );

  await controller.handle({ id: 'native-2', command: 'application.open', args: { applicationId: 'app:notepad' } });

  assert.deepEqual(calls, [[
    'application.open', undefined,
    {
      applicationId: 'app:notepad',
      presentation: {
        position: { x: 0.25, y: 1.65, z: 1.25 },
        rotation: { x: 0, y: 0, z: 0, w: 1 },
        size: { x: 3.2, y: 1.8, z: 0.035 },
      },
    },
  ]]);
});

test('rejects invalid selection and literal surface IDs before sending', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } },
    () => 'pc.window:notepad',
    { surfaceIds: () => ['spatial.surface:right'] },
  );

  assert.equal((await controller.handle({
    id: 'selected', command: 'application.open', args: { applicationId: 'app:notepad', targetSurfaceId: '$selected' },
  })).ok, false);
  assert.equal((await controller.handle({
    id: 'literal', command: 'application.open', args: { applicationId: 'app:notepad', targetSurfaceId: 'spatial.surface:missing' },
  })).ok, false);
  assert.deepEqual(calls, []);
});

test('rejects raw executable fields and unknown commands before sending', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } },
    () => null,
  );

  const rawPath = await controller.handle({
    id: 'x', command: 'application.open', args: { applicationId: 'app:notepad', executablePath: 'C:\\bad.exe' },
  });
  const shell = await controller.handle({ id: 'y', command: 'shell.run', args: {} });

  assert.equal(rawPath.ok, false);
  assert.equal(shell.ok, false);
  assert.deepEqual(calls, []);
});

test('keeps the native request ID on host success and failure', async () => {
  const success = new WorkspaceCommandController(
    { sendCommand: async () => ({ disposition: 'launched' }) }, () => null,
  );
  const failure = new WorkspaceCommandController(
    { sendCommand: async () => { throw new Error('host unavailable'); } }, () => null,
  );

  assert.equal((await success.handle({ id: 'native-success', command: 'application.profile.list', args: {} })).id, 'native-success');
  const result = await failure.handle({ id: 'native-error', command: 'application.profile.list', args: {} });
  assert.equal(result.id, 'native-error');
  assert.equal(result.ok, false);
});

test('uses the semantic window target for focus rather than leaking it into payload', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
  );

  await controller.handle({ id: 'focus-1', command: 'window.focus', args: { windowEntityId: 'pc.window:notepad' } });

  assert.deepEqual(calls, [['window.focus', 'pc.window:notepad']]);
});
