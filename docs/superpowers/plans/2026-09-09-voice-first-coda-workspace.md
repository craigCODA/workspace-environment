# Voice-First Coda Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a Windows 11 native Workspace Environment whose first-run welcome is spoken with bottom captions and whose subscription-authenticated Codex agent can converse by local voice, control the Three.js camera, remember scoped approvals, and rebuild or restart the workspace safely.

**Architecture:** Add a testable .NET native core and WinUI 3/WebView2 shell while retaining the existing Electron package as rollback. The native shell owns local speech, Codex App Server over stdio, capability memory, and host lifecycle; the renderer receives only typed visual and scene-control messages. A small launcher owns staged activation and rollback so the agent never overwrites its running executable.

**Tech Stack:** .NET 8, C# 12, WinUI 3 / Windows App SDK 1.8, WebView2, System.Speech, xUnit, TypeScript 5.9, Three.js 0.185, Node test runner, Codex App Server JSONL protocol, PowerShell packaging scripts.

**Spec:** `docs/superpowers/specs/2026-09-09-voice-first-coda-workspace-design.md`

## Global Constraints

- Windows 11 is the authoritative target; keep `net8.0-windows10.0.19041.0` compatibility with the existing host.
- Do not require or request an OpenAI API key. Codex must use `codex login` with ChatGPT subscription authentication.
- Microphone audio remains local and is not retained by default.
- Keep the renderer without filesystem, process, credential, or general-shell access.
- Preserve the existing Electron package until native Windows acceptance passes.
- Existing application capture, input cleanup, persistence, and restart/rebind tests must remain green.
- Treat `.superpowers/` as pre-existing user content and never add, edit, delete, or commit it.
- Each task ends in a focused commit after its tests pass.

---

### Task 1: Durable voice profile and capability memory

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Workspace.Desktop.Core.csproj`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Persistence/AtomicJsonStore.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Preferences/VoiceProfile.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Preferences/VoiceProfileStore.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Capabilities/CapabilityGrant.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Capabilities/CapabilityBroker.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/VoiceProfileStoreTests.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/CapabilityBrokerTests.cs`
- Create: `apps/desktop-native/Workspace.Desktop.sln`

**Interfaces:**
- Produces: `VoiceProfile`, `VoiceProfileStore.LoadAsync/SaveAsync`, `CapabilityGrant`, and `CapabilityBroker.IsGranted/Remember/Revoke`.
- Persists: schema-versioned JSON through `AtomicJsonStore<T>` using same-directory temporary files and atomic replacement.

- [ ] **Step 1: Write failing preference and capability tests**

```csharp
[Fact]
public async Task ReturningProfilePreservesNameAndVoiceToggles()
{
    var store = new VoiceProfileStore(Path.Combine(_temp, "voice-profile.json"));
    await store.SaveAsync(VoiceProfile.Default with
    {
        PreferredName = "Craig",
        OnboardingCompleted = true,
        CaptionsEnabled = true,
        ProactiveMode = ProactiveSpeechMode.IncludeCompletion,
    });

    var loaded = await store.LoadAsync();
    Assert.Equal("Craig", loaded.PreferredName);
    Assert.True(loaded.OnboardingCompleted);
}

[Fact]
public async Task RememberedGrantMatchesOnlyItsWorkspaceScope()
{
    var broker = await CapabilityBroker.OpenAsync(Path.Combine(_temp, "grants.json"));
    await broker.RememberAsync(new CapabilityGrant("build.run", @"C:\work\one", null));
    Assert.True(broker.IsGranted("build.run", @"C:\work\one"));
    Assert.False(broker.IsGranted("build.run", @"C:\work\two"));
}
```

- [ ] **Step 2: Run the focused tests and confirm they fail**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj --filter "VoiceProfileStoreTests|CapabilityBrokerTests"`

Expected: FAIL because the projects and types do not exist.

- [ ] **Step 3: Implement the stores and exact records**

```csharp
public sealed record VoiceProfile(
    int SchemaVersion,
    string? PreferredName,
    bool OnboardingCompleted,
    bool MicrophoneEnabled,
    bool CaptionsEnabled,
    bool TranscriptRetentionEnabled,
    ProactiveSpeechMode ProactiveMode,
    AgentNavigationMode NavigationMode,
    string WakePhrase)
{
    public static VoiceProfile Default { get; } = new(
        1, null, false, true, true, false,
        ProactiveSpeechMode.CriticalOnly,
        AgentNavigationMode.AskFirst,
        "Hey Coda");
}

