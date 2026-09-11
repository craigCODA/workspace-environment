import assert from 'node:assert/strict';
import test from 'node:test';
import { surfaceDockControlState } from './SurfaceDockControls.ts';

test('spatial mode offers Dock and Collapse', () => {
  assert.deepEqual(surfaceDockControlState('spatial'), {
    mode: 'spatial',
    primary: { label: 'Dock', action: 'dock', disabled: false },
    visibility: { label: 'Collapse', action: 'collapse' },
  });
});

test('docked mode offers Undock and Collapse', () => {
  assert.deepEqual(surfaceDockControlState('docked'), {
    mode: 'docked',
    primary: { label: 'Undock', action: 'undock', disabled: false },
    visibility: { label: 'Collapse', action: 'collapse' },
  });
});

test('collapsed mode exposes Show and disables dock mode changes until visible', () => {
  assert.deepEqual(surfaceDockControlState('collapsed'), {
    mode: 'collapsed',
    primary: { label: 'Dock', action: 'dock', disabled: true },
    visibility: { label: 'Show', action: 'show' },
  });
});
