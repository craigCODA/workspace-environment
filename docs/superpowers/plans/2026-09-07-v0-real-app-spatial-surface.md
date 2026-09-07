# V0 Real Application Spatial Surface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove that a real Windows application can be discovered, launched, captured, displayed as a live Three.js surface, interacted with, spatially repositioned, persisted, and restored without making the spatial client authoritative.

**Architecture:** A Windows 10/11 C#/.NET host owns semantic entities, application/window lifecycle, persistence, capture, and input. A TypeScript/raw-Three.js client maintains a replica over a versioned WebSocket protocol and renders host window streams as spatial surfaces. Microsoft Edge is the first human acceptance target; automated tests use fake adapters and a deterministic first-party test window so nothing is Edge-specific.

**Tech Stack:** .NET 8 / C# 12, ASP.NET Core WebSockets, Win32/WinRT boundaries, TypeScript 5, Vite, Three.js, Vitest, JSON Schema, npm workspaces.

**Spec:** `docs/superpowers/specs/2026-09-07-workspace-environment-design.md`

## Global Constraints

- Initial host platform is Windows 10/11.
- The Windows Workspace Host is authoritative; spatial clients are replaceable renderers.
- Three.js is never the world database.
- Workspace Entity identity must outlive process IDs, HWNDs, sockets, capture textures, and other transient runtime handles.
- Applications and windows are separate semantic entities.
- Presentation changes must not silently mutate underlying application/window bounds.
- The ordinary Workspace Host runs unelevated.
- No model-provider API is required.
- Agent control is not part of V0 implementation; only preserve the capability-oriented boundary.
- Quest 2/WebXR is preserved architecturally but is not required to complete the desktop capture proof.
- V0 is not complete until a real Windows application is live and usable inside the spatial workspace.
- No Edge-specific capture or input implementation is permitted; Edge is only the first human acceptance target.

---

## File Structure

```text
workspace-environment/
├── package.json
├── .gitignore
├── apps/
│   ├── host-windows/
│   │   ├── Workspace.Host.sln
│   │   ├── src/Workspace.Host/
│   │   │   ├── Domain/
│   │   │   ├── Persistence/
│   │   │   ├── Applications/
│   │   │   ├── Windows/
│   │   │   ├── Protocol/
│   │   │   └── Program.cs
│   │   ├── src/Workspace.TestWindow/
│   │   └── tests/Workspace.Host.Tests/
│   └── spatial-client/
│       └── src/{app,protocol,replica,rendering,surfaces,onboarding,xr}/
├── packages/
│   ├── protocol/
│   ├── world-schema/
│   └── shared-fixtures/
└── docs/superpowers/{specs,plans}/
```

---

### Task 1: Monorepo contracts and semantic identity

**Files:**
- Create: `package.json`
- Create: `.gitignore`
- Create: `packages/protocol/package.json`
- Create: `packages/protocol/src/index.ts`
- Test: `packages/protocol/src/index.test.ts`
- Create: `packages/world-schema/package.json`
- Create: `packages/world-schema/src/index.ts`
- Test: `packages/world-schema/src/index.test.ts`
- Create: `packages/shared-fixtures/package.json`
- Create: `packages/shared-fixtures/src/index.ts`

**Interfaces:**
- Produces `PROTOCOL_VERSION = 1`.
- Produces protocol envelope unions for command/result/event/snapshot/error.
- Produces `WorkspaceEntity`, `PresentationState`, `HostBinding`, and `Relationship`.
- Produces `createEntityId(kind: string, stableKey: string): string`.

- [ ] **Step 1: Write the failing protocol test**

```ts
import { describe, expect, it } from 'vitest';
import { PROTOCOL_VERSION, isProtocolEnvelope } from './index';

describe('protocol envelope', () => {
  it('requires explicit protocol version 1', () => {
    expect(PROTOCOL_VERSION).toBe(1);
    expect(isProtocolEnvelope({ protocol: 1, type: 'event', event: 'ENTITY_CREATED', payload: {} })).toBe(true);
    expect(isProtocolEnvelope({ type: 'event', event: 'ENTITY_CREATED', payload: {} })).toBe(false);
  });
});
```

