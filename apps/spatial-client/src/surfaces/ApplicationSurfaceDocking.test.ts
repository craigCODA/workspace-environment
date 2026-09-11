import assert from 'node:assert/strict';
import test from 'node:test';
import type { PresentationState } from '@workspace/world-schema';
import { ApplicationSurface, type SurfaceTextureTarget } from './ApplicationSurface.ts';

const initial: PresentationState = {
  position: { x: 1, y: 1.5, z: -3 },
  rotation: { x: 0, y: 0, z: 0, w: 1 },
  size: { x: 3.2, y: 1.8, z: 0.035 },
};

const docked: PresentationState = {
  position: { x: 1.2, y: 0, z: -2.4 },
  rotation: { x: 0, y: 0, z: 0, w: 1 },
  size: { x: 1.3, y: 1.9, z: 0.035 },
};

const moved: PresentationState = {
  ...initial,
  position: { x: -2, y: 1.8, z: -4 },
};

function createSurface(applied: PresentationState[]): ApplicationSurface {
  const target: SurfaceTextureTarget = {
    update() {},
    markUnavailable() {},
    setPresentation(presentation) { applied.push(presentation); },
    dispose() {},
  };
  return new ApplicationSurface({
    async open() {},
    async readFrame() { return null; },
    async close() {},
  }, target, { initialPresentation: initial });
}

test('transient presentation moves pixels without replacing durable presentation', () => {
  const applied: PresentationState[] = [];
  const surface = createSurface(applied);

  surface.setTransientPresentation(docked);

  assert.deepEqual(surface.presentation, initial);
  assert.deepEqual(surface.displayedPresentation, initial);
  assert.deepEqual(applied, [initial, docked]);
});

test('authoritative updates stay durable while docked and appear after override clears', () => {
  const applied: PresentationState[] = [];
  const surface = createSurface(applied);

  surface.setTransientPresentation(docked);
  surface.acceptAuthoritativePresentation(moved);

  assert.deepEqual(surface.presentation, moved);
  assert.deepEqual(surface.displayedPresentation, moved);
  assert.deepEqual(applied, [initial, docked]);

  surface.setTransientPresentation(null);

  assert.deepEqual(applied, [initial, docked, moved]);
});
