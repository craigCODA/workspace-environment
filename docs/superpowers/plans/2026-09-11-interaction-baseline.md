# Workspace Interaction Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace drag-to-look with true Pointer Lock navigation, keep the native cursor visible during surface interaction, start Coda chat collapsed, and expose agent provider selection as a visible selector.

**Architecture:** Keep application-surface input routing intact and isolate camera capture behind a small testable pointer-lock state helper. Coda continues using the existing preference envelope; only the UI control and initial chat visibility change.

**Tech Stack:** TypeScript, DOM Pointer Lock API, Node test runner, Vite, existing Coda/WorkspaceScene code.

**Spec:** `docs/superpowers/specs/2026-09-11-interaction-baseline-design.md`

## Global Constraints

- Work on `pr/coda-grok-work`, never `main`.
- Preserve application-surface pointer, wheel, keyboard, move, resize, and persistence behavior.
- Pointer Lock may start only from a primary click on empty workspace.
- The native pointer is hidden only while the workspace owns Pointer Lock.
- Coda provider values remain exactly `Codex`, `SpaceXAI`, and `Cursor`.
- Cursor provider runtime fallback behavior remains unchanged.
- Use test-first changes and verify the PR CI after each red/green phase.

---

### Task 1: Pointer Lock state helper

**Files:**
- Create: `apps/spatial-client/src/navigation/PointerLockLookController.ts`
- Create: `apps/spatial-client/src/navigation/PointerLockLookController.test.ts`

**Interfaces:**
- Produces: `PointerLockLookController` with `setLocked(locked: boolean)`, `move(movementX: number, movementY: number)`, and `locked` getter.
- Consumes: callback `(movementX: number, movementY: number) => void` supplied by `createWorkspaceApp`.

- [ ] **Step 1: Write the failing test**

```ts
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
```

- [ ] **Step 2: Push the failing test and verify CI fails**

Expected failure: module `PointerLockLookController.ts` does not exist.

- [ ] **Step 3: Implement the minimal helper**

```ts
export class PointerLockLookController {
  #locked = false;
  readonly #look: (movementX: number, movementY: number) => void;

  constructor(look: (movementX: number, movementY: number) => void) {
    this.#look = look;
  }

  get locked(): boolean {
    return this.#locked;
  }

  setLocked(locked: boolean): void {
    this.#locked = locked;
  }

  move(movementX: number, movementY: number): void {
    if (this.#locked) this.#look(movementX, movementY);
  }
}
```

- [ ] **Step 4: Verify CI passes the new helper test**

Expected: spatial-client tests and typecheck pass.

### Task 2: Wire true Pointer Lock into the workspace

**Files:**
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.test.ts`
- Modify: `apps/spatial-client/src/styles.css`

**Interfaces:**
- Consumes: `PointerLockLookController` from Task 1.
- Produces: empty-workspace click requests `root.requestPointerLock()`, `pointerlockchange` synchronizes state, and relative movement drives camera look.

- [ ] **Step 1: Add a failing pure behavior test for lock eligibility**

Add and export:

```ts
export function shouldRequestPointerLock(input: {
  primaryButton: boolean;
  interactiveUi: boolean;
  surfaceHit: boolean;
}): boolean;
```

Test:

```ts
test('camera capture is reserved for primary clicks on empty workspace', () => {
  assert.equal(shouldRequestPointerLock({ primaryButton: true, interactiveUi: false, surfaceHit: false }), true);
  assert.equal(shouldRequestPointerLock({ primaryButton: false, interactiveUi: false, surfaceHit: false }), false);
  assert.equal(shouldRequestPointerLock({ primaryButton: true, interactiveUi: true, surfaceHit: false }), false);
  assert.equal(shouldRequestPointerLock({ primaryButton: true, interactiveUi: false, surfaceHit: true }), false);
});
```

- [ ] **Step 2: Push and verify the test fails because `shouldRequestPointerLock` is missing**

- [ ] **Step 3: Implement lock eligibility and browser wiring**

In `createWorkspaceApp.ts`:

- import `PointerLockLookController`.
- remove the empty-space `pointerId`, `lastX`, `lastY`, and pointer-capture drag-to-look state.
- after surface/UI branches, request Pointer Lock only when `shouldRequestPointerLock(...)` is true.
- listen for `document.pointerlockchange`.
- set controller lock state from `document.pointerLockElement === root`.
- add/remove `is-looking` from the same condition.
- while locked, route `event.movementX` and `event.movementY` through the controller before hover processing.
- do not end camera look on pointerup.
- on destroy, remove `pointerlockchange`; call `document.exitPointerLock()` if the root owns the lock.
- do not `preventDefault()` Escape while the root owns Pointer Lock; allow the browser to release it, while still clearing selected surfaces once normal keyboard handling resumes.

- [ ] **Step 4: Change cursor CSS and movement hint**

Remove:

```css
.workspace-root.is-surface-hover,
.workspace-root.is-surface-input.is-surface-hover {
  cursor: none;
}
```

Use:

```css
.workspace-root.is-looking {
  cursor: none;
}
```

Update the hint to:

```text
Click empty space to look around. Press Escape to release the pointer. Use W A S D to move. Alt-drag a surface to move; add Shift to resize.
```

- [ ] **Step 5: Verify full CI passes**

Expected: TypeScript tests, typecheck, Windows host tests, native tests/build, and installer build all pass.

### Task 3: Compact Coda startup

**Files:**
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.ts`
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.test.ts`

**Interfaces:**
- Produces: exported `createInitialCodaPresenceModel()` returning a fresh initial model with `chatVisible: false`.

- [ ] **Step 1: Write the failing test**

```ts
import { createInitialCodaPresenceModel } from './CodaPresence.ts';