- [ ] **Step 2: Write the failing durable identity test**

```ts
import { describe, expect, it } from 'vitest';
import { createEntityId } from './index';

describe('durable entity ids', () => {
  it('is stable for the same semantic key', () => {
    expect(createEntityId('pc.application', 'Microsoft Edge'))
      .toBe(createEntityId('pc.application', 'Microsoft Edge'));
  });
});
```

- [ ] **Step 3: Run tests to verify RED**

Run: `npm install && npm test --workspaces --if-present`
Expected: FAIL because workspace packages/exports do not exist.

- [ ] **Step 4: Implement minimal contracts**

Root `package.json`:

```json
{
  "name": "workspace-environment",
  "private": true,
  "workspaces": ["apps/spatial-client", "packages/*"],
  "scripts": {
    "test": "npm test --workspaces --if-present",
    "typecheck": "npm run typecheck --workspaces --if-present",
    "build": "npm run build --workspaces --if-present"
  },
  "devDependencies": {
    "typescript": "^5.9.2",
    "vitest": "^3.2.4"
  }
}
```

`WorkspaceEntity` must contain semantic identity and presentation but no PID/HWND/stream identifier fields.

- [ ] **Step 5: Run tests to verify GREEN**

Run: `npm test --workspaces --if-present`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add package.json package-lock.json .gitignore packages
git commit -m "feat: define workspace protocol and entity contracts"
```

---

### Task 2: Windows host domain model and atomic persistence

**Files:**
- Create: `apps/host-windows/Workspace.Host.sln`
- Create: `apps/host-windows/src/Workspace.Host/Workspace.Host.csproj`
- Create: `apps/host-windows/src/Workspace.Host/Domain/WorkspaceEntity.cs`
- Create: `apps/host-windows/src/Workspace.Host/Domain/PresentationState.cs`
- Create: `apps/host-windows/src/Workspace.Host/Domain/HostBinding.cs`
- Create: `apps/host-windows/src/Workspace.Host/Domain/EntityKinds.cs`
- Create: `apps/host-windows/src/Workspace.Host/Persistence/WorkspaceDocument.cs`
- Create: `apps/host-windows/src/Workspace.Host/Persistence/AtomicWorkspaceStore.cs`
- Create: `apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/DomainTests.cs`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/PersistenceTests.cs`

**Interfaces:**
- Produces immutable `WorkspaceEntity` records matching the shared contract.
- Produces `AtomicWorkspaceStore.LoadAsync` and `SaveAsync`.
- Composition chooses `%LOCALAPPDATA%\WorkspaceEnvironment\workspace.json`; the store itself only receives a path.

- [ ] **Step 1: Write failing domain test**

```csharp
[Fact]
public void PresentationChangeDoesNotChangeSemanticIdentity()
{
    var entity = WorkspaceEntity.CreateApplication("app:microsoft-edge", "Microsoft Edge");
    var moved = entity with { Presentation = entity.Presentation with { Position = new(2, 1, -3) } };
    Assert.Equal(entity.Id, moved.Id);
    Assert.Equal(entity.HostBinding, moved.HostBinding);
}
```

- [ ] **Step 2: Write failing persistence round-trip test**

```csharp
[Fact]
public async Task SaveThenLoadRoundTripsWorkspaceDocument()
{
    var store = new AtomicWorkspaceStore(Path.Combine(_tempDir, "workspace.json"));
    var expected = WorkspaceDocumentFixtures.SingleApplication();
    await store.SaveAsync(expected, CancellationToken.None);
    Assert.Equal(expected, await store.LoadAsync(CancellationToken.None));
}
```

- [ ] **Step 3: Run test to verify RED**

