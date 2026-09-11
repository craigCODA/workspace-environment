# Workspace Environment Windows Installer Design

**Date:** 2026-09-08
**Status:** Approved in chat; recorded for implementation review

## Purpose

Package the completed V0 Workspace Environment as a normal Windows desktop application. A user installs one application and launches one shortcut. The application starts the authoritative C# Windows host and opens the existing Three.js spatial client without requiring terminals, a separately installed .NET runtime, or a development server.

This adds a desktop delivery layer. It does not redesign the spatial environment, move Windows authority into Electron, or change the semantic world model.

## User experience

The supported commands are:

```powershell
npm run electron
npm run dist:win
```

`npm run electron` runs the desktop shell from the repository for development. It builds the current spatial client, starts the C# host through the local .NET SDK, waits for the host to become ready, and opens the spatial environment.

`npm run dist:win` produces:

```text
dist\Workspace Environment Setup 0.1.0.exe
dist\win-unpacked\Workspace Environment.exe
```

The installer is an NSIS one-click, per-user installation. It does not require elevation and creates ordinary Start menu and desktop shortcuts. Uninstalling the program removes installed binaries but preserves `%LOCALAPPDATA%\WorkspaceEnvironment\workspace.json`, because that file is user workspace state rather than disposable application data.

The installed executable starts the complete application. The user does not start the host, run Vite, select a port, or install .NET.

## Chosen architecture

Electron is a thin native desktop shell around the existing components:

```text
Workspace Environment.exe
|
+-- Electron main process
|   +-- owns desktop application lifecycle
|   +-- enforces a single application instance
|   +-- serves packaged spatial assets on random loopback port
|   +-- starts and supervises Workspace.Host.exe
|   +-- creates secured BrowserWindow after host readiness
|
+-- Three.js spatial client
|   +-- remains the visual and interaction layer
|   +-- connects to ws://127.0.0.1:41771/workspace
|
+-- self-contained .NET 8 Windows host
    +-- remains authoritative for semantic state
    +-- owns application/window discovery, capture, input, and persistence
```

This follows the already approved separation: Electron owns desktop presentation and lifecycle; C# owns deep Windows behavior and authoritative workspace state.

### Alternatives rejected

1. **WebView2 or MSIX shell:** potentially smaller, but it replaces more of the proven browser runtime, introduces another Windows packaging model, and provides less direct reuse of the established NerdLife Electron approach.
2. **Installer that launches the current development commands:** smaller initial change, but it would require Node, npm, Vite, and .NET on the user's machine and would not be a self-contained application.
3. **Move the host into Electron:** rejected because JavaScript must not replace the existing tested C# Windows authority.

## Repository structure

The desktop shell lives under `apps/desktop-shell`. Root scripts expose the human-facing commands and coordinate builds. Generated packaging inputs use an ignored staging directory rather than being committed.

Expected additions and changes include:

```text
apps/desktop-shell/
  main.cjs
  lib/
    host-process.cjs
    static-server.cjs
  test/
scripts/
  prepare-desktop.mjs
package.json
package-lock.json
.gitignore
README.md
```

The exact file split may be refined by the implementation plan, but native lifecycle, host supervision, and static-file serving remain independently testable units.

## Build and packaging

The packaging preparation step performs two explicit builds:

1. Build `apps/spatial-client` with Vite.
2. Publish `Workspace.Host` for `win-x64` using .NET 8 Release, self-contained folder deployment.

Folder deployment is intentional. It avoids imposing single-file extraction or WinRT/native loading assumptions on the Windows capture implementation. The published host directory is placed outside `app.asar` through `electron-builder` `extraResources`. The spatial production build is packaged as immutable application content.

Electron and `electron-builder` are development dependencies. The builder configuration uses:

- product name `Workspace Environment`;
- application ID `com.nerdlife.workspaceenvironment`;
- Windows x64 target;
- NSIS one-click installation;
- per-user scope with elevation disabled;
- ASAR for Electron application code;
- a deterministic artifact name;
- Start menu and desktop shortcuts.

Code signing is not part of this slice because no signing certificate has been supplied. The unsigned installer may therefore show Windows SmartScreen's `Unknown publisher` warning. Signing can be added later without changing runtime architecture.

## Runtime startup flow

1. Electron acquires the single-instance lock. A second launch focuses the existing window and exits.
2. Electron opens a local log under `%LOCALAPPDATA%\WorkspaceEnvironment\logs`.
3. Electron starts the host:
   - development: the checked-out host through `dotnet run`;
   - packaged: the bundled self-contained `Workspace.Host.exe` from `process.resourcesPath`.
