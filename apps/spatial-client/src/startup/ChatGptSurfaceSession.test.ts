import assert from 'node:assert/strict';
import test from 'node:test';
import { ChatGptSurfaceSession } from './ChatGptSurfaceSession.ts';

test('attach docks the existing ChatGPT surface by default', () => {
  const docked: Array<[string, boolean]> = [];
  const collapsed: Array<[string, boolean]> = [];
  let isDocked = false;
  let isCollapsed = false;
  const session = new ChatGptSurfaceSession({
    setDocked: (id, value) => { docked.push([id, value]); isDocked = value; },
    setCollapsed: (id, value) => { collapsed.push([id, value]); isCollapsed = value; },
    isDocked: () => isDocked,
    isCollapsed: () => isCollapsed,
    focusWindow: async () => undefined,
  });

  session.attach('spatial.surface:chatgpt', 'pc.window:chatgpt');

  assert.deepEqual(docked, [['spatial.surface:chatgpt', true]]);
  assert.deepEqual(collapsed, [['spatial.surface:chatgpt', false]]);
  assert.deepEqual(session.state, {
    available: true,
    docked: true,
    collapsed: false,
    canFocus: true,
  });
});

test('dock, undock, collapse, and show operate on the same durable surface', () => {
  const docked: Array<[string, boolean]> = [];
  const collapsed: Array<[string, boolean]> = [];
  let isDocked = false;
  let isCollapsed = false;
  const session = new ChatGptSurfaceSession({
    setDocked: (id, value) => { docked.push([id, value]); isDocked = value; },
    setCollapsed: (id, value) => { collapsed.push([id, value]); isCollapsed = value; },
    isDocked: () => isDocked,
    isCollapsed: () => isCollapsed,
    focusWindow: async () => undefined,
  });
  session.attach('spatial.surface:chatgpt', 'pc.window:chatgpt');

  session.undock();
  session.collapse();
  session.show();
  session.dock();

  assert.deepEqual(docked, [
    ['spatial.surface:chatgpt', true],
    ['spatial.surface:chatgpt', false],
    ['spatial.surface:chatgpt', true],
  ]);
  assert.deepEqual(collapsed, [
    ['spatial.surface:chatgpt', false],
    ['spatial.surface:chatgpt', true],
    ['spatial.surface:chatgpt', false],
  ]);
  assert.equal(session.state.docked, true);
  assert.equal(session.state.collapsed, false);
});

test('session state reflects presentation changes made outside its own controls', () => {
  let isDocked = false;
  let isCollapsed = false;
  const session = new ChatGptSurfaceSession({
    setDocked: (_id, value) => { isDocked = value; },
    setCollapsed: (_id, value) => { isCollapsed = value; },
    isDocked: () => isDocked,
    isCollapsed: () => isCollapsed,
    focusWindow: async () => undefined,
  });
  session.attach('spatial.surface:chatgpt', 'pc.window:chatgpt');

  isDocked = false;
  isCollapsed = true;

  assert.equal(session.state.docked, false);
  assert.equal(session.state.collapsed, true);
});

test('focus uses the semantic ChatGPT window without changing presentation mode', async () => {
  const focused: string[] = [];
  let isDocked = false;
  const session = new ChatGptSurfaceSession({
    setDocked: (_id, value) => { isDocked = value; },
    setCollapsed: () => undefined,
    isDocked: () => isDocked,
    isCollapsed: () => false,
    focusWindow: async (id) => { focused.push(id); },
  });
  session.attach('spatial.surface:chatgpt', 'pc.window:chatgpt');
  session.undock();

  await session.focus();

  assert.deepEqual(focused, ['pc.window:chatgpt']);
  assert.equal(session.state.docked, false);
});

test('missing host semantic ids leave controls unavailable instead of guessing', async () => {
  let focused = false;
  const session = new ChatGptSurfaceSession({
    setDocked: () => undefined,
    setCollapsed: () => undefined,
    isDocked: () => false,
    isCollapsed: () => false,
    focusWindow: async () => { focused = true; },
  });

  session.attach(null, null);
  session.dock();
  session.collapse();
  await session.focus();

  assert.deepEqual(session.state, {
    available: false,
    docked: false,
    collapsed: false,
    canFocus: false,
  });
  assert.equal(focused, false);
});
