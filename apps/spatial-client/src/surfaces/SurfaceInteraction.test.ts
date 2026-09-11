import assert from 'node:assert/strict';
import test from 'node:test';
import type { PresentationState } from '@workspace/world-schema';
import {
  ProtocolWindowInputSink,
  ProtocolSurfaceStream,
  type SurfaceFrame,
} from './SurfaceStream.ts';
import {
  ApplicationSurface,
  ProtocolPresentationSink,
  ThreeSurfaceTextureTarget,
  type SurfaceTextureTarget,
} from './ApplicationSurface.ts';

test('surface stream uses runtime stream ids without exposing Windows capture details', async () => {
  const commands: Array<{ operation: string; target?: string; payload?: unknown }> = [];
  const frame: SurfaceFrame = {
    streamId: 'surface-runtime-7',
    sequence: 4,
    width: 800,
    height: 600,
    mimeType: 'image/png',
    dataBase64: 'iVBORw0KGgo=',
  };
  const socket = {
    async sendCommand(operation: string, target?: string, payload?: unknown): Promise<unknown> {
      commands.push({ operation, target, payload });
      if (operation === 'surface.open') {
        return { streamId: frame.streamId, width: frame.width, height: frame.height };
      }
      if (operation === 'surface.frame') {
        return { available: true, frame };
      }
      return {};
    },
  };
  const stream = new ProtocolSurfaceStream(socket, 'pc.window:pc.application:test');

  await stream.open();
  const received = await stream.readFrame();
  await stream.close();

  assert.deepEqual(received, frame);
  assert.deepEqual(commands, [
    { operation: 'surface.open', target: 'pc.window:pc.application:test', payload: undefined },
    { operation: 'surface.frame', target: frame.streamId, payload: { afterSequence: -1 } },
    { operation: 'surface.close', target: frame.streamId, payload: undefined },
  ]);
  assert.equal(JSON.stringify(commands).includes('HWND'), false);
});

test('surface input converts Three.js UV coordinates into normalized Windows intents', async () => {
  const commands: Array<{ operation: string; target?: string; payload?: unknown }> = [];
  const socket = {
    async sendCommand(operation: string, target?: string, payload?: unknown): Promise<unknown> {
      commands.push({ operation, target, payload });
      return {};
    },
  };
  const sink = new ProtocolWindowInputSink(socket, 'pc.window:pc.application:test');

  await sink.pointer('down', 0.25, 0.75, 'primary');
  await sink.wheel(0.5, 0.25, 0, 120);
  await sink.key('down', 'Enter');
  await sink.text('hello');

  assert.deepEqual(commands, [
    {
      operation: 'window.input',
      target: 'pc.window:pc.application:test',
      payload: { kind: 'pointer', phase: 'down', x: 0.25, y: 0.25, button: 'primary' },
    },
    {
      operation: 'window.input',
      target: 'pc.window:pc.application:test',
      payload: { kind: 'wheel', x: 0.5, y: 0.75, deltaX: 0, deltaY: 120 },
    },
    {
      operation: 'window.input',
      target: 'pc.window:pc.application:test',
      payload: { kind: 'key', phase: 'down', key: 'Enter' },
    },
    {
      operation: 'window.input',
      target: 'pc.window:pc.application:test',
      payload: { kind: 'text', text: 'hello' },
    },
  ]);
});

test('surface input preserves ordering while coalescing queued pointer moves', async () => {
  const commands: Array<{ operation: string; target?: string; payload?: unknown }> = [];
  const releases: Array<(value: unknown) => void> = [];
  const socket = {
    sendCommand(operation: string, target?: string, payload?: unknown): Promise<unknown> {
      commands.push({ operation, target, payload });
      return new Promise((resolve) => releases.push(resolve));
    },
  };
  const sink = new ProtocolWindowInputSink(socket, 'pc.window:pc.application:test');

  const down = sink.pointer('down', 0.1, 0.9, 'primary');
  await new Promise((resolve) => setImmediate(resolve));
  const firstMove = sink.pointer('move', 0.2, 0.8);
  const latestMove = sink.pointer('move', 0.25, 0.75);
  const up = sink.pointer('up', 0.25, 0.75, 'primary');

  assert.equal(commands.length, 1);
  releases.shift()?.({});
  await down;
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(commands[1]?.payload, {
    kind: 'pointer',
    phase: 'move',
    x: 0.25,
    y: 0.25,
  });

  releases.shift()?.({});
  await Promise.all([firstMove, latestMove]);
  await new Promise((resolve) => setImmediate(resolve));
  assert.deepEqual(commands[2]?.payload, {
    kind: 'pointer',
    phase: 'up',
    x: 0.25,
    y: 0.25,
    button: 'primary',
  });
  releases.shift()?.({});
  await up;
});

