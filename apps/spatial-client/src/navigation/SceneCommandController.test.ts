import assert from 'node:assert/strict';
import test from 'node:test';
import type { PresentationState } from '@workspace/world-schema';
import { CameraNavigator, type CameraPose } from './CameraNavigator.ts';
import {
  SceneCommandController,
  type SceneControlTarget,
  type SceneSnapshot,
} from './SceneCommandController.ts';

class FakeScene implements SceneControlTarget {
  pose: CameraPose = { position: { x: 0, y: 1.65, z: 4 }, yaw: 0, pitch: 0 };
  presentation: PresentationState = {
    position: { x: 1, y: 2, z: 3 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 4, y: 2, z: 0.1 },
  };
  docked = false;
  collapsed = false;

  getCameraPose(): CameraPose { return structuredClone(this.pose); }
  setCameraPose(pose: CameraPose): void { this.pose = structuredClone(pose); }
  lookBy(): void {}
  move(): void {}
  snapshot(): SceneSnapshot {
    return {
      camera: this.getCameraPose(),
      entities: [{
        id: 'surface:one',
        kind: 'pc.window',
        name: 'One',
        presentation: this.presentation,
        relationships: [],
        selected: false,
      }],
    };
  }
  presentationFor(entityId: string): PresentationState | null {
    return entityId === 'surface:one' ? structuredClone(this.presentation) : null;
  }
  async commitPresentation(_entityId: string, presentation: PresentationState): Promise<void> {
    this.presentation = structuredClone(presentation);
  }
  setSurfaceDocked(entityId: string, docked: boolean): void {
    if (entityId !== 'surface:one') throw new Error('unknown surface');
    this.docked = docked;
  }
  isSurfaceDocked(entityId: string): boolean {
    return entityId === 'surface:one' && this.docked;
  }
  setSurfaceCollapsed(entityId: string, collapsed: boolean): void {
    if (entityId !== 'surface:one') throw new Error('unknown surface');
    this.collapsed = collapsed;
  }
  isSurfaceCollapsed(entityId: string): boolean {
    return entityId === 'surface:one' && this.collapsed;
  }
}

test('scene inspection returns structured state without pixel data', async () => {
  const target = new FakeScene();
  const controller = new SceneCommandController(target, new CameraNavigator(target));

  const result = await controller.handle({ id: 'inspect-1', command: 'scene.inspect' });

  assert.equal(result.ok, true);
  assert.equal(result.id, 'inspect-1');
  assert.equal(JSON.stringify(result).includes('pixels'), false);
  assert.equal(JSON.stringify(result).includes('surface:one'), true);
});

test('surface resize rejects non-positive dimensions and preserves presentation', async () => {
  const target = new FakeScene();
  const controller = new SceneCommandController(target, new CameraNavigator(target));

  const result = await controller.handle({
    id: 'resize-1',
    command: 'surface.resize',
    args: { entityId: 'surface:one', size: { x: 0, y: 2, z: 0.1 } },
  });

  assert.equal(result.ok, false);
  assert.deepEqual(target.presentation.size, { x: 4, y: 2, z: 0.1 });
});

test('surface dock command changes only transient dock state', async () => {
  const target = new FakeScene();
  const controller = new SceneCommandController(target, new CameraNavigator(target));
  const durable = structuredClone(target.presentation);

  const docked = await controller.handle({
    id: 'dock-1',
    command: 'surface.dock',
    args: { entityId: 'surface:one', docked: true },
  });
  const undocked = await controller.handle({
    id: 'dock-2',
    command: 'surface.dock',
    args: { entityId: 'surface:one', docked: false },
  });

  assert.deepEqual(docked, {
    id: 'dock-1',
    ok: true,
    payload: { entityId: 'surface:one', docked: true },
  });
  assert.deepEqual(undocked, {
    id: 'dock-2',
    ok: true,
    payload: { entityId: 'surface:one', docked: false },
  });
  assert.equal(target.docked, false);
  assert.deepEqual(target.presentation, durable);
});

test('surface collapse command preserves dock state', async () => {
  const target = new FakeScene();
  target.docked = true;
  const controller = new SceneCommandController(target, new CameraNavigator(target));

  const result = await controller.handle({
    id: 'collapse-1',
    command: 'surface.collapse',
    args: { entityId: 'surface:one', collapsed: true },
  });

  assert.deepEqual(result, {
    id: 'collapse-1',
    ok: true,
    payload: { entityId: 'surface:one', collapsed: true },
  });
  assert.equal(target.collapsed, true);
  assert.equal(target.docked, true);
});

test('surface presentation commands reject unknown entities', async () => {
  const target = new FakeScene();
  const controller = new SceneCommandController(target, new CameraNavigator(target));

  const result = await controller.handle({
    id: 'dock-missing',
    command: 'surface.dock',
    args: { entityId: 'surface:missing', docked: true },
  });

  assert.equal(result.ok, false);
});

test('unknown commands always receive an error result', async () => {
  const target = new FakeScene();
  const controller = new SceneCommandController(target, new CameraNavigator(target));

  const result = await controller.handle({ id: 'unknown-1', command: 'camera.teleport-through-wall' });

  assert.deepEqual(result, {
    id: 'unknown-1',
    ok: false,
    error: 'Unsupported scene command: camera.teleport-through-wall',
  });
});