public sealed record CapabilityGrant(
    string Capability,
    string Scope,
    DateTimeOffset? ExpiresAt);
```

`CapabilityBroker` must canonicalize Windows paths with `Path.GetFullPath`, use ordinal-ignore-case comparisons, reject empty/root-wide remembered scopes, and always require fresh confirmation for `credential.read`, `remote.publish`, `system.configure`, and `filesystem.destructive-outside-workspace`.

- [ ] **Step 4: Run core tests**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/desktop-native
git commit -m "feat: persist Coda preferences and capability grants"
```

### Task 2: Captioned first-run and returning welcome

**Files:**
- Delete: `apps/spatial-client/src/onboarding/WelcomeSequence.ts`
- Modify: `apps/spatial-client/src/onboarding/WelcomeSequence.test.ts`
- Create: `apps/spatial-client/src/onboarding/CodaPresence.ts`
- Create: `apps/spatial-client/src/onboarding/CodaPresence.test.ts`
- Create: `apps/spatial-client/src/native/WorkspaceNativeBridge.ts`
- Create: `apps/spatial-client/src/native/WorkspaceNativeBridge.test.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.test.ts`
- Modify: `apps/spatial-client/src/styles.css`

**Interfaces:**
- Produces: `CodaPresence.setState`, `CodaPresence.showCaption`, `CodaPresence.setTerminalEvents`, and `WorkspaceNativeBridge`.
- Consumes native messages shaped as `{ version: 1, type: string, payload: unknown }`.
- Falls back to an in-memory browser bridge when `window.chrome.webview` is unavailable so Electron development remains usable.

- [ ] **Step 1: Replace the welcome-copy test with failing caption and presence tests**

```ts
test('first-run copy is emitted for narration instead of rendered as a heading', () => {
  assert.deepEqual(FIRST_RUN_NARRATION.slice(0, 2), [
    'Welcome to your workspace environment.',
    "This is the place where we'll build the way you work.",
  ]);
  const presence = new CodaPresence(root);
  assert.equal(root.querySelector('h1'), null);
  presence.showCaption('Welcome to your workspace environment.');
  assert.equal(root.querySelector('[role="status"]')?.textContent,
    'Welcome to your workspace environment.');
});
```

- [ ] **Step 2: Run focused client tests and confirm failure**

Run: `npm test --workspace @workspace/spatial-client -- CodaPresence`

Expected: FAIL because `CodaPresence` and its bridge do not exist.

- [ ] **Step 3: Implement the spatial presence and bottom caption safe area**

The DOM must contain `.coda-presence[data-state]`, `.coda-caption[role=status]`, a hidden-by-default `.coda-transcript`, and `.coda-terminal`. The stylesheet must pin captions to the bottom center, limit text to two lines, apply a high-contrast translucent background, and avoid the old left-column layout.

- [ ] **Step 4: Wire native messages into application composition**

`createWorkspaceApp` must create `CodaPresence`, subscribe to native `voice.state`, `voice.caption`, `agent.event`, and `preference.changed` messages, and post `renderer.ready` only after the scene and workspace socket are ready. Error messages must flow through Coda's `needs-attention` state instead of the deleted welcome status.

- [ ] **Step 5: Run all spatial client checks**

Run: `npm test --workspace @workspace/spatial-client`

