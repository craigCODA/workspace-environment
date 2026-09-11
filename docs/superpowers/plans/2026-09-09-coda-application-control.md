# Coda Application Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let Coda discover, launch, reuse, place, focus, close, and restart real Windows applications through typed, approved, result-driven workspace actions.

**Architecture:** Extend the existing Workspace Host application/window pipeline and semantic entity store. Add durable display surfaces, route allowlisted application actions from native Coda through the correlated WebView bridge and existing host WebSocket, and keep shell execution unavailable to the renderer.

**Tech Stack:** .NET 8/C# 12, Win32 and Windows Shell APIs, WinUI 3/WebView2, TypeScript 5.9, Three.js, xUnit, Node test runner, Vite.

**Spec:** `docs/superpowers/specs/2026-09-09-coda-application-control-design.md`

## Global Constraints

- Target Windows 11 while retaining `net8.0-windows10.0.19041.0`.
- Extend the existing host, spatial client, native desktop, Codex App Server, and capability broker; do not add a parallel launcher service.
- Keep Workspace Host authoritative for applications, windows, launch profiles, semantic entities, and display bindings.
- Keep the renderer unable to submit raw executable paths, shell strings, credentials, elevation requests, or unrestricted native commands.
- Resolve human-facing application queries through read-only preflight before launch approval.
- Require fresh confirmation for close, restart, and replacement of an occupied display surface.
- Never speak a completed-action claim until the observed host result confirms it.
- Keep global Codex configuration and the installed Electron fallback untouched.
- Preserve the user's untracked `.superpowers/` directory.
- Use red-green-refactor for each production behavior.

## File structure

- Host application metadata, search, profiles, launch, and lifecycle remain under `apps/host-windows/src/Workspace.Host/Applications/`.
- Host semantic surfaces and migration remain under `Domain/` and `Persistence/`; protocol parsing remains in `Protocol/CommandDispatcher.cs`.
- Three.js display rendering remains in `apps/spatial-client/src/rendering/`; native-to-host application commands get a separate `WorkspaceCommandController` under `navigation/`.
- Coda directive parsing, policy, narration, and orchestration remain in `Workspace.Desktop.Core/Runtime/`; WinUI/WebView composition remains in `Workspace.Desktop/`.

---

### Task 1: Typed application identity and deterministic resolution

**Files:**
- Modify: `apps/host-windows/src/Workspace.Host/Applications/ApplicationDescriptor.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Applications/IApplicationCatalog.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationResolver.cs`
- Modify: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs`

**Interfaces:**
- Consumes: `IApplicationCatalog.ListAsync(CancellationToken)`.
- Produces: `ApplicationLaunchKind`, expanded `ApplicationDescriptor`, `ApplicationResolution`, and `ApplicationResolver.Resolve`.

- [ ] **Step 1: Write failing resolver tests**

```csharp
[Fact]
public void Resolve_prefers_an_exact_alias_over_a_prefix()
{
    var apps = new[]
    {
        new ApplicationDescriptor("app:edge", "Microsoft Edge", ApplicationLaunchKind.Executable,
            @"C:\Edge\msedge.exe", ["Edge"]),
        new ApplicationDescriptor("app:edge-beta", "Microsoft Edge Beta", ApplicationLaunchKind.Executable,
            @"C:\EdgeBeta\msedge.exe", ["Edge Beta"]),
    };
    var result = ApplicationResolver.Resolve("Edge", apps);
    Assert.Equal(ApplicationResolutionStatus.Resolved, result.Status);
    Assert.Equal("app:edge", result.Application!.Id);
}

