# Default ChatGPT Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open the installed Windows ChatGPT application automatically after Workspace Environment becomes ready, reusing an existing real window/surface when possible and failing quietly when unavailable.

**Architecture:** Add a small spatial-client startup helper that talks only to the existing `WorkspaceCommandController.handle()` interface. It performs a typed `application.search` for `ChatGPT`, opens only a uniquely resolved stable application ID with `reuseOrLaunch`, and converts search/open failures into a non-throwing startup status. `createWorkspaceApp.ts` invokes it after `renderer.ready` without blocking workspace initialization.

**Tech Stack:** TypeScript, Node test runner, existing WorkspaceCommandController/application-control protocol, existing GitHub Windows CI.

**Spec:** `docs/superpowers/specs/2026-09-11-default-chatgpt-surface-design.md`

## Global Constraints

- Work on `pr/coda-grok-work`, never `main`.
- Use the existing `application.search` and `application.open` operations only.
- The startup query is exactly `ChatGPT`.
- Open only a uniquely resolved stable application ID.
- Launch policy is exactly `reuseOrLaunch`.
- Never send executable paths, package locators, shell commands, arguments, or credentials.
- Do not block or fail Workspace startup when ChatGPT is missing, ambiguous, or cannot open.
- Do not implement dock/undock behavior in this slice.

---

### Task 1: Default application startup helper

**Files:**
- Create: `apps/spatial-client/src/startup/DefaultApplicationStartup.test.ts`
- Create: `apps/spatial-client/src/startup/DefaultApplicationStartup.ts`

**Interfaces:**
- Consumes: a minimal `WorkspaceCommandHandler` with `handle(value: unknown): Promise<WorkspaceCommandResult>`.
- Produces: `openDefaultChatGpt(handler): Promise<DefaultApplicationStartupResult>`.
- `DefaultApplicationStartupResult.status` is `opened | unavailable | failed`.

- [ ] **Step 1: Write the failing resolved-search test**

```ts
import assert from 'node:assert/strict';
import test from 'node:test';
import { openDefaultChatGpt } from './DefaultApplicationStartup.ts';

class FakeHandler {
  readonly requests: unknown[] = [];
  readonly responses: unknown[];

  constructor(...responses: unknown[]) {
    this.responses.push(...responses);
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
      payload: { disposition: 'reused', surfaceEntityId: 'spatial.surface:chatgpt' },
    },
  );

  const result = await openDefaultChatGpt(handler);

  assert.equal(result.status, 'opened');
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
```

- [ ] **Step 2: Push the failing test and verify CI fails because the startup module does not exist**

- [ ] **Step 3: Add failure/ambiguity tests before implementation**

```ts
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
    id: 'startup-chatgpt-search', ok: true, payload: { status: 'resolved', application: {} },
  });
  assert.equal((await openDefaultChatGpt(malformed)).status, 'failed');

  const throwing = {
    async handle(): Promise<any> {
      throw new Error('host unavailable');
    },
  };
  assert.equal((await openDefaultChatGpt(throwing)).status, 'failed');
});
```

- [ ] **Step 4: Implement the minimal helper**

```ts
import type { WorkspaceCommandResult } from '../navigation/WorkspaceCommandController.ts';

export type WorkspaceCommandHandler = Readonly<{
  handle(value: unknown): Promise<WorkspaceCommandResult>;
}>;

export type DefaultApplicationStartupResult = Readonly<{
  status: 'opened' | 'unavailable' | 'failed';
}>;

function record(value: unknown): Record<string, unknown> | null {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}

export async function openDefaultChatGpt(
  handler: WorkspaceCommandHandler,
): Promise<DefaultApplicationStartupResult> {
  try {
    const search = await handler.handle({
      id: 'startup-chatgpt-search',
      command: 'application.search',
      args: { query: 'ChatGPT', limit: 5 },
    });
    if (!search.ok) return { status: 'failed' };
    const searchPayload = record(search.payload);
    if (!searchPayload) return { status: 'failed' };
    if (searchPayload.status !== 'resolved') return { status: 'unavailable' };
    const application = record(searchPayload.application);
    const applicationId = application?.id;
    if (typeof applicationId !== 'string' || applicationId.trim().length === 0) {
      return { status: 'failed' };
    }

    const opened = await handler.handle({
      id: 'startup-chatgpt-open',
      command: 'application.open',
      args: { applicationId, launchPolicy: 'reuseOrLaunch' },
    });
    return { status: opened.ok ? 'opened' : 'failed' };
  } catch {
    return { status: 'failed' };
  }
}
```

