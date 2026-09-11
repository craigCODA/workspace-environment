import assert from 'node:assert/strict';
import test from 'node:test';
import {
  CameraNavigator,
  type CameraPose,
  type CameraPoseTarget,
} from './CameraNavigator.ts';

class FakeCameraTarget implements CameraPoseTarget {
  pose: CameraPose = { position: { x: 0, y: 1.65, z: 4 }, yaw: 0, pitch: 0 };

  getCameraPose(): CameraPose {
    return structuredClone(this.pose);
  }

  setCameraPose(pose: CameraPose): void {
    this.pose = structuredClone(pose);
  }
}

test('manual cancellation stops an in-flight agent navigation', () => {
  const target = new FakeCameraTarget();
  const navigator = new CameraNavigator(target);
  navigator.navigate(
    { position: { x: 4, y: 1.65, z: -2 }, yaw: 0.4, pitch: 0 },
    { mode: 'glide', durationMs: 1000 },
  );
  navigator.tick(250);
  navigator.cancel('manual-input');
  const stopped = target.getCameraPose();
  navigator.tick(1000);

  assert.deepEqual(target.getCameraPose(), stopped);
  assert.equal(navigator.isNavigating, false);
});

test('glide reaches the exact target with pitch and duration clamped', () => {
  const target = new FakeCameraTarget();
  const navigator = new CameraNavigator(target);
  navigator.navigate(
    { position: { x: 2, y: 3, z: -8 }, yaw: 1.2, pitch: 9 },
    { mode: 'glide', durationMs: 90_000 },
  );

  navigator.tick(30_000);

  assert.deepEqual(target.pose.position, { x: 2, y: 3, z: -8 });
  assert.equal(target.pose.yaw, 1.2);
  assert.ok(target.pose.pitch < Math.PI / 2);
  assert.equal(navigator.isNavigating, false);
});

test('non-finite navigation coordinates are rejected', () => {
  const navigator = new CameraNavigator(new FakeCameraTarget());

  assert.throws(() => navigator.navigate({
    position: { x: Number.NaN, y: 1, z: 2 },
    yaw: 0,
    pitch: 0,
  }));
});
