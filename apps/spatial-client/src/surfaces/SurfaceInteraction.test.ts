import assert from 'node:assert/strict';
import test from 'node:test';
import {
  ProtocolSurfaceStream,
  type SurfaceFrame,
} from './SurfaceStream.ts';
import {
  ApplicationSurface,
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