- [ ] **Step 5: Verify TypeScript tests and typecheck pass**

Expected: new startup tests pass and existing application-control tests remain green.

### Task 2: Invoke startup after renderer readiness

**Files:**
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.test.ts`

**Interfaces:**
- Consumes: `openDefaultChatGpt(workspaceCommands)` from Task 1.
- Produces: one fire-and-forget default ChatGPT startup attempt after host connection and `renderer.ready`.

- [ ] **Step 1: Extract and test post-connection startup sequencing**

Add exported helper:

```ts
export async function initializeReadyWorkspace(
  socket: InitialSyncSocket,
  onReady: () => void,
  openDefaultApplication: () => Promise<unknown>,
): Promise<void> {
  await initializeWorkspaceConnection(socket);
  onReady();
  void openDefaultApplication();
}
```

Test:

```ts
test('workspace readiness is posted before optional default application startup', async () => {
  const calls: string[] = [];
  const socket = {
    async waitUntilOpen(): Promise<void> { calls.push('connected'); },
    async sendCommand(operation: string): Promise<void> { calls.push(operation); },
  };

  await initializeReadyWorkspace(
    socket,
    () => calls.push('ready'),
    async () => { calls.push('default-app'); },
  );
  await new Promise<void>((done) => queueMicrotask(done));

  assert.deepEqual(calls, ['connected', 'application.list', 'ready', 'default-app']);
});
```

- [ ] **Step 2: Push and verify the sequencing test fails because `initializeReadyWorkspace` is missing**

- [ ] **Step 3: Implement the helper and use it in `createWorkspaceApp`**

Import `openDefaultChatGpt` and replace the existing connection chain with:

```ts
void initializeReadyWorkspace(
  socket,
  () => bridge.post('renderer.ready', { surface: 'spatial', version: 1 }),
  () => openDefaultChatGpt(workspaceCommands),
).catch((error) => {
  const message = error instanceof Error ? error.message : String(error);
  coda.setState('needs-attention');
  coda.showCaption(`Windows Workspace Host is not connected. ${message}`);
  coda.setState('needs-attention');
});
```

The helper does not await the optional application startup, so ChatGPT failure cannot enter this host-health catch path.

- [ ] **Step 4: Verify full Windows CI**

Expected green steps:

- TypeScript tests
- TypeScript typecheck
- Windows host tests
- native Coda core tests
- native Windows preview build
- Windows installer build
- installer artifact upload

### Task 3: Acceptance note

**Files:**
- Modify: `docs/v0-acceptance.md`

**Interfaces:**
- Produces: manual Windows checks for startup behavior.

- [ ] **Step 1: Add acceptance checks**

```text
1. With ChatGPT already open, start Workspace Environment: the existing ChatGPT window is reused and shown through its durable spatial surface.
2. With ChatGPT installed but closed, start Workspace Environment: ChatGPT launches and its real window appears as a spatial surface.
3. Close Workspace Environment, restart it with the same ChatGPT window still open: the existing durable ChatGPT surface identity/presentation is reused.
4. On a machine without ChatGPT installed, Workspace Environment still reaches its normal ready state and remains usable.
```

- [ ] **Step 2: Run the final full Windows CI gate and record the head status**
