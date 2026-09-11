# V0 real-application acceptance

## Acceptance statement

V0 demonstrates that an ordinary Windows application can become a persistent, live, interactive Three.js object while Windows remains the owner of the real process and window. The host owns semantic identity and durable presentation; HWND, PID, capture stream IDs, texture IDs, and WebSocket IDs remain transient runtime observations.

This record separates automated coverage from behavior observed on an interactive Windows desktop.

## Automated verification

Run from the repository root:

```powershell
npm install
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
npm run dist:win
```

The automated suites cover:

- stable application and window semantic identity independent of PID, HWND, and title;
- atomic workspace persistence and presentation-only mutation;
- generic application catalog and process-launch boundaries;
- window reconciliation and replacement capture;
- versioned protocol validation, deterministic command correlation, and host-authoritative replicas;
- persist-before-`PRESENTATION_UPDATED` ordering;
- live frame stream ownership and unavailable-window recovery;
- normalized pointer, wheel, key, and text intents;
- pointer/key release on disconnect, bounded input leases after window loss, and exact permission errors;
- local presentation preview, failed-write rollback, and serialized repeated edits;
- the sparse Three.js environment and first-run orientation geometry.

`Workspace.TestWindow` is built with the host solution. It is an ordinary WinForms top-level window containing:

- a color-changing, numbered live-frame indicator;
- a native multiline text input;
- a native increment button and counter;
- a native scrollable region with forty markers.

It exposes no test-only protocol, host callback, shared-memory channel, or other private control path. Automation must observe and operate it through the same Windows window/capture/input boundaries used for third-party applications.

To make a local Release build discoverable through the same catalog boundary, register its executable through the standard per-user Windows `App Paths` key before starting the host:

```powershell
dotnet build apps/host-windows/src/Workspace.TestWindow/Workspace.TestWindow.csproj --configuration Release
$testWindow = (Resolve-Path 'apps/host-windows/src/Workspace.TestWindow/bin/Release/net8.0-windows10.0.19041.0/Workspace.TestWindow.exe').Path
$appPathKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\App Paths\Workspace.TestWindow.exe'
if (Test-Path -LiteralPath $appPathKey) { throw "Refusing to overwrite $appPathKey" }
New-Item -Path $appPathKey | Out-Null
Set-Item -Path $appPathKey -Value $testWindow
```

After the host starts, a protocol-v1 client launches it using the ordinary command envelope:

```json
{
  "protocol": 1,
  "type": "command",
  "id": "launch-test-window",
  "operation": "application.launch",
  "target": "Workspace Environment Test Window"
}
```

Remove only the temporary key created above when acceptance is complete:

```powershell
if ((Get-Item -LiteralPath $appPathKey).GetValue('') -eq $testWindow) {
  Remove-Item -LiteralPath $appPathKey
}
```

## Interactive Windows verification

Verified on 2026-09-08 using Windows 11 Home build 26200 in an unelevated desktop session.

### Deterministic real-window proof

The test executable was registered temporarily through the standard current-user Windows `App Paths` mechanism so the generic application catalog could discover it by the human-facing name `Workspace Environment Test Window`. The host was restarted after registration; no special test application branch was added. The temporary registration and acceptance window were removed after the proof.

Observed results:

1. `application.launch` returned application ID `pc.application:61f6559f1ebcecd73033a033`, window entity ID `pc.window:pc.application:61f6559f1ebcecd73033a033`, and `surfaceAvailable: true`.
2. The changing indicator produced different captured frame pixels while displayed on the Three.js surface.
3. Typing through the surface produced the native TextBox value `V0OK`.
4. Clicking through the surface changed the native counter from `COUNT 000` to `COUNT 001`.
5. Scrolling through the surface changed the native panel scroll position from `0` to `644`.
6. Spatial commands moved the surface to `y = 2.0` and resized it to width `3.4`; the complete presentation was stored in `%LOCALAPPDATA%\WorkspaceEnvironment\workspace.json`.
7. The first runtime used PID `45604` and HWND `3082586`. After closing the application and restarting the host, the generic launch produced PID `19636` and HWND `3148122`.
8. The semantic application/window IDs and the persisted `y = 2.0`, width `3.4` presentation remained unchanged, and the replacement runtime rendered live at the restored placement.

### Microsoft Edge human acceptance

Microsoft Edge went through the same generic catalog, launch, window reconciliation, capture, input, and presentation pipeline.

Observed results:

1. The first-run **Open Microsoft Edge** action launched/focused real Edge through `application.launch`; the spatial client displayed a live capture, not a fabricated browser or screenshot fixture.
2. A click selected the Edge surface. `Ctrl+L`, text input, and `Enter` sent through the surface navigated a real Edge window to the Wikipedia `Computer` article; its native title became `Computer - Wikipedia - Profile 1 - Microsoft Edge`.
3. Wheel input through the surface changed Edge UI Automation's document scroll percentage from `0.00` to `1.38`.
4. Spatial move/resize changed only presentation state. The authoritative store recorded `x = -2.0`, width `3.2`, and height `2.2` for the durable Edge window entity.
5. After restarting the Windows host without relaunching Edge, the live surface resolved again and restored at that persisted placement and size.

The deterministic window replacement above proves the same semantic reconciliation path across actual PID/HWND replacement without closing unrelated user Edge windows during acceptance.

## Electron and installer acceptance

Verified on 2026-09-09 on the same Windows 11 machine:

