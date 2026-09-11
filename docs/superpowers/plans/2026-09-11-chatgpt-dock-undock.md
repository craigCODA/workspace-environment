# ChatGPT Dock / Undock Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Present the one real Windows ChatGPT surface as either a camera-anchored right-side panel or its normal durable spatial surface, with collapse/show/focus controls and no duplicate ChatGPT process/window.

**Architecture:** `ApplicationSurface` gains a transient presentation override that never mutates durable presentation state. `WorkspaceScene` owns generic renderer-local dock/collapse state and reparents the same Three.js object between scene and camera. Default ChatGPT startup exposes the stable surface/window IDs already returned by `application.open`; a compact generic surface-control strip maps UI actions onto those existing IDs.

**Tech Stack:** TypeScript, Three.js, DOM, Node test runner, existing WorkspaceCommandController/application-control protocol, Windows CI.

**Spec:** `docs/superpowers/specs/2026-09-11-chatgpt-dock-undock-design.md`

## Global Constraints

- Work on `pr/coda-grok-work`, never `main`.
- One ChatGPT process/window/surface identity only; docking never launches another instance.
- Durable world presentation remains host-authoritative and is never overwritten by camera-local dock coordinates.
- Dock/collapse mode is renderer-session state only in this slice.
- Docked surface remains the same capture stream and input sink.
- Alt-drag and Alt-arrow durable presentation edits are disabled for docked surfaces.
- Focus uses the existing typed `window.focus` operation.
- The generic scene primitives must be reusable for applications other than ChatGPT later.

---

### Task 1: Transient ApplicationSurface presentation override

**Files:**
- Modify: `apps/spatial-client/src/surfaces/ApplicationSurface.ts`
- Modify: `apps/spatial-client/src/surfaces/ApplicationSurface.test.ts`

**Interfaces:**
- Produces: `ApplicationSurface.setTransientPresentation(presentation: PresentationState | null): void`.
- Durable `presentation` and `displayedPresentation` semantics remain unchanged.

- [ ] **Step 1: Add failing tests**

Test that a transient presentation is sent to the texture target without changing `displayedPresentation`, authoritative updates received while overridden update durable state, and clearing the override reapplies the latest durable presentation.

- [ ] **Step 2: Verify RED**

Expected: `setTransientPresentation` does not exist.

- [ ] **Step 3: Implement minimal override state**

Add `#transientPresentation: PresentationState | null = null` and a private `#applyPresentation()` helper. Persistent preview/authoritative updates mutate durable fields but call `#applyPresentation`; the helper sends the transient presentation when present, otherwise the durable displayed presentation.

- [ ] **Step 4: Verify TypeScript tests/typecheck green**

### Task 2: Generic scene dock/collapse state

**Files:**
- Modify: `apps/spatial-client/src/rendering/WorkspaceScene.ts`
- Modify: `apps/spatial-client/src/rendering/WorkspaceScene.test.ts`

**Interfaces:**
- Produces: `dockedSurfacePresentation(aspect: number): PresentationState` pure helper.
- Produces: `scene.setSurfaceDocked(entityId: string, docked: boolean): void`.
- Produces: `scene.isSurfaceDocked(entityId: string): boolean`.
- Produces: `scene.setSurfaceCollapsed(entityId: string, collapsed: boolean): void`.
- Produces: `scene.isSurfaceCollapsed(entityId: string): boolean`.

- [ ] **Step 1: Add failing geometry test**

Verify the calculated presentation uses negative camera-local z, positive x, identity rotation, positive size, and stays within horizontal camera bounds at 16:9 and 4:3.

- [ ] **Step 2: Add failing dock lifecycle test**

Using the existing fake renderer/observer setup, upsert one spatial surface, dock it, assert the same surface remains in the snapshot with unchanged durable presentation and `isSurfaceDocked === true`; undock and assert false without creating another stream/input binding.

- [ ] **Step 3: Verify RED**

Expected: dock helpers/methods missing.

- [ ] **Step 4: Implement dock/collapse**

Use sets for desired dock/collapse state. Reparent the same object with `removeFromParent()` and `camera.add()`/`scene.add()`. Apply/clear `ApplicationSurface` transient presentation. Resize recomputes dock geometry. Collapsed controls only object visibility.

Update rebind/remove teardown to use `removeFromParent()` so camera-parented surfaces dispose correctly.