[Fact]
public void Resolve_returns_candidates_for_an_ambiguous_prefix()
{
    var apps = new[]
    {
        new ApplicationDescriptor("app:vs", "Visual Studio", ApplicationLaunchKind.Executable, @"C:\VS\devenv.exe", []),
        new ApplicationDescriptor("app:vscode", "Visual Studio Code", ApplicationLaunchKind.Executable, @"C:\Code\Code.exe", []),
    };
    var result = ApplicationResolver.Resolve("Visual", apps);
    Assert.Equal(ApplicationResolutionStatus.Ambiguous, result.Status);
    Assert.Equal(["app:vs", "app:vscode"], result.Candidates.Select(x => x.Id));
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release --filter "FullyQualifiedName~ApplicationLifecycleTests"
```

Expected: compile failure because the new launch-kind and resolver types are absent.

- [ ] **Step 3: Implement the model and resolver**

```csharp
public enum ApplicationLaunchKind { Executable, Shortcut, Packaged }
public sealed record ApplicationDescriptor(
    string Id, string DisplayName, ApplicationLaunchKind LaunchKind,
    string Locator, IReadOnlyList<string> Aliases);
public enum ApplicationResolutionStatus { NotFound, Resolved, Ambiguous }
public sealed record ApplicationResolution(
    ApplicationResolutionStatus Status,
    ApplicationDescriptor? Application,
    IReadOnlyList<ApplicationDescriptor> Candidates);
```

Normalize NFKC text, trim and collapse whitespace, compare ordinal-ignore-case, then resolve exact ID, exact display/alias, unique prefix, unique token match, ambiguity, or not-found. Sort ties by display name and stable ID.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
git add apps/host-windows/src/Workspace.Host/Applications apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs
git commit -m "feat: resolve Windows applications deterministically"
```

### Task 2: Windows inventory and structured launch strategies

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationInventory.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Applications/WindowsApplicationCatalog.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Applications/IProcessLauncher.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Applications/ApplicationLauncher.cs`
- Create: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationInventoryTests.cs`
- Modify: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs`

**Interfaces:**
- Consumes: Task 1 descriptors.
- Produces: `IApplicationInventorySource`, merged App Paths/Start Menu/AppsFolder inventory, `ApplicationStartRequest`, and nullable PID observations.

- [ ] **Step 1: Write failing inventory and launch tests**

```csharp
[Fact]
public void Inventory_merges_duplicate_locators_and_keeps_aliases()
{
    var merged = ApplicationInventory.Merge([
        new("app:edge", "Microsoft Edge", ApplicationLaunchKind.Executable, @"C:\Edge\msedge.exe", ["Edge"]),
        new("app:copy", "Edge", ApplicationLaunchKind.Executable, @"C:\EDGE\msedge.exe", ["Microsoft Edge"]),
    ]);
    var application = Assert.Single(merged);
    Assert.Contains("Edge", application.Aliases);
}

[Fact]
public async Task Launcher_keeps_arguments_and_working_directory_structured()
{
    var process = new RecordingProcessLauncher(8100);
    await new ApplicationLauncher(process).LaunchAsync(
        new ApplicationDescriptor("app:terminal", "Terminal", ApplicationLaunchKind.Executable, @"C:\wt.exe", []),
        ["new-tab", "codex"], @"D:\PythOS-Workspace", CancellationToken.None);
    Assert.Equal(["new-tab", "codex"], process.Request!.Arguments);
    Assert.Equal(@"D:\PythOS-Workspace", process.Request.WorkingDirectory);
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release --filter "FullyQualifiedName~ApplicationInventoryTests|FullyQualifiedName~ApplicationLifecycleTests"
```

Expected: compile failure for inventory and structured start contracts.

- [ ] **Step 3: Implement inventory sources and launcher**

```csharp
public interface IApplicationInventorySource
{
    Task<IReadOnlyList<ApplicationDescriptor>> ListAsync(CancellationToken cancellationToken);
}
public sealed record ApplicationStartRequest(
    ApplicationLaunchKind LaunchKind,
    string Locator,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory);
public interface IProcessLauncher
{
    Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken);
}
```

Keep App Paths as one source. Enumerate `.lnk` files under current-user/common Start Menu directories as `Shortcut`. Enumerate `shell:AppsFolder` through the Windows Shell COM boundary as `Packaged`. Inaccessible sources return no items without hiding other sources. `Executable` uses `ProcessStartInfo.ArgumentList`; shell kinds use typed locators with `UseShellExecute = true`. Never fabricate a PID when Windows returns none.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
git add apps/host-windows/src/Workspace.Host/Applications apps/host-windows/tests/Workspace.Host.Tests/ApplicationInventoryTests.cs apps/host-windows/tests/Workspace.Host.Tests/ApplicationLifecycleTests.cs
git commit -m "feat: inventory and launch Windows app kinds"
```

### Task 3: Durable launch profiles

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationLaunchProfile.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/IApplicationProfileStore.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/AtomicApplicationProfileStore.cs`
- Create: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationProfileStoreTests.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Program.cs`

**Interfaces:**
- Consumes: Tasks 1-2 application IDs and launch requests.
- Produces: versioned list/find/save/delete profile persistence.

- [ ] **Step 1: Write failing real-store tests**

```csharp
[Fact]
public async Task Save_round_trips_tokenized_arguments()
{
    IApplicationProfileStore store = new AtomicApplicationProfileStore(
        Path.Combine(_temporaryDirectory, "application-profiles.json"));
    var profile = new ApplicationLaunchProfile(
        "profile:pythos-codex", "PythOS Codex", "app:terminal",
        ["new-tab", "codex"], @"D:\PythOS-Workspace",
        ApplicationLaunchPolicy.ReuseOrLaunch, null, null);
    await store.SaveAsync(profile, CancellationToken.None);
    Assert.Equal(profile, await store.FindAsync(profile.Id, CancellationToken.None));
}

[Fact]
public async Task Save_rejects_a_relative_working_directory()
{
    IApplicationProfileStore store = new AtomicApplicationProfileStore(
        Path.Combine(_temporaryDirectory, "application-profiles.json"));
    var profile = new ApplicationLaunchProfile(
        "profile:bad", "Bad", "app:terminal", [], @"..\outside",
        ApplicationLaunchPolicy.ReuseOrLaunch, null, null);
    await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(profile, CancellationToken.None));
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release --filter "FullyQualifiedName~ApplicationProfileStoreTests"
```

Expected: compile failure because profile contracts are absent.

- [ ] **Step 3: Implement profile contracts and atomic store**

```csharp
public enum ApplicationLaunchPolicy { ReuseOrLaunch, NewInstance }
public sealed record ApplicationLaunchProfile(
    string Id, string DisplayName, string ApplicationId,
    IReadOnlyList<string> Arguments, string? WorkingDirectory,
    ApplicationLaunchPolicy LaunchPolicy,
    string? PreferredSurfaceId, PresentationState? PreferredPresentation);
public interface IApplicationProfileStore
{
    Task<IReadOnlyList<ApplicationLaunchProfile>> ListAsync(CancellationToken cancellationToken);
    Task<ApplicationLaunchProfile?> FindAsync(string profileId, CancellationToken cancellationToken);
    Task SaveAsync(ApplicationLaunchProfile profile, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string profileId, CancellationToken cancellationToken);
}
```

Persist schema version 1 with create-new temporary files, flush-to-disk, and replace/move. Validate IDs, no NUL characters, at most 64 arguments, 4096 characters per argument, and absolute working directories. Store at `%LOCALAPPDATA%\WorkspaceEnvironment\application-profiles.json`. Do not add environment variables or secrets.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
git add apps/host-windows/src/Workspace.Host/Applications apps/host-windows/src/Workspace.Host/Program.cs apps/host-windows/tests/Workspace.Host.Tests/ApplicationProfileStoreTests.cs
git commit -m "feat: persist structured application profiles"
```

### Task 4: Durable display surfaces and schema migration

**Files:**
- Modify: `apps/host-windows/src/Workspace.Host/Domain/EntityKinds.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Domain/WorkspaceEntity.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Persistence/WorkspaceDocument.cs`
- Create: `apps/host-windows/src/Workspace.Host/Persistence/WorkspaceMigrator.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Program.cs`
- Modify: `apps/host-windows/tests/Workspace.Host.Tests/DomainTests.cs`
- Modify: `apps/host-windows/tests/Workspace.Host.Tests/PersistenceTests.cs`

**Interfaces:**
- Consumes: existing entities, relationships, presentation, and store.
- Produces: `EntityKinds.Surface`, `CreateDisplaySurface`, schema 2 migration, and `TryBindWindow`.

- [ ] **Step 1: Write failing migration and binding tests**

```csharp
[Fact]
public void Migration_creates_one_surface_and_preserves_window_placement()
{
    var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge") with
    { Presentation = PresentationState.Default with { Position = new Vec3(4.2, 1.4, -3) } };
    var migrated = new WorkspaceDocument(1, [window]).MigrateToCurrent();
    var surface = Assert.Single(migrated.Entities.Where(x => x.Kind == EntityKinds.Surface));
    Assert.Equal(new Vec3(4.2, 1.4, -3), surface.Presentation.Position);
    Assert.Contains(surface.Relationships, x => x.Type == "displays" && x.TargetId == window.Id);
    Assert.Single(migrated.MigrateToCurrent().Entities.Where(x => x.Kind == EntityKinds.Surface));
}

[Fact]
public void Bind_window_replaces_only_the_display_relationship()
{
    var first = WorkspaceEntity.CreateWindow("pc.window:first", "First", "pc.application:first");
    var second = WorkspaceEntity.CreateWindow("pc.window:second", "Second", "pc.application:second");
    var surface = WorkspaceEntity.CreateDisplaySurface("spatial.surface:desk", "Desk", PresentationState.Default, first.Id);
    var document = new WorkspaceDocument(2, [first, second, surface]);
    Assert.True(document.TryBindWindow(surface.Id, second.Id, out var updated));
    Assert.Contains(updated!.Relationships, x => x.Type == "displays" && x.TargetId == second.Id);
}
```

- [ ] **Step 2: Verify RED**

```powershell
dotnet test apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release --filter "FullyQualifiedName~DomainTests|FullyQualifiedName~PersistenceTests"
```

Expected: compile failure for surface/migration APIs.

- [ ] **Step 3: Implement migration and binding**

Add `EntityKinds.Surface = "spatial.surface"`. Surface capabilities are `bindWindow`, `setPresentation`, and `select`; its optional `displays` relationship targets one `pc.window`. Set `WorkspaceDocument.CurrentSchemaVersion = 2`. Migrate each directly presented legacy window into deterministic ID `spatial.surface:{window.Id}` only when no surface displays it. Keep legacy presentation for rollback. `TryBindWindow` validates both kinds, replaces only `displays`, and leaves state unchanged on failure. Run `WorkspaceMigrator.EnsureCurrentAsync` before the protocol server starts.

- [ ] **Step 4: Verify GREEN and commit**

```powershell
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
git add apps/host-windows/src/Workspace.Host/Domain apps/host-windows/src/Workspace.Host/Persistence apps/host-windows/src/Workspace.Host/Program.cs apps/host-windows/tests/Workspace.Host.Tests/DomainTests.cs apps/host-windows/tests/Workspace.Host.Tests/PersistenceTests.cs
git commit -m "feat: persist durable display surfaces"
```

### Task 5: Host application-control lifecycle and protocol

**Files:**
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationControlService.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/IWindowLifecycleService.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/Win32WindowLifecycleService.cs`
- Create: `apps/host-windows/src/Workspace.Host/Applications/ApplicationControlAuditStore.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Protocol/CommandDispatcher.cs`
- Modify: `apps/host-windows/src/Workspace.Host/Program.cs`
- Create: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationControlTests.cs`
- Create: `apps/host-windows/tests/Workspace.Host.Tests/ApplicationControlAuditStoreTests.cs`
- Modify: `apps/host-windows/tests/Workspace.Host.Tests/ProtocolTests.cs`

**Interfaces:**
- Consumes: Tasks 1-4 services and stores.
- Produces: `application.search`, `application.profile.list/save/delete`, `application.open/close/restart`, and `surface.bindWindow`.

- [ ] **Step 1: Write failing lifecycle tests**

```csharp
[Fact]
public async Task Open_reuses_a_visible_window_and_binds_the_selected_surface()
{
    var fixture = ApplicationControlFixture.WithVisibleWindow(
        "app:notepad", "pc.window:notepad", "spatial.surface:right");
    var result = await fixture.Service.OpenAsync(
        new ApplicationOpenRequest("op-1", "app:notepad", null,
            ApplicationLaunchPolicy.ReuseOrLaunch, "spatial.surface:right", null),
        CancellationToken.None);
    Assert.Equal(ApplicationOpenDisposition.Reused, result.Disposition);
    Assert.Equal("spatial.surface:right", result.SurfaceEntityId);
    Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
}

[Fact]
public async Task Restart_stops_when_close_remains_pending()
{
    var fixture = ApplicationControlFixture.WithCloseResult(WindowCloseState.ClosePending);
    var result = await fixture.Service.RestartAsync(
        new ApplicationRestartRequest("op-2", "pc.window:notepad"), CancellationToken.None);
    Assert.Equal(ApplicationLifecycleState.ClosePending, result.State);
    Assert.Equal(0, fixture.ProcessLauncher.LaunchCount);
}

[Fact]
public async Task New_instance_attempts_launch_even_when_a_window_is_visible()
{
    var fixture = ApplicationControlFixture.WithVisibleWindow(
        "app:notepad", "pc.window:notepad", "spatial.surface:right");
    await fixture.Service.OpenAsync(
        new ApplicationOpenRequest("op-3", "app:notepad", null,
            ApplicationLaunchPolicy.NewInstance, null, null),
        CancellationToken.None);
    Assert.Equal(1, fixture.ProcessLauncher.LaunchCount);
}
```

Add protocol tests using literal JSON and rejecting unexpected fields.

Add a real temporary-file audit test that executes an open result and asserts one bounded record containing operation ID, semantic IDs, approval source, lifecycle state, and redacted error category while excluding captured content and raw microphone text.

- [ ] **Step 2: Verify RED**

```powershell
dotnet test apps/host-windows/tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release --filter "FullyQualifiedName~ApplicationControlTests|FullyQualifiedName~ProtocolTests"
```

Expected: compile failure for lifecycle request/result types.

- [ ] **Step 3: Implement open/reuse/wait/bind/focus**

```csharp
public enum ApplicationOpenDisposition { Reused, Launched, LaunchedWithoutWindow }
public enum ApplicationSurfaceState { Available, Unavailable, NotResolved }
public enum ApplicationLifecycleState { Open, Closed, ClosePending, NotRunning, Failed }
public sealed record ApplicationOpenResult(
    string OperationId, string ApplicationEntityId,
    string? WindowEntityId, string? SurfaceEntityId, int? ProcessId,
    ApplicationOpenDisposition Disposition,
    ApplicationSurfaceState SurfaceState, bool Focused);
```

Reuse a unique visible matching window before launch. `NewInstance` always attempts a launch and truthfully reports reuse if Windows coalesces the request. Otherwise snapshot HWNDs, launch, and poll every 100 ms for five seconds. Match exact application ID, launched PID/descendant PID, then unique newly appeared compatible window. Return `LaunchedWithoutWindow` instead of guessing. Persist entities, create/bind a surface, and focus only after binding. Reject an occupied target with `surface_occupied` unless the separately approved request contains `replaceOccupied: true`; replacement unbinds but never closes the displaced window.

- [ ] **Step 4: Implement graceful close/restart and strict protocol**

```csharp
public enum WindowCloseState { Closed, ClosePending, NotRunning }
public interface IWindowLifecycleService
{
    Task<WindowCloseState> RequestCloseAsync(
        string windowEntityId, TimeSpan timeout, CancellationToken cancellationToken);
}
```

The Win32 implementation resolves current HWND, sends `WM_CLOSE`, and observes disappearance for five seconds. It never calls `Process.Kill`. Restart retains profile and surface, stops on `ClosePending`, relaunches, and rebinds. Parse request DTOs with `JsonUnmappedMemberHandling.Disallow`. Preserve existing operations. `ApplicationControlAuditStore` atomically retains the latest 500 redacted records at `%LOCALAPPDATA%\WorkspaceEnvironment\application-control-audit.json`.

- [ ] **Step 5: Verify GREEN and commit**

```powershell
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
git add apps/host-windows/src/Workspace.Host/Applications apps/host-windows/src/Workspace.Host/Protocol/CommandDispatcher.cs apps/host-windows/src/Workspace.Host/Program.cs apps/host-windows/tests/Workspace.Host.Tests/ApplicationControlTests.cs apps/host-windows/tests/Workspace.Host.Tests/ApplicationControlAuditStoreTests.cs apps/host-windows/tests/Workspace.Host.Tests/ProtocolTests.cs
git commit -m "feat: control application lifecycle through host"
```

### Task 6: Spatial display rendering and native-to-host command bridge

**Files:**
- Modify: `packages/world-schema/src/index.ts`
- Modify: `packages/world-schema/src/index.test.ts`
- Create: `apps/spatial-client/src/navigation/WorkspaceCommandController.ts`
- Create: `apps/spatial-client/src/navigation/WorkspaceCommandController.test.ts`
- Modify: `apps/spatial-client/src/rendering/RendererRegistry.ts`
- Modify: `apps/spatial-client/src/rendering/WorkspaceScene.ts`
- Modify: `apps/spatial-client/src/surfaces/ApplicationSurface.ts`
- Modify: `apps/spatial-client/src/replica/SceneReplicaSynchronizer.ts`
- Modify: `apps/spatial-client/src/replica/SceneReplicaSynchronizer.test.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`

**Interfaces:**
- Consumes: host operations and surface entities.
- Produces: `displayedWindowId`, `WorkspaceCommandController.handle`, selected-surface context, camera-relative default placement, and correlated results.

- [ ] **Step 1: Write failing command and rendering tests**

```typescript
test('open forwards resolved identity and selected surface', async () => {
  const calls: unknown[][] = [];
  const controller = new WorkspaceCommandController({
    sendCommand: async (...args: unknown[]) => { calls.push(args); return { disposition: 'launched' }; },
  }, () => 'spatial.surface:right');
  const result = await controller.handle({
    id: 'native-1', command: 'application.open',
    args: { applicationId: 'app:notepad', targetSurfaceId: '$selected' },
  });
  assert.equal(result.ok, true);
  assert.deepEqual(calls, [[
    'application.open', undefined,
    { applicationId: 'app:notepad', targetSurfaceId: 'spatial.surface:right' },
  ]]);
});

test('raw paths and unknown commands are rejected', async () => {
  const controller = new WorkspaceCommandController({ sendCommand: async () => ({}) }, () => null);
  assert.equal((await controller.handle({
    id: 'x', command: 'application.open', args: { executablePath: 'C:\\bad.exe' },
  })).ok, false);
  assert.equal((await controller.handle({ id: 'y', command: 'shell.run', args: {} })).ok, false);
});
```

Add a scene test asserting one bound surface renders once, capture/input use window ID, and presentation uses surface ID.

Add a controller test with no selected surface and literal camera pose `{ position: { x: 0, y: 1.65, z: 4 }, yaw: 0, pitch: 0 }`; assert `application.open` receives a new-surface presentation centered three metres in front of the camera and offset from an already occupied coordinate.

- [ ] **Step 2: Verify RED**

```powershell
npm test --workspace @workspace/world-schema
npm test --workspace @workspace/spatial-client
```

Expected: missing controller and surface rendering failures.

- [ ] **Step 3: Implement surface helpers and command allowlist**

```typescript
export function displayedWindowId(entity: WorkspaceEntity): string | null {
  if (entity.kind === 'pc.window') return entity.id;
  if (entity.kind !== 'spatial.surface') return null;
  return entity.relationships.find((edge) => edge.type === 'displays')?.targetId ?? null;
}
const ALLOWED_WORKSPACE_COMMANDS = new Set([
  'application.search', 'application.profile.list', 'application.profile.save',
  'application.profile.delete', 'application.open', 'application.close',
  'application.restart', 'window.focus', 'surface.bindWindow',
]);
```

Render a bound `spatial.surface` as the application surface. Render a legacy `pc.window` only when no surface displays it. A blank surface gets a neutral selectable material. Stream/input factories receive window ID; presentation sink receives surface ID. Validate operation-specific keys and replace only `'$selected'` with a selected surface ID. When neither an explicit nor selected surface exists, derive a 3.2 by 1.8 metre presentation three metres in front of the current camera and apply deterministic 0.25 metre cascade offsets until it no longer exactly overlaps an existing surface.

- [ ] **Step 4: Wire, verify GREEN, and commit**

Wire native `workspace.command` to the controller and return `workspace.command.result` with matching ID.

```powershell
npm test --workspace @workspace/world-schema
npm test --workspace @workspace/spatial-client
npm run typecheck --workspace @workspace/world-schema
npm run typecheck --workspace @workspace/spatial-client
npm run build --workspace @workspace/spatial-client
git add packages/world-schema apps/spatial-client/src
git commit -m "feat: bind Windows apps to spatial displays"
```

### Task 7: Native directive validation, permission policy, and truthful orchestration

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Runtime/WorkspaceDirectiveParser.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Runtime/WorkspaceActionPolicy.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Runtime/WorkspaceActionNarrator.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Runtime/WorkspaceActionOrchestrator.cs`
- Modify: `apps/desktop-native/src/Workspace.Desktop.Core/Bridge/WebViewMessage.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/WorkspaceDirectiveParserTests.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/WorkspaceActionPolicyTests.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/WorkspaceActionOrchestratorTests.cs`
- Modify: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/WebViewMessageTests.cs`

**Interfaces:**
- Consumes: `CapabilityBroker` and correlated WebView results.
- Produces: strict workspace directives, semantic approval policy, deterministic narration, and resolve/approve/execute flow.

- [ ] **Step 1: Write failing parser and policy tests**

```csharp
[Fact]
public void Parses_action_without_speaking_private_markup()
{
    var result = WorkspaceDirectiveParser.Parse(
        "Opening it. [[workspace:{\"command\":\"application.open\",\"args\":{\"query\":\"Notepad\"}}]]");
    Assert.Equal("Opening it.", result.SpokenText);
    Assert.Equal("application.open", Assert.Single(result.Directives).Command);
}

[Theory]
[InlineData("application.search", WorkspaceConfirmation.None)]
[InlineData("application.open", WorkspaceConfirmation.Rememberable)]
[InlineData("application.close", WorkspaceConfirmation.Fresh)]
[InlineData("application.restart", WorkspaceConfirmation.Fresh)]
public void Classifies_confirmation(string command, WorkspaceConfirmation expected) =>
    Assert.Equal(expected, WorkspaceActionPolicy.Classify(command).Confirmation);
```

Also reject `shell.run`, `executablePath`, extra root fields, malformed JSON, and directive payloads over 64 KiB.

- [ ] **Step 2: Write failing truthful-sequencing test**

```csharp
[Fact]
public async Task Mutation_speaks_no_success_before_host_completion()
{
    var gateway = new ControllableWorkspaceGateway();
    var output = new RecordingWorkspaceActionOutput();
    var orchestrator = new WorkspaceActionOrchestrator(gateway, output);
    var pending = orchestrator.BeginAsync(
        new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new { query = "Notepad" })),
        CancellationToken.None);
    await gateway.WaitUntilRequestedAsync();
    Assert.DoesNotContain(output.Messages, x => x.Contains("open", StringComparison.OrdinalIgnoreCase));
    gateway.Complete(new { disposition = "launched", applicationName = "Notepad", focused = true });
    await pending;
    Assert.Contains("Notepad is open and focused.", output.Messages);
}
```

- [ ] **Step 3: Verify RED**

```powershell
dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj --configuration Release --filter "FullyQualifiedName~WorkspaceDirectiveParserTests|FullyQualifiedName~WorkspaceActionPolicyTests|FullyQualifiedName~WorkspaceActionOrchestratorTests|FullyQualifiedName~WebViewMessageTests"
```

Expected: compile failure for the new runtime contracts.

- [ ] **Step 4: Implement parser, policy, narrator, and orchestrator**

```csharp
public sealed record WorkspaceDirective(string Command, JsonElement Arguments);
public sealed record WorkspaceDirectiveResult(string SpokenText, IReadOnlyList<WorkspaceDirective> Directives);
public enum WorkspaceConfirmation { None, Rememberable, Fresh }
public interface IWorkspaceCommandGateway
{
    Task<JsonElement> SendAsync(string command, object? arguments, CancellationToken cancellationToken);
}
public interface IWorkspaceActionOutput
{
    Task RequestApprovalAsync(WorkspacePendingApproval approval, CancellationToken cancellationToken);
    Task ReportActivityAsync(string message, CancellationToken cancellationToken);
    Task SpeakAsync(string message, CancellationToken cancellationToken);
}
```

Parse only the nine spec operations and operation-specific fields. Resolve queries with `application.search`, reject ambiguity, substitute stable ID, then classify. Search/profile-list are automatic; launch/focus/bind/profile-edit are exact-scope rememberable; close/restart/occupied replacement are fresh. Narrate only host results. Add separate `workspace.command.result` correlation to `RendererMessageValidator`; reject unsolicited, duplicate, wrong-ID, and oversized results.

- [ ] **Step 5: Verify GREEN and commit**

```powershell
npm run native:test
git add apps/desktop-native/src/Workspace.Desktop.Core apps/desktop-native/tests/Workspace.Desktop.Core.Tests
git commit -m "feat: validate and authorize Coda app actions"
```

### Task 8: Connect Coda voice and chat to application control

**Files:**
- Modify: `apps/desktop-native/src/Workspace.Desktop/Bridge/WebViewBridge.cs`
- Modify: `apps/desktop-native/src/Workspace.Desktop/Runtime/DesktopCoordinator.cs`
- Modify: `apps/desktop-native/src/Workspace.Desktop.Core/Runtime/CodaLocalCommandParser.cs`
- Modify: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/CodaLocalCommandParserTests.cs`
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.ts`
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.test.ts`