Run: `npm run typecheck --workspace @workspace/spatial-client`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/spatial-client
git commit -m "feat: replace static welcome with captioned Coda presence"
```

### Task 3: Structured camera navigation and scene understanding

**Files:**
- Create: `apps/spatial-client/src/navigation/CameraNavigator.ts`
- Create: `apps/spatial-client/src/navigation/CameraNavigator.test.ts`
- Create: `apps/spatial-client/src/navigation/SceneCommandController.ts`
- Create: `apps/spatial-client/src/navigation/SceneCommandController.test.ts`
- Modify: `apps/spatial-client/src/rendering/WorkspaceScene.ts`
- Modify: `apps/spatial-client/src/rendering/WorkspaceScene.test.ts`
- Modify: `apps/spatial-client/src/app/createWorkspaceApp.ts`

**Interfaces:**
- Produces: `WorkspaceScene.snapshot(): SceneSnapshot`, `WorkspaceScene.setCameraPose`, `CameraNavigator.navigate/cancel/tick`, and `SceneCommandController.handle`.
- Consumes: validated commands `scene.inspect`, `camera.navigate`, `camera.focus`, `camera.stop`, `camera.return-home`, `surface.move`, and `surface.resize`.

- [ ] **Step 1: Write failing deterministic navigation tests**

```ts
test('manual cancellation stops an in-flight agent navigation', () => {
  const target = new FakeCameraTarget();
  const navigator = new CameraNavigator(target);
  navigator.navigate({ position: { x: 4, y: 1.65, z: -2 }, yaw: 0.4, pitch: 0 },
    { mode: 'glide', durationMs: 1000 });
  navigator.tick(250);
  navigator.cancel('manual-input');
  const stopped = target.pose;
  navigator.tick(1000);
  assert.deepEqual(target.pose, stopped);
});
```

- [ ] **Step 2: Run navigation tests and confirm failure**

Run: `npm test --workspace @workspace/spatial-client -- CameraNavigator SceneCommandController`

Expected: FAIL because the navigation modules do not exist.

- [ ] **Step 3: Implement pure interpolation and command validation**

Use cubic ease-in/out for glide, clamp pitch to the existing range, cap duration at 30 seconds, reject non-finite coordinates, and emit a result for every command. `scene.inspect` returns camera pose plus entity id, kind, presentation, and selection; it never returns captured pixels. Surface movement and resizing reuse `ApplicationSurface.commitPresentation`, preserve authoritative rollback behavior, and reject unknown entity ids or non-positive dimensions.

- [ ] **Step 4: Integrate one navigation controller for keyboard and agent movement**

Keyboard `WASD`, mouse look, Escape, pointer movement, and spoken `camera.stop` must all route through the controller. Manual input cancels agent motion before applying the user's movement.

- [ ] **Step 5: Run spatial tests, typecheck, and build**

Run: `npm test --workspace @workspace/spatial-client`

Run: `npm run typecheck --workspace @workspace/spatial-client`

Run: `npm run build --workspace @workspace/spatial-client`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/spatial-client
git commit -m "feat: add agent-controlled spatial navigation"
```

### Task 4: Local Windows voice engine and conversation state machine

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/VoiceState.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/VoiceEvent.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/IWakeWordEngine.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/ISpeechRecognizer.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/ISpeechSynthesizer.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Voice/VoiceConversationController.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Windows/Workspace.Desktop.Windows.csproj`
- Create: `apps/desktop-native/src/Workspace.Desktop.Windows/Voice/SystemSpeechVoiceEngine.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/VoiceConversationControllerTests.cs`

**Interfaces:**
- Produces: local wake, transcript, caption, speaking, barge-in, and error events.
- `VoiceConversationController.StartAsync(VoiceProfile)`, `SpeakAsync`, `Pause`, `Resume`, `StopConversation`, and `DisposeAsync` must not depend on UI classes.

- [ ] **Step 1: Write failing voice state-machine tests with fakes**

Cover ambient speech ignored before wake, wake phrase entering active listening, silence returning to dormant, speech interruption cancelling synthesis, microphone toggle, captions toggle, first-run narration order, and returning greeting.

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj --filter VoiceConversationControllerTests`

Expected: FAIL because the controller does not exist.

- [ ] **Step 3: Implement the deterministic controller**

Use injected `TimeProvider` and voice interfaces. State transitions must be explicit and serialized through a `SemaphoreSlim`. Caption events carry `{ text, isFinal, utteranceId }`; no raw audio enters persistence APIs.

- [ ] **Step 4: Implement the Windows adapter**

Reference `System.Speech` from the Windows project. Load an exact wake-phrase grammar while dormant and local dictation grammar while active. Use `SpeechSynthesizer.SpeakAsync` plus `SpeakProgress` for caption timing. If no compatible recognizer is installed, return a typed recoverable failure and leave captions/text recovery available.

- [ ] **Step 5: Run core tests**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj`

Expected: PASS without requiring audio hardware.

- [ ] **Step 6: Commit**

```powershell
git add apps/desktop-native
git commit -m "feat: add local wake word and speech orchestration"
```

### Task 5: Subscription-authenticated Codex App Server client

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Agent/AgentEvent.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Agent/ICodingAgent.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Agent/CodexMessageTranslator.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Windows/Agent/CodexAppServerClient.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/CodexMessageTranslatorTests.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/CodexAppServerProtocolTests.cs`