Run on .NET 8: `dotnet test apps/host-windows/Workspace.Host.sln`
Expected: FAIL because host solution/domain/store do not exist.

- [ ] **Step 4: Implement records and atomic write**

`SaveAsync` writes UTF-8 JSON to a sibling temporary file, flushes it, then replaces/moves it to the durable path. Do not report success before the final filesystem operation succeeds.

- [ ] **Step 5: Run tests to verify GREEN**

Run: `dotnet test apps/host-windows/Workspace.Host.sln`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add apps/host-windows
git commit -m "feat: add durable workspace entity store"
```

---

### Task 3: Generic application inventory and launch boundary

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationDescriptor.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/IApplicationCatalog.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/WindowsApplicationCatalog.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/IProcessLauncher.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationLauncher.cs`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs`

**Interfaces:**
- `IApplicationCatalog.ListAsync(CancellationToken)`
- `IApplicationCatalog.FindByNameAsync(string, CancellationToken)`
- `ApplicationLauncher.LaunchAsync(ApplicationDescriptor, CancellationToken)` returns durable application ID plus transient PID.

- [ ] **Step 1: Write failing fake-launcher test**

```csharp
[Fact]
public async Task LaunchUsesDescriptorWithoutEdgeSpecificLogic()
{
    var app = new ApplicationDescriptor("app:microsoft-edge", "Microsoft Edge", @"C:\Program Files\Microsoft\Edge\Application\msedge.exe", null);
    var launcher = new FakeProcessLauncher(4242);
    var result = await new ApplicationLauncher(launcher).LaunchAsync(app, CancellationToken.None);
    Assert.Equal(app.Id, result.ApplicationId);
    Assert.Equal(4242, result.ProcessId);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test apps/host-windows/Workspace.Host.sln --filter ApplicationLifecycleTests`
Expected: FAIL.

- [ ] **Step 3: Implement Windows catalog and launcher**

Catalog rules: discover human-facing installed-app entries, normalize display names, derive stable IDs from durable launch identity/path, collapse duplicate launch targets, and contain no special branch for Edge.

- [ ] **Step 4: Verify GREEN and Windows smoke test**

Run targeted tests. On Windows run the host diagnostic `apps list` and confirm Microsoft Edge appears by name.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows/src/Workspace.Host/Applications apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs
git commit -m "feat: discover and launch installed applications"
```

---

### Task 4: Window discovery and lifecycle reconciliation

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Windows/IWindowCatalog.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/WindowSnapshot.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/Win32WindowCatalog.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/WindowReconciler.cs`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/WindowReconcilerTests.cs`

**Interfaces:**
- `WindowSnapshot` contains runtime HWND/PID/title/bounds/visibility only.
- `WindowReconciler` maps runtime observations onto durable `pc.window` entities.
- HWND is never persisted.

- [ ] **Step 1: Write failing HWND rotation test**

```csharp
[Fact]
public void RecreatedWindowKeepsSemanticIdentity()
{
    var first = new WindowSnapshot((nint)100, 10, "Workspace Test Window", new Rect(0,0,800,600), true, false);
    var second = first with { Hwnd = (nint)900, ProcessId = 22 };
    Assert.Equal(_sut.ResolveEntityId(first), _sut.ResolveEntityId(second));
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test apps/host-windows/Workspace.Host.sln --filter WindowReconcilerTests`
Expected: FAIL.

- [ ] **Step 3: Implement Win32 enumeration behind `IWindowCatalog`**

Use `EnumWindows`, `GetWindowThreadProcessId`, `GetWindowText`, `GetWindowRect`, `IsWindowVisible`, and `IsIconic`. Keep all P/Invoke code in `Win32WindowCatalog`.

- [ ] **Step 4: Verify GREEN**

Run targeted tests.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows/src/Workspace.Host/Windows apps/host-windows/tests/Workspace.Host.Tests/WindowReconcilerTests.cs
git commit -m "feat: reconcile Windows windows into workspace entities"
```

---

### Task 5: Versioned WebSocket protocol and client replica

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Protocol/ProtocolEnvelope.cs`
- Create: `apps/host-windows/src/Workspace.Host/Protocol/CommandDispatcher.cs`
- Create: `apps/host-windows/src/Workspace.Host/Protocol/WorkspaceProtocolServer.cs`
- Create: `apps/host-windows/src/Workspace.Host/Program.cs`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/ProtocolTests.cs`
- Create: `apps/spatial-client/package.json`
- Create: `apps/spatial-client/vite.config.ts`
- Create: `apps/spatial-client/tsconfig.json`
- Create: `apps/spatial-client/src/protocol/WorkspaceSocket.ts`
- Test: `apps/spatial-client/src/protocol/WorkspaceSocket.test.ts`
- Create: `apps/spatial-client/src/replica/WorldReplica.ts`
- Test: `apps/spatial-client/src/replica/WorldReplica.test.ts`

**Interfaces:**
- V0 endpoint is loopback-only: `ws://127.0.0.1:41771/workspace`.
- Commands: `application.list`, `application.launch`, `entity.setPresentation`, `window.focus`.
- Client `WorkspaceSocket.sendCommand(...)` returns correlated results.
- `WorldReplica.apply(...)` rejects unsupported protocol versions.

- [ ] **Step 1: Write failing protocol-version tests in C# and TypeScript**

```ts
it('rejects an unsupported protocol version', () => {
  const replica = new WorldReplica();
  expect(() => replica.apply({ protocol: 99, type: 'snapshot', entities: [] } as never)).toThrow(/protocol/i);
});
```

- [ ] **Step 2: Verify RED**

Run: `npm test --workspaces --if-present` and `dotnet test apps/host-windows/Workspace.Host.sln --filter ProtocolTests`.

- [ ] **Step 3: Implement loopback server, dispatcher, socket client, and replica**

Every command receives exactly one correlated result/error. Send a snapshot only after version agreement. Persist presentation before emitting `PRESENTATION_UPDATED`.

- [ ] **Step 4: Verify GREEN**

Run both test suites.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows/src/Workspace.Host/Protocol apps/host-windows/src/Workspace.Host/Program.cs apps/spatial-client
git commit -m "feat: connect spatial client to authoritative host protocol"
```

---

### Task 6: Sparse Three.js workspace and first-run welcome

**Files:**
- Create: `apps/spatial-client/index.html`
- Create: `apps/spatial-client/src/main.ts`
- Create: `apps/spatial-client/src/app/createWorkspaceApp.ts`
- Create: `apps/spatial-client/src/rendering/WorkspaceScene.ts`
- Create: `apps/spatial-client/src/rendering/RendererRegistry.ts`
- Test: `apps/spatial-client/src/rendering/RendererRegistry.test.ts`
- Create: `apps/spatial-client/src/onboarding/WelcomeSequence.ts`
- Create: `apps/spatial-client/src/styles.css`

**Interfaces:**
- `createWorkspaceApp(root)` is the client composition root.
- `WorkspaceScene.upsert(entity)`/`remove(entityId)` react to replica changes.
- Scene starts sparse with floor, neutral light, initial camera, and an actual first work area behind spawn so “turn around” is truthful.

- [ ] **Step 1: Write failing renderer registry test**

```ts
it('maps pc.window to application-surface', () => {
  expect(registry.resolve('pc.window').kind).toBe('application-surface');
});
```

- [ ] **Step 2: Verify RED**

Run: `npm test --workspace @workspace/spatial-client`

- [ ] **Step 3: Implement scene and welcome flow**

Welcome begins exactly with:

```text
Welcome to your workspace environment.
This is the place where we'll build the way you work.
```

Provide a visible local `Open Microsoft Edge` action that sends `application.launch`; this is the V0 command surface before an agent is attached.

- [ ] **Step 4: Verify GREEN and production build**

Run: `npm test --workspace @workspace/spatial-client && npm run build --workspace @workspace/spatial-client`

- [ ] **Step 5: Commit**

```bash
git add apps/spatial-client
git commit -m "feat: add sparse spatial workspace shell"
```

---

### Task 7: Live capture behind a transport-independent `SurfaceStream`

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Windows/IWindowCapture.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/SurfaceFrame.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/GraphicsCaptureWindowCapture.cs`
- Create: `apps/spatial-client/src/surfaces/SurfaceStream.ts`
- Create: `apps/spatial-client/src/surfaces/ApplicationSurface.ts`
- Test: capture lifecycle in `WindowReconcilerTests.cs`
- Test: `apps/spatial-client/src/surfaces/SurfaceInteraction.test.ts`

**Interfaces:**
- Host starts/stops capture by transient stream ID.
- Stream IDs are runtime-only.
- Renderer consumes `SurfaceStream` and does not know Windows Graphics Capture details.

- [ ] **Step 1: Write failing capture ownership test**

```csharp
[Fact]
public async Task ClosingWindowStopsCaptureWithoutDeletingApplicationIdentity()
{
    var state = await _harness.OpenThenCloseTestWindowAsync();
    Assert.Empty(state.ActiveCaptureStreams);
    Assert.Contains(state.Entities, e => e.Kind == EntityKinds.Application);
}
```

- [ ] **Step 2: Verify RED**

Run targeted host tests.

- [ ] **Step 3: Implement Windows Graphics Capture adapter and local desktop stream transport**

Capture must be generic by HWND, stop on window disappearance, surface failure as an unavailable state, and never persist capture identifiers.

- [ ] **Step 4: Verify GREEN with deterministic test window**

Start the test window, host, and client. Confirm visibly changing content renders live in Three.js.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows/src/Workspace.Host/Windows apps/spatial-client/src/surfaces
git commit -m "feat: stream live Windows surfaces into Three.js"
```

---

### Task 8: Spatial input routing to the real Windows window

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Windows/IInputRouter.cs`
- Create: `apps/host-windows/src/Workspace.Host/Windows/Win32InputRouter.cs`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/InputMappingTests.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Protocol/CommandDispatcher.cs`
- Modify/Test: `apps/spatial-client/src/surfaces/ApplicationSurface.ts`, `SurfaceInteraction.test.ts`

**Interfaces:**
- Client command `window.input` carries normalized `[0,1]` coordinates plus pointer/wheel/key/text intent.
- Host maps against current real window bounds immediately before dispatch.

- [ ] **Step 1: Write failing normalized-coordinate tests**

```csharp
[Theory]
[InlineData(0.0, 0.0, 100, 200)]
[InlineData(1.0, 1.0, 900, 800)]
[InlineData(0.5, 0.5, 500, 500)]
public void MapsNormalizedCoordinates(double x, double y, int ex, int ey)
{
    var result = InputCoordinateMapper.Map(x, y, new Rect(100, 200, 800, 600));
    Assert.Equal((ex, ey), (result.X, result.Y));
}
```

- [ ] **Step 2: Verify RED**

Run targeted host/client tests.

- [ ] **Step 3: Implement pointer, wheel, keyboard, and text routing**

If Windows integrity boundaries block injection, return `INPUT_TARGET_NOT_PERMITTED`; do not elevate the host.

- [ ] **Step 4: Verify GREEN and interact with deterministic test window**

Confirm click, typing, and scrolling work through the Three.js surface.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows apps/spatial-client/src/surfaces
git commit -m "feat: route spatial input to Windows applications"
```

---

### Task 9: Presentation persistence and restart recovery

**Files:**
- Modify: `apps/host-windows/src/Workspace.Host/Persistence/WorkspaceDocument.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Protocol/CommandDispatcher.cs`
- Modify/Test: `apps/spatial-client/src/surfaces/ApplicationSurface.ts`, `SurfaceInteraction.test.ts`
- Test: `apps/host-windows/tests/Workspace.Host.Tests/PersistenceTests.cs`

**Interfaces:**
- `entity.setPresentation` sends complete `PresentationState`.
- Host persists first, then emits authoritative `PRESENTATION_UPDATED`.
- Client move/resize affects presentation only.

- [ ] **Step 1: Write failing restart/rebind test**

```csharp
[Fact]
public async Task PresentationSurvivesNewHwnd()
{
    const string id = "window:workspace-test:main";
    await _store.SavePresentationAsync(id, TestPresentation.At(3, 1.5f, -2), default);
    var reopened = await _harness.RestartWithHwndAsync((nint)999);
    Assert.Equal(id, reopened.Entity.Id);
    Assert.Equal(3, reopened.Entity.Presentation.Position.X);
    Assert.Equal((nint)999, reopened.Runtime.Hwnd);
}
```

- [ ] **Step 2: Verify RED**

Run targeted host/client tests.

- [ ] **Step 3: Implement durable presentation transaction**

Local drag preview is allowed, but final authority comes from host event. Revert preview when persistence fails.

- [ ] **Step 4: Verify GREEN**

Run all host/client tests and restart smoke test.

- [ ] **Step 5: Commit**

```bash
git add apps/host-windows apps/spatial-client/src/surfaces
git commit -m "feat: persist spatial application placement"
```

---

### Task 10: Deterministic E2E window, Edge acceptance, and documentation

**Files:**
- Create: `apps/host-windows/src/Workspace.TestWindow/Workspace.TestWindow.csproj`
- Create: `apps/host-windows/src/Workspace.TestWindow/Program.cs`
- Create: `docs/v0-acceptance.md`
- Modify: `README.md`

**Interfaces:**
- Automated E2E target is a normal first-party window with text input, a counter button, scrollable region, and changing visual indicator.
- Human acceptance target is Microsoft Edge through the exact same generic pipeline.

- [ ] **Step 1: Build deterministic test window**

No host-specific backchannel is permitted. It must behave like an ordinary top-level Windows app.

- [ ] **Step 2: Run complete verification**

```bash
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln
```

Expected: all tests and builds pass.

- [ ] **Step 3: Perform Edge acceptance on Windows 10/11**

Verify: inventory → launch → top-level-window discovery → live surface → click/type/scroll → spatial move/resize → restart → semantic identity and placement restore despite PID/HWND changes.

- [ ] **Step 4: Update README and acceptance document**

Document exact Windows prerequisites, start commands, loopback port, local data path, and V0 restrictions. Do not claim Quest, remote streaming, MCP agent control, or elevated-app interaction are implemented.

- [ ] **Step 5: Commit**

```bash
git add README.md docs/v0-acceptance.md apps/host-windows/src/Workspace.TestWindow
git commit -m "docs: define and verify V0 real application proof"
```

---

## Plan self-review

### Spec coverage

- Authority/client split: Tasks 2, 5, 6.
- Durable identity/application-window separation: Tasks 1, 2, 4.
- App inventory/launch: Task 3.
- Window lifecycle: Task 4.
- Versioned protocol: Task 5.
- Sparse welcoming place: Task 6.
- Live generic surface: Task 7.
- Real input: Task 8.
- Durable presentation/restart: Task 9.
- Edge human proof without an Edge-specific integration: Task 10.
- Unelevated security posture: Tasks 3 and 8.
- Quest/WebXR path: preserved by client/protocol/surface boundaries; not falsely claimed as V0 implementation.
- Provider-independent foundation: no model/API dependency is introduced.

### Type consistency

- `WorkspaceEntity.Id` is durable and serialized.
- HWND/PID/stream identifiers stay runtime-only.
- `entity.setPresentation` carries complete presentation state.
- Pointer coordinates are normalized before host mapping.
- Protocol version is exactly `1`.

### Placeholder scan

Later-scope features are excluded rather than represented as incomplete V0 steps. No V0 step contains deferred or placeholder implementation instructions.