**Interfaces:**
- Consumes: Tasks 6-7 controller and orchestrator.
- Produces: one voice/chat path, correlated gateway, pending action approval, profile actions, and result activity.

- [ ] **Step 1: Write failing integration-facing tests**

Keep `open Notepad here`, `restart this app`, and `save this as PythOS Codex` as `AgentRequest`; approval text is interpreted only while its pending action exists. Add a Coda presence test proving chat posts the same instruction envelope as final voice transcription:

```typescript
assert.deepEqual(posted, [
  { type: 'agent.instruction', payload: { text: 'open Notepad here' } },
]);
```

- [ ] **Step 2: Verify RED where integration is missing**

```powershell
npm run native:test
npm test --workspace @workspace/spatial-client
```

Expected: pending workspace-action cases fail; existing chat submission remains characterized.

- [ ] **Step 3: Implement native gateway and coordinator flow**

```csharp
public void PostWorkspaceCommand(string requestId, string command, object? arguments)
{
    _validator.ExpectWorkspaceResult(requestId);
    Post("workspace.command", new { id = requestId, command, args = arguments });
}
```

Add a workspace request sequence and completion dictionary parallel to scene requests. Keep Codex command approvals, navigation approvals, and workspace approvals as distinct pending records. Resolve before approval; show exact app/profile/surface and every profile argument; reject remember for fresh actions. Parse both scene and workspace directives. For `this application`, resolve the selected `spatial.surface` through its `displays` relationship to the exact window entity. After successful window focus, apply the existing navigation preference to camera focus. Withhold agent completion claims for mutations and speak deterministic host results. Add bounded activity entries without captured content or secrets.

