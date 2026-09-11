import assert from 'node:assert/strict';
import test from 'node:test';
import { RendererRegistry } from './RendererRegistry.ts';

test('maps pc.window to the application-surface renderer', () => {
  const registry = new RendererRegistry();

  assert.equal(registry.resolve('pc.window').kind, 'application-surface');
});

test('maps durable spatial surfaces to the application-surface renderer', () => {
  const registry = new RendererRegistry();

  assert.equal(registry.resolve('spatial.surface').kind, 'application-surface');
});

test('falls back to a semantic marker without inventing a window surface', () => {
  const registry = new RendererRegistry();

  assert.equal(registry.resolve('workspace.project').kind, 'semantic-marker');
});