4. Electron waits for the child process's explicit listening message. Early exit, timeout, or port collision is treated as a startup failure.
5. Electron starts a static HTTP server on an operating-system-assigned `127.0.0.1` port and serves only the built spatial-client directory.
6. Electron creates the secured `BrowserWindow` and loads that loopback URL.
7. The spatial client connects to the unchanged, fixed host endpoint at `ws://127.0.0.1:41771/workspace`.

The main window is not loaded before the host is ready. This avoids a permanent disconnected/loading state caused by a one-time client connection racing host startup. Normal startup should transition directly into the existing spatial environment.

The host receives the Electron parent PID and monitors it. If Electron terminates abnormally, the host exits instead of remaining as an orphan that occupies port `41771`. During normal shutdown Electron closes the asset server, asks the host process to terminate, waits briefly, then force-terminates only that exact child if necessary.

## Static asset server

The packaged renderer is served over a random loopback HTTP port rather than `file://` or a broadly trusted custom scheme. This preserves the host's existing loopback-origin policy and gives browser APIs a conventional secure boundary.

The server:

- binds only to `127.0.0.1` with port `0`;
- serves only files beneath the spatial-client build root;
- rejects traversal and malformed paths;
- supplies explicit MIME types;
- maps `/` and client-side fallback requests to `index.html` only when appropriate;
- applies a restrictive Content Security Policy allowing the local renderer and the fixed loopback WebSocket endpoint;
- closes when Electron exits.

It is transport support for packaged immutable assets, not a new application backend.

## Electron security

The main window uses:

- `contextIsolation: true`;
- `nodeIntegration: false`;
- Chromium sandboxing enabled;
- web security enabled;
- no broad preload API;
- navigation restricted to the exact generated loopback origin;
- popup/window creation denied;
- developer tools disabled in packaged builds and available in development.

The renderer never receives filesystem, process, shell, or Electron APIs. Existing host commands remain the only application/window authority exposed to the spatial client.

## Host lifecycle extension

`Workspace.Host` gains a narrow optional parent-process monitor. The host accepts a positive `--parent-pid` value only for lifecycle supervision. The PID is transient runtime state and is never written into workspace entities or persistence.

Invalid arguments fail startup explicitly. If the monitored parent exits, the host cancels its existing shutdown token and follows the normal listener/window-monitor disposal path. Running the host directly without `--parent-pid` remains supported for development and diagnostics.

## Failure behavior

- **Bundled host missing:** show an explicit native startup error and exit.
- **Host exits before ready:** show the captured error summary and direct the user to the desktop log.
- **Host readiness timeout:** terminate only the child started by Electron, report the failure, and exit.
- **Port `41771` occupied:** fail honestly rather than silently attaching to an unknown host.
- **Renderer assets missing:** fail before opening the main window.
- **Renderer server failure:** show an explicit startup error and shut down the host child.
- **Capture unavailable:** preserve the existing unavailable-surface behavior inside the spatial client.
- **Unexpected renderer crash:** preserve host state on disk; Electron may recreate the window without restarting or duplicating the host.

No failure path deletes or resets the user's persisted workspace.

## Testing and acceptance

Implementation follows TDD. Pure Node modules cover:

- packaged versus development host command resolution;
- exact child ownership and shutdown decisions;
- readiness, early-exit, and timeout handling;
- static-server path containment, MIME behavior, CSP, and loopback binding;
- packaged resource path resolution.

The host test suite covers:

- valid and invalid `--parent-pid` parsing;
- parent-exit cancellation without persistent PID leakage;
- unchanged direct-host behavior.

Existing Node, TypeScript, protocol, host, capture, input, and persistence tests remain green.

Build verification runs:

```powershell
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
npm run dist:win
```

Artifact acceptance requires:

1. The unpacked executable starts one Electron instance and one bundled host.
2. No .NET SDK/runtime or Vite server is used by the packaged application.
3. The installer installs per-user and launches from its installed shortcut.
4. The installed spatial client opens Microsoft Edge through the generic application path.
5. A live surface renders and accepts click, text, and wheel input.
6. Move/resize persists across full application restart.
7. Normal exit leaves no Electron-owned host process or listener behind.
8. Uninstall removes installed binaries while preserving workspace state.

Windows CI must build the unpacked directory and NSIS installer on `windows-latest`. Interactive capture/input acceptance remains a real Windows desktop check because hosted CI is not evidence of interactive desktop behavior.

## Explicit non-goals

This slice does not add:

- automatic updates;
- code-signing credentials or certificate acquisition;
- a macOS or Linux package;
- ARM64 packaging;
- Electron-side semantic authority;
- new repository, agent, Quest, remote-streaming, or PythSpace features;
- a redesign of the Three.js environment;
- deletion of persisted user state during uninstall;
- merge or readiness changes to draft PR #1.
