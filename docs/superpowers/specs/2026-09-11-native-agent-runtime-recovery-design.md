# Native Agent Runtime Recovery Design

## Problem

The Windows installer artifact produced by `windows-ci` is the Electron fallback package. The spatial renderer therefore shows Coda and provider controls, but `WorkspaceNativeBridge` resolves to `BrowserFallbackTransport`, so `agent.instruction` messages never reach `DesktopCoordinator`. The Electron package cannot start Codex login, start an xAI agent, or return agent responses.

The native Codex path also assumes an executable named `codex.exe` exists directly on PATH. Common Windows CLI installs can expose command shims such as `codex.cmd`, so a usable Codex installation can be missed before login is attempted.

## Required behavior

- CI publishes a native self-contained Workspace Environment artifact in addition to any Electron fallback artifact.
- The native bundle launches `Workspace.Desktop` through the existing versioned launcher and includes the bundled Windows host and spatial client.
- Coda labels the Codex-backed subscription provider as `ChatGPT (Codex)` without pretending ordinary ChatGPT is directly embedded as an inference API.
- Codex command resolution supports Windows executable and command-shim forms.
- A failed agent startup does not abort the entire Workspace native shell. Coda remains available, reports the provider failure visibly, and permits switching providers.
- Browser/Electron fallback mode never silently accepts an agent instruction. It returns a clear Coda status/caption that the native agent runtime is unavailable in that shell.

## Boundaries

- No OpenAI API key path is added.
- Ordinary consumer ChatGPT is not represented as a direct programmable provider. ChatGPT subscription authentication continues through Codex.
- Grok continues to require `XAI_API_KEY`.
- The Electron fallback remains supported for renderer/host development, but is no longer the artifact presented as the native Coda build.

## Verification checkpoint

The recovery slice is verified through staged RED/GREEN gates for provider labeling, fallback error visibility, Windows Codex command-shim resolution, native artifact staging, and the final full Windows pipeline.
