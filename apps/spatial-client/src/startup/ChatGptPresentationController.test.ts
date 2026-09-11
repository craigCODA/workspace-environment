import assert from 'node:assert/strict';
import test from 'node:test';
import type { PresentationState } from '@workspace/world-schema';
import type { CameraPose } from '../navigation/CameraNavigator.ts';
import {
  ChatGptPresentationController,
  dockedPresentationFor,
} from './ChatGptPresentationController.ts';

const spatialPresentation: PresentationState = {
  position: { x: 1, y: 1.4, z: -3 },
  rotation: { x: 0, y: 0, z: 0, w: 1 },
  size: { x: 3.2, y: 1.8, z: 0.035 },
};

const camera: CameraPose = {
  position: { x: 0, y: 1.65, z: 4 },
  yaw: 0,
  pitch: 0,
};

test('docked presentation sits to the camera right and faces the camera', () => {
  const presentation = dockedPresentationFor(camera);

  assert.ok(presentation.position.x > camera.position.x);
  assert.ok(presentation.position.z < camera.position.z);
  assert.ok(presentation.size.y > presentation.size.x);
  assert.deepEqual(presentation.rotation, { x: 0, y: 0, z: 0, w: 1 });
});

test('dock follows camera without persisting camera-relative presentation', async () => {
  const previews: Array<PresentationState | null> = [];
  const commits: PresentationState[] = [];
  let currentCamera = camera;
  const controller = new ChatGptPresentationController({
    cameraPose: () => currentCamera,
    preview: (_surfaceEntityId, presentation) => previews.push(presentation),
    presentationFor: () => spatialPresentation,
    commit: async (_surfaceEntityId, presentation) => {
      commits.push(presentation);
    },
  });

  controller.attach('spatial.surface:chatgpt');
  controller.dock();
  assert.equal(controller.mode, 'docked');
  assert.equal(previews.length, 1);
  assert.equal(commits.length, 0);

  currentCamera = { ...camera, position: { x: 2, y: 1.65, z: 2 } };
  controller.tick();
  assert.equal(previews.length, 2);
  assert.notDeepEqual(previews[0], previews[1]);
  assert.equal(commits.length, 0);
});

test('undock clears transient docking and restores spatial mode', () => {
  const previews: Array<PresentationState | null> = [];
  const controller = new ChatGptPresentationController({
    cameraPose: () => camera,
    preview: (_surfaceEntityId, presentation) => previews.push(presentation),
    presentationFor: () => spatialPresentation,
    commit: async () => undefined,
  });

  controller.attach('spatial.surface:chatgpt');
  controller.dock();
  controller.undock();

  assert.equal(controller.mode, 'spatial');
  assert.equal(previews.at(-1), null);
});