test('Coda starts compact with chat collapsed', () => {
  const model = createInitialCodaPresenceModel();
  assert.equal(model.chatVisible, false);
  assert.equal(model.terminalVisible, false);
  assert.equal(model.microphoneEnabled, true);
});
```

- [ ] **Step 2: Push and verify failure because the initializer is missing**

- [ ] **Step 3: Implement the initializer and use it for class state**

```ts
export function createInitialCodaPresenceModel(): CodaPresenceModel {
  return {
    state: 'waiting',
    caption: '',
    microphoneEnabled: true,
    captionsEnabled: true,
    transcriptVisible: false,
    terminalVisible: false,
    chatVisible: false,
    proactiveMode: 'CriticalOnly',
    agentProvider: 'Codex',
    terminalEvents: [],
  };
}
```

Initialize `#model` from this function.

- [ ] **Step 4: Verify CI passes**

### Task 4: Replace provider cycling with an explicit selector

**Files:**
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.ts`
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.test.ts`
- Modify: `apps/spatial-client/src/styles.css`

**Interfaces:**
- Produces: `AGENT_PROVIDER_OPTIONS` records with stable `value` and visible `label`.
- UI emits the existing `{ agentProvider }` preference payload.

- [ ] **Step 1: Write the failing options test**

```ts
import { AGENT_PROVIDER_OPTIONS } from './CodaPresence.ts';

test('agent provider selector exposes stable values and honest labels', () => {
  assert.deepEqual(AGENT_PROVIDER_OPTIONS, [
    { value: 'Codex', label: 'Codex' },
    { value: 'SpaceXAI', label: 'SpaceXAI' },
    { value: 'Cursor', label: 'Cursor (soon)' },
  ]);
});
```

- [ ] **Step 2: Push and verify failure because the options export is missing**

- [ ] **Step 3: Replace `#agentProviderButton` with `#agentProviderSelect`**

Create a label with text `Agent` and a native select populated from `AGENT_PROVIDER_OPTIONS`. On `change`, validate the selected value against `AGENT_PROVIDERS`, call `setPreferences({ agentProvider })`, and send `onPreferenceChange?.({ agentProvider })`.

In `#render()`, assign `this.#agentProviderSelect.value = this.#model.agentProvider`.

- [ ] **Step 4: Style the selector alongside existing Coda controls**

Keep the selector compact, keyboard accessible, and visually consistent with the current control pills without hiding the selected provider.

- [ ] **Step 5: Verify full CI passes**

Expected: all existing provider preference behavior remains green and the new options test passes.

### Task 5: Acceptance documentation

**Files:**
- Modify: `docs/v0-acceptance.md`

**Interfaces:**
- Consumes: completed behavior from Tasks 1-4.
- Produces: manual acceptance steps for pointer capture/release, cursor visibility, compact Coda startup, and provider selection.

- [ ] **Step 1: Add explicit acceptance steps**

Document these checks:

```text
1. Hover a live application surface: OS cursor remains visible.
2. Click empty workspace: pointer hides and mouse movement rotates the camera without holding a button.
3. Press Escape: pointer returns and surface/UI interaction resumes.
4. Restart the app: Coda chat is collapsed.
5. Open Agent selector: Codex, SpaceXAI, and Cursor (soon) are visible; changing selection persists through the existing preference path.
```

- [ ] **Step 2: Run final CI verification and record the head SHA/status in the PR update**