**Interfaces:**
- Produces: `ICodingAgent.StartAsync`, `StartOrResumeThreadAsync`, `StartTurnAsync`, `SteerAsync`, `InterruptAsync`, and `IAsyncEnumerable<AgentEvent>`.
- Consumes: newline-delimited JSON from `codex app-server --stdio` after `initialize`/`initialized`.

- [ ] **Step 1: Write failing protocol translation tests**

Use captured protocol fixtures for initialize, thread start, turn start, assistant delta, command events, approval request, turn completion, malformed JSON, and unexpected server exit. Assert that credentials and raw environment variables never appear in translated events.

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj --filter "CodexMessageTranslatorTests|CodexAppServerProtocolTests"`

Expected: FAIL because agent types do not exist.

- [ ] **Step 3: Implement the stdio client**

Resolve `codex.exe` with `PATH`, start `codex app-server --stdio` with redirected stdin/stdout/stderr and no shell, send initialize metadata `{ name: "workspace_environment", title: "Workspace Environment", version: "0.2.0" }`, and use monotonically increasing request ids. Use the user's existing Codex config and ChatGPT login; never pass or inspect an API key.

Start threads with the selected source root as `cwd`, `approvalPolicy: "on-request"`, and the full-access sandbox only after the capability broker has established the workspace scope. Route each server-initiated approval request to the broker and voice/UI confirmation path.

- [ ] **Step 4: Add login-state handling**

Run `codex login status` as an argument vector. Parse only exit status and documented human-readable states. A signed-out result emits `AgentEvent.AuthenticationRequired`; Workspace Desktop opens a visible `codex login` process and retries after it exits successfully.

- [ ] **Step 5: Run core tests and a read-only live handshake smoke test**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj`

Run a temporary client turn with sandbox read-only and prompt `Reply with WORKSPACE_CODEX_OK and do not modify files.`

Expected: PASS and exact marker `WORKSPACE_CODEX_OK` through ChatGPT subscription auth.

- [ ] **Step 6: Commit**

```powershell
git add apps/desktop-native
git commit -m "feat: connect Coda to subscription-authenticated Codex"
```

### Task 6: WinUI 3 shell, WebView2 bridge, and Workspace Host lifecycle

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop/` from the official `Microsoft.WindowsAppSDK.WinUI.CSharp.Templates` WinUI template
- Modify: `apps/desktop-native/src/Workspace.Desktop/Workspace.Desktop.csproj`
- Modify: `apps/desktop-native/src/Workspace.Desktop/App.xaml.cs`
- Modify: `apps/desktop-native/src/Workspace.Desktop/MainWindow.xaml`
- Modify: `apps/desktop-native/src/Workspace.Desktop/MainWindow.xaml.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop/Bridge/WebViewMessage.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop/Bridge/WebViewBridge.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop/Runtime/WorkspaceHostProcess.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop/Runtime/DesktopCoordinator.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/WebViewMessageTests.cs`

**Interfaces:**
- Produces: native top-level window, WebView2 virtual-host mapping, typed bidirectional messages, host readiness, renderer readiness, and health state.
- Consumes: built spatial-client files and bundled `Workspace.Host.exe`.

- [ ] **Step 1: Install/use the official template and add projects to the solution**

Run: `dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates`

Run: `dotnet new winui -n Workspace.Desktop -o apps/desktop-native/src/Workspace.Desktop -tfm net8.0 -tpmv 10.0.19041.0`

Add project references to `Workspace.Desktop.Core` and `Workspace.Desktop.Windows`, and add all projects to `apps/desktop-native/Workspace.Desktop.sln`.

- [ ] **Step 2: Write failing typed-message validation tests**

Assert that version 1 known messages deserialize, unknown types are rejected, payloads over 256 KiB are rejected, unexpected properties do not become native command arguments, and scene result ids must match pending requests.

- [ ] **Step 3: Implement host and WebView lifecycle**

Launch the bundled host with `--parent-pid`, wait for the exact readiness marker, map `https://workspace.local/` to the spatial-client folder with deny-cors virtual-host access, deny external navigation, disable browser accelerator keys, and use `PostWebMessageAsJson` / `WebMessageReceived` only.