- [ ] **Step 5: Verify TypeScript tests/typecheck green**

### Task 3: Return stable ChatGPT window/surface identity from startup

**Files:**
- Modify: `apps/spatial-client/src/startup/DefaultApplicationStartup.ts`
- Modify: `apps/spatial-client/src/startup/DefaultApplicationStartup.test.ts`

**Interfaces:**
- On success result may include `applicationId`, `windowEntityId`, `surfaceEntityId`.

- [ ] **Step 1: Tighten failing success test**

Provide an open payload containing the three IDs and assert `openDefaultChatGpt()` returns them with `status: 'opened'`.

- [ ] **Step 2: Verify RED**

Expected: existing helper returns only `{ status: 'opened' }`.

- [ ] **Step 3: Parse only stable string IDs from the existing open payload**

Malformed optional IDs are omitted rather than guessed. `applicationId` is the already-resolved ID used in the request and is always returned on successful open.

- [ ] **Step 4: Verify TypeScript tests/typecheck green**

### Task 4: Generic surface dock controls

**Files:**
- Create: `apps/spatial-client/src/surfaces/SurfaceDockControls.ts`
- Create: `apps/spatial-client/src/surfaces/SurfaceDockControls.test.ts`
- Modify: `apps/spatial-client/src/styles.css`

**Interfaces:**
- Produces: `SurfaceViewMode = 'spatial' | 'docked' | 'collapsed'`.
- Produces: pure `surfaceDockControlState(mode)` describing Dock/Undock and Collapse/Show labels/actions.
- Produces: `SurfaceDockControls` DOM class with `setMode()` and `destroy()`.
- Options callbacks: `onDock`, `onUndock`, `onCollapse`, `onShow`, optional `onFocus`.

- [ ] **Step 1: Write failing pure model tests**

Assert label/action mappings for all three modes.

- [ ] **Step 2: Verify RED**

Expected missing module.

- [ ] **Step 3: Implement pure model and DOM control strip**

Render a compact `ChatGPT`-labeled strip near Coda controls, with primary Dock/Undock, Collapse/Show, and optional Focus buttons.

- [ ] **Step 4: Style controls consistently with Coda without merging their semantic ownership**

- [ ] **Step 5: Verify TypeScript tests/typecheck green**

### Task 5: Wire resolved ChatGPT surface to dock controls

**Files:**
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.test.ts`

**Interfaces:**
- Consumes successful startup IDs from Task 3.
- Consumes WorkspaceScene dock/collapse methods from Task 2.
- Consumes SurfaceDockControls from Task 4.

- [ ] **Step 1: Add pure guard helper and failing test**

Export `canEditDurablePresentation(surfaceEntityId: string, isDocked: (id: string) => boolean): boolean` and assert docked surfaces return false.

- [ ] **Step 2: Verify RED**

- [ ] **Step 3: Wire startup success**

On `openDefaultChatGpt()` success with `surfaceEntityId`:

- dock the surface immediately;
- construct `SurfaceDockControls` labeled ChatGPT;
- Dock/Undock calls scene methods and updates control mode;
- Collapse hides the surface while preserving prior visible mode; Show restores it;
- Focus sends `{ id, command: 'window.focus', args: { windowEntityId } }` through `workspaceCommands.handle()` when the ID is available.

Destroy controls on workspace teardown.

- [ ] **Step 4: Gate durable presentation edits**

Alt-drag and Alt-arrow presentation changes only run when the selected surface entity is not docked.

- [ ] **Step 5: Verify full Windows CI green**

### Task 6: Acceptance documentation

**Files:**
- Modify: `docs/v0-acceptance.md`

- [ ] **Step 1: Add manual acceptance checks**

```text
1. Start Workspace with ChatGPT available: the one resolved ChatGPT surface appears docked at the right edge.
2. Click Undock: the same live surface returns to its durable world position without launching another ChatGPT window.
3. Move the spatial surface, Dock, then Undock: the moved durable presentation returns exactly.
4. Collapse and Show: visibility changes without closing/minimizing ChatGPT or changing identity.
5. Focus activates the same real ChatGPT window through the typed window.focus operation.
6. Input, wheel, and keyboard routing work in both docked and spatial modes.
7. Alt-drag/Alt-arrow do not persist camera-local dock coordinates while docked.
```

- [ ] **Step 2: Run final full Windows CI and record the verified head**