Update the agent prompt with exact directive schemas and: `Never claim an application action completed; Workspace Host supplies the completion statement after observing the result.`

- [ ] **Step 4: Verify GREEN and commit**

```powershell
npm run native:test
npm test --workspace @workspace/spatial-client
npm run typecheck --workspace @workspace/spatial-client
npm run native:build
git add apps/desktop-native/src/Workspace.Desktop apps/desktop-native/src/Workspace.Desktop.Core/Runtime/CodaLocalCommandParser.cs apps/desktop-native/tests/Workspace.Desktop.Core.Tests/CodaLocalCommandParserTests.cs apps/spatial-client/src/onboarding
git commit -m "feat: let Coda operate Windows applications"
```

### Task 9: Full verification, activation, and acceptance

**Files:**
- Create: `docs/coda-application-control-acceptance.md`
- Modify: `README.md`

**Interfaces:**
- Consumes: complete slice.
- Produces: automated/live evidence and promoted native preview.

- [ ] **Step 1: Run repository gates**

```powershell
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
npm run native:test
npm run native:build
git diff --check
```

Expected: every command exits 0. Record exact pass counts and distinguish the existing Vite chunk advisory from failures.

- [ ] **Step 2: Audit the security boundary**

```powershell
rg -n "Process\.Kill|Kill\(|cmd\.exe|powershell|pwsh|executablePath|WM_CLOSE|application\.restart" apps/host-windows apps/desktop-native apps/spatial-client
```

