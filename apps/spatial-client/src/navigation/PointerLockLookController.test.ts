import assert from 'node:assert/strict';
import test from 'node:test';
import { PointerLockLookController } from './PointerLockLookController.ts';

test('relative mouse movement is routed only while pointer lock is active', () => {
  const moves: Array<[number, number]> = [];
  const controller = new PointerLockLookController((x, y) => moves.push([x, y]));

  controller.move(4, -2);
  controller.setLocked(true);
  controller.move(7, -5);
  controller.setLocked(false);
  controller.move(9, 9);

  assert.deepEqual(moves, [[7, -5]]);
});