1. `npm run electron` built the spatial client, started the C# host automatically, waited for its explicit readiness message, and opened a responsive `Workspace Environment` Electron window.
2. The development shell bound immutable renderer assets to a random `127.0.0.1` port and connected to the unchanged host endpoint on `41771`.
3. `npm run dist:win` produced both the unpacked executable and one-click NSIS installer.
4. The unpacked executable started `Workspace.Host.exe` from packaged `resources\host` without a Vite or .NET SDK process.
5. The installer completed with exit code `0`, installed per-user under `%LOCALAPPDATA%\Programs\workspace-environment`, and created desktop and Start menu shortcuts.
6. The installed executable launched its bundled host and sandboxed renderer. A second launch focused the existing instance rather than creating another host.
7. Normal close and forced Electron termination both left zero owned host processes and zero `41771` listeners.

The verified installer is `dist\Workspace Environment Setup 0.1.0.exe` (141,279,423 bytes), with SHA-256 `3FA4F4FB9AAB043D4B9C20879A23DFEB9903508CC38CCC3BFD751149C7F87CA2`. It is intentionally unsigned for this slice.

## Interaction baseline acceptance

The current interaction baseline adds explicit pointer ownership and compact agent controls without changing the Windows surface authority model.

Verify these behaviors on an interactive Windows desktop:

1. Hover a live application surface. The operating-system cursor remains visible while the in-world surface cursor and normalized Windows pointer routing continue to update.
2. Click the primary mouse button on empty workspace. Pointer Lock engages, the native cursor hides, and relative mouse movement rotates the camera without holding a mouse button.
3. Press `Escape`. The browser releases Pointer Lock, the native cursor returns, and normal Coda/application-surface interaction resumes.
4. Restart the app. Coda starts compact with its chat and activity panels collapsed while the beacon, voice state, and controls remain available.
5. Open the **Agent** selector. `Codex`, `SpaceXAI`, and `Cursor (soon)` are visible. Changing the selection uses the existing `agentProvider` preference path; Cursor retains its current compatibility/fallback runtime behavior.

## Default ChatGPT startup acceptance

The default application startup uses the ordinary typed application-control path. ChatGPT receives no executable-path, shell, package, or credential exception.

Verify these behaviors on an interactive Windows desktop:

1. With the signed-in Windows ChatGPT application already open, start Workspace Environment. The existing ChatGPT window is reused and appears through its durable spatial surface rather than launching a duplicate instance.
2. With ChatGPT installed but closed, start Workspace Environment. The host launches ChatGPT through `application.open` with `reuseOrLaunch`, observes its real top-level window, and presents that window as a spatial surface.
3. Move or resize the ChatGPT surface, close Workspace Environment, leave ChatGPT running, and start Workspace Environment again. The durable ChatGPT window/surface identity and stored presentation are reused.
4. On a Windows machine where ChatGPT is not installed or where the `ChatGPT` application search is not uniquely resolved, Workspace Environment still reaches its normal ready state and remains usable. No guessed application is launched.
5. A ChatGPT-specific search/open failure does not put Coda into the host-disconnected state; only failure of the underlying Workspace Host connection uses that health path.

## ChatGPT presentation-mode acceptance

Automated Windows CI for the presentation-mode implementation passed on commit `aea3dc6b88c797d7e0ce5c60f8f65596510f9e64`, including TypeScript tests/typecheck, Windows-host tests, native Coda tests, native Windows preview build, NSIS installer build, and installer artifact upload.

Verify these behaviors on an interactive Windows desktop before recording human acceptance:

1. Start Workspace Environment with ChatGPT available. The one resolved ChatGPT surface starts docked at the right side of the view.
2. Click **Undock**. The same live ChatGPT surface returns to its durable world position without launching another ChatGPT process or window.
3. Move the undocked ChatGPT surface, Dock it, then Undock it again. The moved durable position and size return exactly; camera-local dock coordinates are never persisted.
4. Click **Collapse**, then **Show**. Only renderer visibility changes; ChatGPT remains the same real window and returns in the presentation mode it had before collapse.
5. Click **Focus**. Workspace activates the same semantic `pc.window` through the typed `window.focus` operation.
6. Pointer, wheel, key, and text input continue to route to the same ChatGPT window in both docked and spatial modes.
7. While ChatGPT is docked, Alt-drag and Alt-arrow placement editing do not write camera-local coordinates into durable presentation state.
8. Issue typed `surface.dock` and `surface.collapse` scene directives. The renderer state changes through the same generic scene primitives used by the control strip, and the ChatGPT controls remain synchronized with scene authority.
9. Restart Workspace Environment after moving ChatGPT in spatial mode. Its durable spatial presentation survives restart and is restored when the surface is undocked.

## V0 boundaries

V0 is deliberately narrow:

- The host listens only on `127.0.0.1:41771`; LAN/remote and Quest clients are not enabled.
- V0 permits one spatial client connection at a time.
- The host is unelevated. Windows integrity boundaries can reject input to elevated targets with `INPUT_TARGET_NOT_PERMITTED`; the host does not self-elevate.
- Multiple top-level windows for one application currently reconcile to one durable `main` window role. Per-tab and multiple durable-window heuristics are deferred.
- Capture is a local desktop frame transport for the V0 proof, not remote-rendering or PCVR streaming.
- The Electron/NSIS package is Windows x64 only and is currently unsigned; automatic updates and code signing are deferred.
- Quest/WebXR, agents, MCP control, remote workers, audio, clipboard, notifications, files, projects, terminals, and other broader semantic resources are not claimed as implemented.
- The host does not automatically commit, push, merge, publish, or otherwise grant applications or future agents unrelated authority.