- [ ] **Step 4: Compose profile, voice, Codex, bridge, and health**

`DesktopCoordinator` loads preferences, starts the host, initializes WebView2, waits for `renderer.ready`, starts voice, starts Codex, sends the first-run narration or returning greeting, and reports healthy only after required components settle. Voice and Codex survive a renderer reload.

- [ ] **Step 5: Build the native shell**

Run: `dotnet build apps/desktop-native/Workspace.Desktop.sln --configuration Release`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add apps/desktop-native
git commit -m "feat: host the workspace in a native WinUI shell"
```

### Task 7: Voice-to-agent orchestration and spatial terminal

**Files:**
- Create: `apps/desktop-native/src/Workspace.Desktop/Runtime/CodaCoordinator.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop/Runtime/ProactiveSpeechPolicy.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/ProactiveSpeechPolicyTests.cs`
- Modify: `apps/spatial-client/src/onboarding/CodaPresence.ts`
- Modify: `apps/spatial-client/src/styles.css`

**Interfaces:**
- Consumes: final voice transcripts and `AgentEvent` stream.
- Produces: spoken/captioned responses, live terminal events, remembered approval prompts, scene commands, and quiet/critical/completion proactive behavior.

- [ ] **Step 1: Write failing policy and coordinator tests**

Verify that `critical-only` speaks failures/risks/blocked decisions but not success, `include-completion` also speaks success, `quiet` speaks nothing proactively, and custom event toggles are exact. Verify that command output appears in the terminal surface but is not narrated verbatim. Verify that a first-run answer is stored as the preferred name and that later launches produce `Welcome back, <name>.`

- [ ] **Step 2: Implement Coda coordination**

Prefix agent turns with a compact structured scene snapshot and durable instruction that Coda may use only the brokered capabilities. Speak final agent responses sentence-by-sentence, summarize long command activity, preserve command output for the terminal, and handle `stop`, `pause`, `repeat`, `show terminal`, `hide terminal`, `list permissions`, `forget permission`, `reset onboarding`, name changes, microphone/caption/transcript toggles, proactive-speech modes, and navigation modes locally before sending general requests to Codex. During first run, treat the answer to Coda's name question as profile setup rather than an agent turn.

- [ ] **Step 3: Add approval voice flow**

For an approval request, announce the exact action and scope. Recognize `allow once`, `remember this`, and `deny`. Only `remember this` writes a grant; fresh-confirmation capabilities ignore attempts to persist.

- [ ] **Step 4: Run native and spatial tests**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj`

