import assert from 'node:assert/strict';
import test from 'node:test';
import {
  FIRST_WORK_AREA_POSITION,
  INITIAL_CAMERA_POSITION,
  INITIAL_VIEW_TARGET,
  calculatePlanarMovement,
} from './WorkspaceScene.ts';

test('the first work area is geometrically behind the initial view', () => {
  const forwardZ = INITIAL_VIEW_TARGET.z - INITIAL_CAMERA_POSITION.z;
  const workAreaZ = FIRST_WORK_AREA_POSITION.z - INITIAL_CAMERA_POSITION.z;

  assert.ok(forwardZ * workAreaZ < 0);
});

test('forward movement follows the initial negative-z view direction', () => {
  assert.deepEqual(calculatePlanarMovement(0, 1, 0), { x: 0, z: -1 });
});
