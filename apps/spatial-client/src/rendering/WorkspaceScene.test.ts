import assert from 'node:assert/strict';
import test from 'node:test';
import {
  FIRST_WORK_AREA_POSITION,
  INITIAL_CAMERA_POSITION,
  INITIAL_VIEW_TARGET,
  calculatePlanarMovement,
  SurfaceBindingLifecycle,
  surfaceBindingFor,
  surfaceBindingNeedsReplacement,
  WorkspaceScene,
} from './WorkspaceScene.ts';
import type { WorkspaceEntity } from '@workspace/world-schema';

test('the first work area is geometrically behind the initial view', () => {
  const forwardZ = INITIAL_VIEW_TARGET.z - INITIAL_CAMERA_POSITION.z;
  const workAreaZ = FIRST_WORK_AREA_POSITION.z - INITIAL_CAMERA_POSITION.z;

  assert.ok(forwardZ * workAreaZ < 0);
});

test('forward movement follows the initial negative-z view direction', () => {
  assert.deepEqual(calculatePlanarMovement(0, 1, 0), { x: 0, z: -1 });
});

test('a bound scene surface captures and receives input by window ID while presentation stays on its surface ID', () => {
  const surface: WorkspaceEntity = {
    id: 'spatial.surface:right', kind: 'spatial.surface', name: 'Right', properties: {},
    relationships: [{ type: 'displays', targetId: 'pc.window:notepad' }], capabilities: ['select'],
    presentation: {
      position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: 1, y: 1, z: 1 },
    },
  };

  assert.deepEqual(surfaceBindingFor(surface), {
    captureWindowId: 'pc.window:notepad', inputWindowId: 'pc.window:notepad', presentationSurfaceId: 'spatial.surface:right',
  });
  assert.equal(surfaceBindingNeedsReplacement('pc.window:notepad', { ...surface, relationships: [{ type: 'displays', targetId: 'pc.window:terminal' }] }), true);
});

test('a scene rebind disposes the old window binding exactly once', () => {
  const surface = {
    id: 'spatial.surface:right', kind: 'spatial.surface', name: 'Right', properties: {},
    relationships: [{ type: 'displays', targetId: 'pc.window:notepad' }], capabilities: ['select'],
    presentation: { position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: 1, y: 1, z: 1 } },
  } satisfies WorkspaceEntity;
  const lifecycle = new SurfaceBindingLifecycle(surface);
  let disposed = 0;

  assert.equal(lifecycle.rebind({ ...surface, relationships: [{ type: 'displays', targetId: 'pc.window:terminal' }] }, () => { disposed += 1; }), true);
  assert.equal(lifecycle.rebind({ ...surface, relationships: [{ type: 'displays', targetId: 'pc.window:terminal' }] }, () => { disposed += 1; }), false);
  assert.equal(disposed, 1);
});

test('WorkspaceScene uses window factories for capture/input and its surface for presentation, then disposes on rebind', async () => {
  const streams: string[] = [];
  const inputs: string[] = [];
  const presentations: string[] = [];
  const closed: string[] = [];
  const renderer = {
    domElement: { className: '', parentElement: null, remove() {}, getBoundingClientRect() { return { width: 0, height: 0, left: 0, top: 0 }; } },
    outputColorSpace: '', toneMapping: 0, toneMappingExposure: 0, xr: { enabled: false },
    setPixelRatio() {}, setAnimationLoop() {}, setSize() {}, render() {}, dispose() {},
  } as unknown as import('three').WebGLRenderer;
  const observer = { observe() {}, disconnect() {} } as unknown as ResizeObserver;
  const root = { append() {} } as unknown as HTMLElement;
  const scene = new WorkspaceScene(root, undefined,
    (id) => ({ async open() { streams.push(id); }, async readFrame() { return null; }, async close() { closed.push(id); } }),
    (id) => { inputs.push(id); return { async pointer() {}, async wheel() {}, async key() {}, async text() {} }; },
    (id) => { presentations.push(id); return { async setPresentation() {} }; },
    { renderer, resizeObserver: observer });
  const make = (windowId: string): WorkspaceEntity => ({
    id: 'spatial.surface:right', kind: 'spatial.surface', name: 'Right', properties: {}, capabilities: ['select'],
    relationships: [{ type: 'displays', targetId: windowId }],
    presentation: { position: { x: 0, y: 0, z: 0 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: 1, y: 1, z: 1 } },
  });
  scene.upsert(make('pc.window:notepad'));
  await new Promise<void>((done) => queueMicrotask(done));
  assert.deepEqual(scene.snapshot('spatial.surface:right').entities[0]?.relationships, [
    { type: 'displays', targetId: 'pc.window:notepad' },
  ]);
  scene.upsert(make('pc.window:terminal'));
  await new Promise<void>((done) => queueMicrotask(done));
  scene.dispose();

  assert.deepEqual(streams, ['pc.window:notepad', 'pc.window:terminal']);
  assert.deepEqual(inputs, ['pc.window:notepad', 'pc.window:terminal']);
  assert.deepEqual(presentations, ['spatial.surface:right', 'spatial.surface:right']);
  assert.deepEqual(closed, ['pc.window:notepad', 'pc.window:terminal']);
});