Run: `npm test --workspace @workspace/spatial-client`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add apps/desktop-native apps/spatial-client
git commit -m "feat: orchestrate voice-driven Codex work"
```

### Task 8: Versioned launcher, staged activation, and rollback

**Files:**
- Create: `apps/desktop-native/src/Workspace.Launcher/Workspace.Launcher.csproj`
- Create: `apps/desktop-native/src/Workspace.Launcher/Program.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Launcher/VersionManifest.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Launcher/VersionSelector.cs`
- Create: `apps/desktop-native/src/Workspace.Desktop.Core/Launcher/ActivationStore.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/VersionSelectorTests.cs`
- Create: `apps/desktop-native/tests/Workspace.Desktop.Core.Tests/ActivationStoreTests.cs`
- Create: `scripts/prepare-native-desktop.ps1`

**Interfaces:**
- Produces: active/pending/known-good pointers, health handshake, restart request, rollback, and a versioned staging layout under `%LOCALAPPDATA%\WorkspaceEnvironment\versions`.

- [ ] **Step 1: Write failing activation and rollback tests**

Cover first activation, healthy pending activation, pending timeout rollback, invalid manifest rejection, executable hash mismatch, retention of known-good, and exact-child process cleanup.

- [ ] **Step 2: Implement atomic activation records**

The launcher accepts `--state-root`, `--source-root`, and `--once` for tests. It validates a staged manifest containing relative paths and SHA-256 hashes, starts only the exact desktop executable in the chosen version, and waits for a per-launch named-pipe health token. It never evaluates a shell command from the manifest.

- [ ] **Step 3: Implement the build/stage script**

`prepare-native-desktop.ps1` runs the spatial build, publishes Workspace Host self-contained, publishes Workspace Desktop and Launcher, copies only declared assets, writes hashes, and places the result in a new timestamp-plus-commit version directory. It fails before changing activation state if any command fails.

- [ ] **Step 4: Run launcher tests and stage a local version**

Run: `dotnet test apps/desktop-native/tests/Workspace.Desktop.Core.Tests/Workspace.Desktop.Core.Tests.csproj --filter "VersionSelectorTests|ActivationStoreTests"`

Run: `powershell -ExecutionPolicy Bypass -File scripts/prepare-native-desktop.ps1 -Configuration Release -OutputRoot C:\tmp\workspace-native-stage`

Expected: PASS with a complete manifest.

- [ ] **Step 5: Commit**

```powershell
git add apps/desktop-native scripts/prepare-native-desktop.ps1
git commit -m "feat: stage and roll back native workspace versions"
```

### Task 9: Packaging, documentation, and Windows acceptance

**Files:**
- Modify: `package.json`
- Modify: `README.md`
- Create: `docs/voice-first-coda-acceptance.md`
- Create: `scripts/install-native-preview.ps1`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: `npm run native:build`, `npm run native:test`, `npm run native:stage`, and an explicit native-preview installer that preserves the existing Electron installation.

- [ ] **Step 1: Add orchestration scripts**

Add root scripts that install the official WinUI template when absent, restore/build/test the native solution, build the spatial client, stage a version, and install a side-by-side `Workspace Environment Native Preview` shortcut. The preview installer must never delete or overwrite the existing Electron install.

- [ ] **Step 2: Run the complete automated verification**

Run: `npm install`

Run: `npm test`

Run: `npm run typecheck`

Run: `npm run build`

Run: `dotnet test apps/host-windows/Workspace.Host.sln --configuration Release`

Run: `npm run native:test`

Run: `npm run native:stage`

Expected: all commands PASS; the existing Electron package remains buildable.

- [ ] **Step 3: Perform controlled Windows acceptance**

Install the native preview side-by-side, launch it, capture the first visible frame and narration captions, verify local speech devices, say `Hey Coda`, run the read-only marker turn, approve a fixture build with `remember this`, ask Coda to navigate to the terminal surface, rebuild the renderer, and verify the same agent thread resumes after reload.

Then stage a deliberately invalid preview version and verify automatic rollback without modifying the known-good installation.

- [ ] **Step 4: Record evidence without secrets**

`docs/voice-first-coda-acceptance.md` records versions, commands, test totals, observed voice state transitions, restart/rollback outcomes, limitations, and whether physical microphone/speaker acceptance was completed. It must not contain credentials, raw audio, private transcript content, or machine-wide process dumps.

- [ ] **Step 5: Commit**

```powershell
git add package.json README.md .github/workflows/ci.yml docs/voice-first-coda-acceptance.md scripts/install-native-preview.ps1
git commit -m "feat: package and verify voice-first Workspace Environment"
```

### Task 10: Final regression and installed-view verification

**Files:**
- Modify only files required by defects proven during this task.

**Interfaces:**
- Consumes all prior deliverables.
- Produces a clean, reviewable branch and a running native preview.

- [ ] **Step 1: Run repository hygiene checks**

Run: `git status --short`

Run: `git diff --check`

Run: `git ls-files .superpowers`

Expected: only intentional feature files are tracked; `.superpowers/` remains untracked and untouched.

- [ ] **Step 2: Run complete verification from a fresh process**

Repeat root tests, TypeScript checks/build, host Release tests, native tests/build/stage, and the side-by-side preview launch. Do not reuse a still-running development host or prior renderer process.

- [ ] **Step 3: Capture and inspect the installed native preview**

Observe startup from process creation through the returning greeting. Verify there is no large onboarding overlay, captions are bottom-aligned, the Three.js world remains navigable, and the app reports healthy after voice, host, renderer, and Codex initialization.

- [ ] **Step 4: Commit only proven final corrections**

Review `git diff --name-only`, stage each proven correction by its literal path, and commit with `git commit -m "fix: complete native voice workspace acceptance"`. If verification found no defect, leave this step commit-free.

- [ ] **Step 5: Report branch state**

Report the branch name, exact HEAD SHA, commits, verification commands and results, installed preview path, remaining limitations, and the fact that no remote publication or merge occurred.