test('application surface forwards changing frames to its texture target', async () => {
  const receivedSequences: number[] = [];
  const textureTarget: SurfaceTextureTarget = {
    async update(frame) {
      receivedSequences.push(frame.sequence);
    },
    markUnavailable() {},
    dispose() {},
  };
  const frames: SurfaceFrame[] = [
    { streamId: 'stream-1', sequence: 1, width: 2, height: 2, mimeType: 'image/png', dataBase64: 'one' },
    { streamId: 'stream-1', sequence: 2, width: 2, height: 2, mimeType: 'image/png', dataBase64: 'two' },
  ];
  const stream = {
    async open() {},
    async readFrame() {
      return frames.shift() ?? null;
    },
    async close() {},
  };
  const surface = new ApplicationSurface(stream, textureTarget, { frameIntervalMs: 0 });

  await surface.renderNextFrame();
  await surface.renderNextFrame();
  await surface.dispose();

  assert.deepEqual(receivedSequences, [1, 2]);
});

test('application surface geometry lets persistent presentation own its displayed size', () => {
  const target = new ThreeSurfaceTextureTarget();

  assert.equal(target.object.geometry.parameters.width, 1);
  assert.equal(target.object.geometry.parameters.height, 1);

  target.dispose();
});

test('texture target maps hover UV onto a visible surface cursor and can hide it', () => {
  const target = new ThreeSurfaceTextureTarget();

  assert.equal(target.cursorVisible, false);

  target.setCursor(0.75, 0.25);
  assert.equal(target.cursorVisible, true);
  assert.deepEqual(
    { x: target.cursorPosition.x, y: target.cursorPosition.y },
    { x: 0.25, y: -0.25 },
  );

  target.setCursor(null);
  assert.equal(target.cursorVisible, false);

  target.dispose();
});

test('presentation sink sends the complete presentation through semantic identity', async () => {
  const commands: Array<{ operation: string; target?: string; payload?: unknown }> = [];
  const presentation: PresentationState = {
    position: { x: 3, y: 1.5, z: -2 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 4.2, y: 2.4, z: 1 },
    representation: 'application-surface',
  };
  const sink = new ProtocolPresentationSink({
    async sendCommand(operation, target, payload): Promise<unknown> {
      commands.push({ operation, target, payload });
      return {};
    },
  }, 'pc.window:pc.application:test');

  await sink.setPresentation(presentation);

  assert.deepEqual(commands, [{
    operation: 'entity.setPresentation',
    target: 'pc.window:pc.application:test',
    payload: presentation,
  }]);
});

test('failed presentation persistence reverts the local surface preview', async () => {
  const initial: PresentationState = {
    position: { x: 0, y: 1.4, z: -3 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 3.2, y: 1.8, z: 1 },
    representation: 'application-surface',
  };
  const moved: PresentationState = {
    ...initial,
    position: { x: 2, y: 2.2, z: -3 },
    size: { x: 4, y: 2.25, z: 1 },
  };
  const applied: PresentationState[] = [];
  const stream = {
    async open(): Promise<void> {},
    async readFrame(): Promise<null> { return null; },
    async close(): Promise<void> {},
  };
  const target: SurfaceTextureTarget = {
    update() {},
    markUnavailable() {},
    setPresentation(presentation) { applied.push(presentation); },
    dispose() {},
  };
  const surface = new ApplicationSurface(stream, target, {
    initialPresentation: initial,
    presentationSink: {
      async setPresentation(): Promise<never> {
        throw new Error('persistence failed');
      },
    },
  });

  await assert.rejects(surface.commitPresentation(moved), /persistence failed/);

  assert.deepEqual(applied, [initial, moved, initial]);
  assert.deepEqual(surface.presentation, initial);
});

test('successful presentation preview waits for the authoritative host event', async () => {
  const initial: PresentationState = {
    position: { x: 0, y: 1.4, z: -3 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 3.2, y: 1.8, z: 1 },
    representation: 'application-surface',
  };
  const resized: PresentationState = {
    ...initial,
    size: { x: 4.5, y: 2.5, z: 1 },
  };
  const committed: PresentationState[] = [];
  const surface = new ApplicationSurface(
    {
      async open(): Promise<void> {},
      async readFrame(): Promise<null> { return null; },
      async close(): Promise<void> {},
    },
    {
      update() {},
      markUnavailable() {},
      setPresentation() {},
      dispose() {},
    },
    {
      initialPresentation: initial,
      presentationSink: {
        async setPresentation(presentation): Promise<void> {
          committed.push(presentation);
        },
      },
    },
  );

  await surface.commitPresentation(resized);

  assert.deepEqual(committed, [resized]);
  assert.deepEqual(surface.presentation, initial);
  surface.acceptAuthoritativePresentation(resized);
  assert.deepEqual(surface.presentation, resized);
});