Expected: Coda/renderer cannot submit executable paths or shell strings; normal app close uses `WM_CLOSE`; owned Workspace Desktop/Host/Codex cleanup is not reachable through `application.close`.

- [ ] **Step 3: Stage and activate safely**

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install-native-preview.ps1 -SourceRoot 'C:\Users\NeverAMoment\source\repos\workspace-environment' -SkipLaunch
Get-Content -LiteralPath "$env:LOCALAPPDATA\WorkspaceEnvironment\activation.json" -Raw
```

Verify the new version is pending, old known-good remains, and Electron is preserved. Launch the stable native launcher and wait until active/known-good name the new version.

- [ ] **Step 4: Exercise real Windows behavior**

Use voice and chat for:

```text
Search for Notepad.
Open Notepad on this screen.
Open Notepad on this screen again.
Open a new Notepad in front of me.
Restart this application.
Close this application.
```

Verify first open truthfully launches/reuses; second defaults to reuse; the explicit new-instance request attempts a new launch and reports reuse if Windows coalesces it; the unselected request creates a camera-relative nonoverlapping surface; selected surface retains its transform; denial causes no side effect; close succeeds only after disappearance; capture/input work after restart. Confirm the bounded audit store records semantic outcomes without captured content.

- [ ] **Step 5: Exercise a saved PythOS Codex profile**

Create an approved `PythOS Codex` profile for Windows Terminal with working directory `D:\PythOS-Workspace` and the exact displayed token list needed to start Codex. Request `Open PythOS Codex on this screen.` Verify the terminal binds to the selected display, starts Codex in that directory, and its remembered grant cannot authorize another app or altered arguments.

- [ ] **Step 6: Record, commit, and check final state**

Record commit/version, gates, application observations, approvals, and real dispositions in `docs/coda-application-control-acceptance.md`. Add concise README examples and capability rules.

```powershell
git add README.md docs/coda-application-control-acceptance.md
git commit -m "docs: record Coda application control acceptance"
git status --short
git log -10 --oneline
Get-Content -LiteralPath "$env:LOCALAPPDATA\WorkspaceEnvironment\activation.json" -Raw
```

Expected: only `.superpowers/` remains untracked, the acceptance commit is HEAD, and the new installed version is active and known-good.
