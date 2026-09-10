# Workspace Environment

Workspace Environment is a Windows-first spatial workspace where the PC remains authoritative. Real Windows applications stay real processes and windows; the workspace gives them durable semantic identity, relationships, capabilities, and a persistent spatial presentation.

V0 proves one complete path: discover an installed application, launch its ordinary Windows process, resolve its top-level window, capture it as a live Three.js surface, route input back to it, move and resize the spatial representation, and restore that representation after transient PID/HWND replacement.

The canonical designs and implementation plans are:

- `docs/superpowers/specs/2026-09-07-workspace-environment-design.md`
- `docs/superpowers/plans/2026-09-07-v0-real-app-spatial-surface.md`
- [V0 acceptance record](docs/v0-acceptance.md)
- [Voice-first Coda design](docs/superpowers/specs/2026-09-09-voice-first-coda-workspace-design.md)
- [Voice-first Coda acceptance record](docs/voice-first-coda-acceptance.md)

## Prerequisites

- Installed application: Windows 10 version 2004 (build 19041) or newer, or Windows 11
- Building from source: .NET 8 SDK, Node.js 24, and npm
- An unelevated interactive Windows desktop session
- Microsoft Edge for the human acceptance path

The installed application includes its own Electron runtime, production spatial client, and self-contained .NET host. It does not require Node.js, npm, the .NET runtime, or Vite on the target machine.

The native preview also needs the Codex CLI signed in with a ChatGPT subscription. It does not request an OpenAI API key. Speech recognition, the `Hey Coda` wake phrase, and speech synthesis run through local Windows speech services.

## Native voice-first preview

Build, stage, install, and launch the side-by-side native preview:

```powershell
npm install
npm run native:test
npm run native:build
npm run native:install
```

This creates **Workspace Environment Native Preview** shortcuts without changing or deleting the existing Electron installation. Versioned builds live under `%LOCALAPPDATA%\WorkspaceEnvironment\versions`; the stable launcher promotes a pending build only after its health handshake and rolls back to the last known-good version if validation or startup fails.

On first launch, Coda speaks the welcome while synchronized captions appear at the bottom, then asks what to call you and confirms what it heard before saving. Later launches say `Welcome back, <name>.` After that, ambient speech is ignored until `Hey Coda` is detected. The **Talk** control can start the same conversation without a wake phrase. Microphone, captions, transcript display, activity, proactive alerts, and navigation policy remain directly controllable in the workspace.

Coda can inspect structured scene state, guide the camera, focus named surfaces, move or resize surfaces, run builds, and request a restart. Approval prompts accept `allow once`, `remember this`, or `deny`. Remembered permissions are exact and project-scoped; credentials, remote publication, system configuration, and destructive work outside the workspace always require fresh confirmation.

## Windows installer

Build the installer from the repository root:

```powershell
npm install
npm run dist:win
```

Outputs:

```text
dist\Workspace Environment Setup 0.1.0.exe
dist\win-unpacked\Workspace Environment.exe
```

The Electron one-click installer remains the rollback package. It is per-user and requires no administrator prompt. Launching **Workspace Environment** automatically starts the bundled Windows host and opens the existing Three.js environment. The current build is unsigned, so Windows SmartScreen may display `Unknown publisher`.

## Run from source through Electron

```powershell
npm install
npm run electron
```

This builds the spatial client, starts the Windows host, waits for it to become ready, and opens the secure Electron shell. Closing the application stops the host automatically.

## Run components separately

For host/client diagnostics, start the authoritative Windows host in one terminal:

```powershell
dotnet run --project apps/host-windows/src/Workspace.Host/Workspace.Host.csproj
```

Start the Three.js client in another terminal:

```powershell
npm run dev --workspace @workspace/spatial-client -- --host 127.0.0.1
```

Open the loopback URL printed by Vite, then use **Open Microsoft Edge**. The fixed host endpoint is `ws://127.0.0.1:41771/workspace`; it rejects non-loopback origins and V0 permits one spatial client connection at a time.

The host stores authoritative semantic and presentation state at:

```text
%LOCALAPPDATA%\WorkspaceEnvironment\workspace.json
```

On a selected application surface:

- click, type, and scroll operate the real Windows window;
- `Alt` + drag moves the spatial representation;
- `Alt` + `Shift` + drag resizes it;
- `Alt` + arrow keys provide precise movement;
- `Alt` + `Shift` + arrow keys provide precise resizing.

## Verify

```powershell
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
npm run native:test
npm run native:build
npm run dist:win
```

The solution includes `Workspace.TestWindow`, an ordinary first-party WinForms application with a changing visual indicator, text input, counter button, and scrollable region. It has no private host backchannel and is used as a deterministic real-window acceptance target.

Current boundaries include multi-window role reconciliation beyond one durable main window per application, elevated-application input, Quest/WebXR, remote streaming, code signing, and the broader file/project/process semantic model. Native Coda is a local preview and the Electron package remains installed as the known fallback.
