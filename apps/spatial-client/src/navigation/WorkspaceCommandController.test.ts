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

test('rejects a missing or blank native request ID before every host call', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
  );

  for (const id of [undefined, '', '   ', 1]) {
    const result = await controller.handle({ id, command: 'application.open', args: { applicationId: 'app:notepad' } });
    assert.equal(result.ok, false);
  }
  assert.deepEqual(calls, []);
});

test('forwards complete explicit presentation state without dropping optional fields', async () => {
  const calls: unknown[][] = [];
  const explicit = {
    position: { x: 1, y: 2, z: 3 },
    rotation: { x: 0.1, y: 0.2, z: 0.3, w: 0.4 },
    size: { x: 3.2, y: 1.8, z: 0.035 },
    parentPresentationId: 'spatial.room:desk',
    representation: 'screen',
  };
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
  );

  const result = await controller.handle({
    id: 'complete-presentation', command: 'application.open',
    args: { applicationId: 'app:notepad', presentation: explicit },
  });

  assert.equal(result.ok, true);
  assert.deepEqual(calls[0]?.[2], { applicationId: 'app:notepad', presentation: explicit });
});

test('places a default surface exactly three metres along the pitched camera forward vector', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
    { cameraPose: () => ({ position: { x: 1, y: 2, z: 3 }, yaw: Math.PI / 2, pitch: Math.PI / 6 }) },
  );

  await controller.handle({ id: 'pitched', command: 'application.open', args: { applicationId: 'app:notepad' } });

  const target = (calls[0]?.[2] as { presentation: { position: { x: number; y: number; z: number } } }).presentation.position;
  assert.ok(Math.abs(target.x + 1.598076211) < 0.000001);
  assert.ok(Math.abs(target.y - 3.5) < 0.000001);
  assert.ok(Math.abs(target.z - 3) < 0.000001);
});

test('validates search, restart, and profile save payloads before sending', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
    { surfaceIds: () => ['spatial.surface:right'] },
  );
  const validProfile = {
    id: 'profile:notes', displayName: 'Notes', applicationId: 'app:notepad', arguments: ['--new'],
    workingDirectory: 'C:\\Workspace', launchPolicy: 'reuseOrLaunch',
    preferredSurfaceId: 'spatial.surface:right',
    preferredPresentation: {
      position: { x: 1, y: 2, z: 3 }, rotation: { x: 0, y: 0, z: 0, w: 1 },
      size: { x: 3.2, y: 1.8, z: 0.035 }, parentPresentationId: 'spatial.room:desk', representation: 'screen',
    },
  };

  assert.equal((await controller.handle({ id: 'bad-limit', command: 'application.search', args: { query: 'Notepad', limit: 11 } })).ok, false);
  assert.equal((await controller.handle({ id: 'bad-restart', command: 'application.restart', args: { windowEntityId: 'pc.window:x', profileId: 'profile:x' } })).ok, false);
  assert.equal((await controller.handle({ id: 'bad-profile', command: 'application.profile.save', args: { ...validProfile, arguments: ['ok', 3] } })).ok, false);
  assert.equal((await controller.handle({ id: 'profile', command: 'application.profile.save', args: validProfile })).ok, true);
  assert.equal((await controller.handle({ id: 'restart', command: 'application.restart', args: { profileId: 'profile:notes' } })).ok, true);
  assert.equal((await controller.handle({ id: 'search', command: 'application.search', args: { query: 'Notepad', limit: 3 } })).ok, true);
  assert.deepEqual(calls.map((call) => call[2]), [validProfile, { profileId: 'profile:notes' }, { query: 'Notepad', limit: 3 }]);
});

test('returns a deterministic safe host-failure category instead of exception text', async () => {
  const controller = new WorkspaceCommandController(
    { sendCommand: async () => { throw new Error('C:\\Secrets\\host-stack-details'); } }, () => null,
  );

  const result = await controller.handle({ id: 'safe-error', command: 'application.profile.list', args: {} });

  assert.deepEqual(result, { id: 'safe-error', ok: false, error: 'workspace_command_failed' });
});

test('blocks malformed presentation and entity-operation values before send', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController(
    { sendCommand: async (...args: unknown[]) => { calls.push(args); return {}; } }, () => null,
    { surfaceIds: () => ['spatial.surface:right'] },
  );
  const basePresentation = { position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: 1, y: 1, z: 1 } };
  const invalid = [
    { ...basePresentation, size: { x: 0, y: 1, z: 1 } },
    { ...basePresentation, size: { x: -1, y: 1, z: 1 } },
    { ...basePresentation, rotation: { x: 0, y: 0, z: 0, w: 0 } },
  ];
  for (const presentation of invalid) {
    assert.equal((await controller.handle({ id: `presentation-${presentation.size.x}`, command: 'application.open', args: { applicationId: 'app:notepad', presentation } })).ok, false);
  }
  assert.equal((await controller.handle({ id: 'focus-bad', command: 'window.focus', args: { windowEntityId: 'pc.application:notepad' } })).ok, false);
  assert.equal((await controller.handle({ id: 'close-source', command: 'application.close', args: { windowEntityId: 'pc.window:notepad', approvalSource: 7 } })).ok, false);
  assert.equal((await controller.handle({ id: 'bind-bad', command: 'surface.bindWindow', args: { surfaceEntityId: 'spatial.surface:right', windowEntityId: 'pc.window:notepad', replaceOccupied: 'yes' } })).ok, false);
  assert.deepEqual(calls, []);
});
