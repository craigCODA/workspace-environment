import assert from 'node:assert/strict';
import test from 'node:test';
import * as THREE from 'three';
import type { WorkspaceEntity } from '@workspace/world-schema';
import {
  dockedSurfacePresentation,
  WorkspaceScene,
} from './WorkspaceScene.ts';

function halfView(aspect: number, depth: number): { width: number; height: number } {
  const height = Math.tan(THREE.MathUtils.degToRad(52 / 2)) * depth;
  return { width: height * aspect, height };
}

test('docked surface stays inside the right side of common camera frustums', () => {
  for (const aspect of [16 / 9, 4 / 3]) {
    const presentation = dockedSurfacePresentation(aspect);
    const depth = Math.abs(presentation.position.z);
    const view = halfView(aspect, depth);

    assert.ok(presentation.position.x > 0);
    assert.ok(presentation.position.z < 0);
    assert.deepEqual(presentation.rotation, { x: 0, y: 0, z: 0, w: 1 });
    assert.ok(presentation.size.x > 0);
    assert.ok(presentation.size.y > 0);
    assert.ok(presentation.position.x + presentation.size.x / 2 < view.width);
    assert.ok(presentation.size.y / 2 < view.height);
  }
});

test('docking and undocking preserve durable presentation and existing bindings', async () => {
  const streams: string[] = [];
  const inputs: string[] = [];
  const renderer = {
    domElement: {
      className: '',
      parentElement: null,
      remove() {},
      getBoundingClientRect() { return { width: 0, height: 0, left: 0, top: 0 }; },
    },
    outputColorSpace: '',
    toneMapping: 0,
    toneMappingExposure: 0,
    xr: { enabled: false },
    setPixelRatio() {},
    setAnimationLoop() {},
    setSize() {},
    render() {},
    dispose() {},
  } as unknown as THREE.WebGLRenderer;
  const observer = { observe() {}, disconnect() {} } as unknown as ResizeObserver;
  const root = { append() {} } as unknown as HTMLElement;
  const scene = new WorkspaceScene(
    root,
    undefined,
    (id) => {
      streams.push(id);
      return { async open() {}, async readFrame() { return null; }, async close() {} };
    },
    (id) => {
      inputs.push(id);
      return { async pointer() {}, async wheel() {}, async key() {}, async text() {} };
    },
    () => ({ async setPresentation() {} }),
    { renderer, resizeObserver: observer },
  );
  const presentation = {
    position: { x: -1.5, y: 1.6, z: -4 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 3.2, y: 1.8, z: 0.035 },
  };
  const entity: WorkspaceEntity = {
    id: 'spatial.surface:chatgpt',
    kind: 'spatial.surface',
    name: 'ChatGPT',
    properties: {},
    capabilities: ['select'],
    relationships: [{ type: 'displays', targetId: 'pc.window:chatgpt' }],
    presentation,
  };

  scene.upsert(entity);
  scene.setSurfaceDocked(entity.id, true);

  assert.equal(scene.isSurfaceDocked(entity.id), true);
  assert.deepEqual(scene.presentationFor(entity.id), presentation);
  assert.deepEqual(streams, ['pc.window:chatgpt']);
  assert.deepEqual(inputs, ['pc.window:chatgpt']);

  scene.setSurfaceDocked(entity.id, false);

  assert.equal(scene.isSurfaceDocked(entity.id), false);
  assert.deepEqual(scene.presentationFor(entity.id), presentation);
  assert.deepEqual(streams, ['pc.window:chatgpt']);
  assert.deepEqual(inputs, ['pc.window:chatgpt']);

  scene.dispose();
  await new Promise<void>((done) => queueMicrotask(done));
});

test('collapse and show preserve the requested dock mode', () => {
  const renderer = {
    domElement: {
      className: '', parentElement: null, remove() {},
      getBoundingClientRect() { return { width: 0, height: 0, left: 0, top: 0 }; },
    },
    outputColorSpace: '', toneMapping: 0, toneMappingExposure: 0,
    xr: { enabled: false }, setPixelRatio() {}, setAnimationLoop() {}, setSize() {}, render() {}, dispose() {},
  } as unknown as THREE.WebGLRenderer;
  const scene = new WorkspaceScene(
    { append() {} } as unknown as HTMLElement,
    undefined,
    null,
    null,
    null,
    { renderer, resizeObserver: { observe() {}, disconnect() {} } as unknown as ResizeObserver },
  );
  const entity: WorkspaceEntity = {
    id: 'spatial.surface:chatgpt', kind: 'spatial.surface', name: 'ChatGPT', properties: {}, capabilities: ['select'],
    relationships: [],
    presentation: {
      position: { x: 0, y: 1.5, z: -3 }, rotation: { x: 0, y: 0, z: 0, w: 1 }, size: { x: 3, y: 2, z: 0.035 },
    },
  };
  scene.setSurfaceDocked(entity.id, true);
  scene.setSurfaceCollapsed(entity.id, true);
  scene.upsert(entity);

  assert.equal(scene.isSurfaceDocked(entity.id), true);
  assert.equal(scene.isSurfaceCollapsed(entity.id), true);

  scene.setSurfaceCollapsed(entity.id, false);
  assert.equal(scene.isSurfaceCollapsed(entity.id), false);
  assert.equal(scene.isSurfaceDocked(entity.id), true);
  scene.dispose();
});