test('a second presentation edit accumulates from the pending displayed preview', async () => {
  const initial: PresentationState = {
    position: { x: 0, y: 1.4, z: -3 },
    rotation: { x: 0, y: 0, z: 0, w: 1 },
    size: { x: 3.2, y: 1.8, z: 1 },
    representation: 'application-surface',
  };
  const committed: PresentationState[] = [];
  const surface = new ApplicationSurface(
    {
      async open(): Promise<void> {},
      async readFrame(): Promise<null> { return null; },
      async close(): Promise<void> {},
    },
    {
      update() {},
      markUnavailable() {},
      setPresentation() {},
      dispose() {},
    },
    {
      initialPresentation: initial,
      presentationSink: {
        async setPresentation(presentation): Promise<void> {
          committed.push(presentation);
        },
      },
    },
  );

  const first = {
    ...surface.displayedPresentation,
    position: { ...surface.displayedPresentation.position, x: 0.2 },
  };
  const firstCommit = surface.commitPresentation(first);
  const second = {
    ...surface.displayedPresentation,
    position: { ...surface.displayedPresentation.position, x: 0.4 },
  };
  const secondCommit = surface.commitPresentation(second);
  await Promise.all([firstCommit, secondCommit]);

  assert.deepEqual(committed, [first, second]);
  assert.deepEqual(surface.presentation, initial);
  assert.deepEqual(surface.displayedPresentation, second);
});

test('surface stream distinguishes no new frame from an unavailable capture', async () => {
  let frameResult: unknown = { available: true, frame: null };
  const socket = {
    async sendCommand(operation: string): Promise<unknown> {
      if (operation === 'surface.open') {
        return { streamId: 'stream-1', width: 800, height: 600 };
      }
      return frameResult;
    },
  };
  const stream = new ProtocolSurfaceStream(socket, 'pc.window:pc.application:test');
  await stream.open();

  assert.equal(await stream.readFrame(), null);

  frameResult = { available: false };
  await assert.rejects(stream.readFrame(), /surface capture is unavailable/i);
});

test('application surface recovers when a semantic window becomes capturable later', async () => {
  let openAttempts = 0;
  let resolveRendered!: () => void;
  const rendered = new Promise<void>((resolve) => {
    resolveRendered = resolve;
  });
  const stream = {
    async open() {
      openAttempts++;
      if (openAttempts === 1) throw new Error('Window is not currently available.');
    },
    async readFrame(): Promise<SurfaceFrame> {
      return {
        streamId: 'stream-recovered',
        sequence: 1,
        width: 2,
        height: 2,
        mimeType: 'image/png',
        dataBase64: 'recovered',
      };
    },
    async close() {},
  };
  const textureTarget: SurfaceTextureTarget = {
    update() {
      resolveRendered();
    },
    markUnavailable() {},
    dispose() {},
  };
  const surface = new ApplicationSurface(stream, textureTarget, {
    frameIntervalMs: 1,
    retryIntervalMs: 1,
  });

  surface.start();
  await Promise.race([
    rendered,
    new Promise<never>((_, reject) => setTimeout(
      () => reject(new Error('Surface did not recover.')),
      100,
    )),
  ]);
  await surface.dispose();

  assert.equal(openAttempts, 2);
});

test('a blank display surface remains selectable without opening capture or input', async () => {
  let opened = 0;
  let inputCalls = 0;
  const surface = new ApplicationSurface({
    async open() { opened += 1; },
    async readFrame() { return null; },
    async close() {},
  }, {
    update() {},
    markUnavailable() {},
    dispose() {},
  }, {
    inputSink: {
      async pointer() { inputCalls += 1; },
      async wheel() { inputCalls += 1; },
      async key() { inputCalls += 1; },
      async text() { inputCalls += 1; },
    },
    boundWindowId: null,
    initialPresentation: {
      position: { x: 0, y: 0, z: 0 },
      rotation: { x: 0, y: 0, z: 0, w: 1 },
      size: { x: 1, y: 1, z: 1 },
    },
  });

  surface.start();
  await new Promise((resolve) => setTimeout(resolve, 0));
  await surface.pointer('down', 0.5, 0.5, 'primary');

  assert.equal(surface.isBound, false);
  assert.equal(opened, 0);
  assert.equal(inputCalls, 0);
  await surface.dispose();
});
