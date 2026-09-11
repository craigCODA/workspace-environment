import assert from 'node:assert/strict';
import test from 'node:test';
import { openDefaultChatGpt } from './DefaultApplicationStartup.ts';

class FakeHandler {
  readonly requests: unknown[];
  readonly responses: unknown[];

  constructor(...responses: unknown[]) {
    this.requests = [];
    this.responses = [...responses];
  }

  async handle(value: unknown): Promise<any> {
    this.requests.push(value);
    return this.responses.shift();
  }
}

test('resolved ChatGPT search opens the stable application with reuseOrLaunch', async () => {
  const handler = new FakeHandler(
    {
      id: 'startup-chatgpt-search',
      ok: true,
      payload: {
        status: 'resolved',
        application: { id: 'pc.application:chatgpt', displayName: 'ChatGPT' },
        candidates: [],
      },
    },
    {
      id: 'startup-chatgpt-open',
      ok: true,
      payload: {
        disposition: 'reused',
        surfaceEntityId: 'spatial.surface:chatgpt',
        windowEntityId: 'pc.window:chatgpt',
      },
    },
  );

  const result = await openDefaultChatGpt(handler);

  assert.deepEqual(result, {
    status: 'opened',
    surfaceEntityId: 'spatial.surface:chatgpt',
    windowEntityId: 'pc.window:chatgpt',
  });
  assert.deepEqual(handler.requests, [
    {
      id: 'startup-chatgpt-search',
      command: 'application.search',
      args: { query: 'ChatGPT', limit: 5 },
    },
    {
      id: 'startup-chatgpt-open',
      command: 'application.open',
      args: { applicationId: 'pc.application:chatgpt', launchPolicy: 'reuseOrLaunch' },
    },
  ]);
});

test('opened ChatGPT tolerates a host that omits optional semantic ids', async () => {
  const handler = new FakeHandler(
    {
      id: 'startup-chatgpt-search',
      ok: true,
      payload: {
        status: 'resolved',
        application: { id: 'pc.application:chatgpt', displayName: 'ChatGPT' },
        candidates: [],
      },
    },
    {
      id: 'startup-chatgpt-open',
      ok: true,
      payload: { disposition: 'launched' },
    },
  );

  assert.deepEqual(await openDefaultChatGpt(handler), {
    status: 'opened',
    surfaceEntityId: null,
    windowEntityId: null,
  });
});

test('missing or ambiguous ChatGPT performs no open', async () => {
  for (const status of ['notFound', 'ambiguous']) {
    const handler = new FakeHandler({
      id: 'startup-chatgpt-search',
      ok: true,
      payload: { status, application: null, candidates: [] },
    });

    const result = await openDefaultChatGpt(handler);

    assert.equal(result.status, 'unavailable');
    assert.equal(handler.requests.length, 1);
  }
});

test('malformed or failed startup commands never throw into workspace initialization', async () => {
  const malformed = new FakeHandler({
    id: 'startup-chatgpt-search',
    ok: true,
    payload: { status: 'resolved', application: {} },
  });
  assert.equal((await openDefaultChatGpt(malformed)).status, 'failed');

  const throwing = {
    async handle(): Promise<any> {
      throw new Error('host unavailable');
    },
  };
  assert.equal((await openDefaultChatGpt(throwing)).status, 'failed');
});
