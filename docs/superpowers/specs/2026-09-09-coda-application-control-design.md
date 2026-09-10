# Coda Application Control Design

**Status:** Draft for written review; architectural direction approved in chat on 2026-09-09  
**Target:** Windows 11  
**Parent design:** `docs/superpowers/specs/2026-09-09-voice-first-coda-workspace-design.md`  
**Baseline:** `09b12b94fa84c5b4544f62acdac67426f849a35a`

## Purpose

Give Coda typed, truthful control over real Windows applications and their spatial presentation. When an exact saved profile exists, a request such as `open Codex in PythOS on this screen` must resolve that approved profile, start or reuse its terminal application, wait for a real top-level window, connect its capture stream to the requested workspace display surface, focus it, and report the observed result.

This is the first vertical slice of the broader spatial operating-system design. It extends the existing application discovery, launch, reconciliation, capture, input, presentation, Coda, and capability-broker paths instead of introducing a second launcher or a general native shell inside the renderer.

## Current baseline

The live host already:

- discovers Win32 applications registered under Windows App Paths;
- launches an application descriptor through the ordinary Windows process model;
- waits for a visible top-level window;
- creates durable application and window entities;
- reconciles transient PID and HWND changes back to durable identity;
- captures a real window as a Three.js surface;
- routes input and focus back to the real window;
- persists presentation state.

The live Coda integration already receives structured scene state and can emit allowlisted camera and presentation directives. It cannot list, resolve, launch, close, restart, or place applications through those directives. The renderer currently treats each `pc.window` entity as its own display surface, so there is no durable blank screen whose content can be replaced independently of its placement.

## Scope

This slice adds:

1. A broader Windows application catalog covering ordinary executable registrations, Start Menu shortcuts, and packaged applications through typed launch strategies.
2. Durable application launch profiles with a working directory, arguments, launch policy, and preferred spatial placement.
3. Typed host operations for application search, open, close, and restart, plus window focus and placement.
4. A durable `spatial.surface` display slot that can be blank or bound to a `pc.window` content source.
5. Allowlisted Coda application-control directives routed through the native bridge and existing host protocol.
6. Capability checks and explicit confirmation before side effects.
7. Result-driven speech, captions, activity, and bounded audit records.
8. Voice and chat access to the same application-control path.

## Non-goals

The slice does not add:

- arbitrary shell commands or a raw terminal primitive in the renderer;
- repository build, test, package, or deployment commands;
- filesystem browsing or editing;
- grid, architectural, material, prefab, or general scene-authoring tools;
- event/action automation graphs;
- screenshot OCR or Windows UI Automation inspection;
- remote control, elevation, or bypass of Windows integrity boundaries;
- forceful process-tree termination as a normal close operation.

Those remain later vertical slices that consume the capability and entity boundaries established here.

## Product behavior

### Discover and resolve

Coda can search installed applications by display name, known aliases, stable application identity, and saved profile name. Matching is case-insensitive and Unicode-normalized.

An exact stable identity, exact profile name, or unique exact display-name match resolves directly. A unique prefix or token match may resolve when its score is unambiguous. If two plausible matches remain, Coda presents no success claim and asks the user to choose from a short candidate list. Resolution is always a read-only preflight; launch approval is requested only after the preflight identifies the exact application and profile.

Application inventory is read-only and does not require confirmation. Executable paths, package identifiers, arguments, and environment details are not spoken unless the user asks to inspect them.

### Open

The default launch policy is `reuse-or-launch`:

1. Reuse a visible compatible window when one already exists.
2. Otherwise launch the selected application or saved profile.
3. Wait up to five seconds for a compatible top-level window while observing the launched process and its descendants.
4. Reconcile the window to durable application and window entities.
5. Bind the window content to the requested display surface or create a new display surface in front of the camera.
6. Apply the requested or saved presentation.
7. Focus the real window only after binding succeeds.
8. Report whether the window was reused or launched and whether capture is available.

The user can explicitly request `new instance`; Coda never assumes it when a compatible window already exists. An application that refuses a second instance is reported as reused rather than falsely described as newly launched.

### Place on a screen

A durable `spatial.surface` entity owns position, rotation, size, and selection. Its optional `displays` relationship points to one `pc.window` entity. The window owns host identity and capture/input capabilities, while the display surface owns spatial placement.

For `on this screen`, Coda uses the currently selected display surface from the structured scene snapshot. Rebinding a blank surface is immediate after launch approval. Replacing a surface that already displays another live window requires a separate explicit replacement confirmation; it does not close the displaced application. The displaced window remains available as an unplaced window entity and can be placed elsewhere later.

If no display surface is selected, the client creates a new surface centered in front of the camera with a small deterministic cascade offset that avoids exact overlap with existing surfaces.

Existing V0 window entities remain readable. On first load after the schema change, each directly presented `pc.window` receives a corresponding display-surface entity with the same presentation. The migration is idempotent and retains the previous window presentation for rollback compatibility. The new renderer displays a legacy `pc.window` directly only when no `spatial.surface` displays it, preventing duplicate surfaces after migration.

### Focus

`focus` activates the current resolved HWND for the bound window and guides the camera to its display surface. Windows integrity restrictions remain enforced. Camera motion continues to obey the existing `guide-freely`, `ask-first`, or `voice-commands-only` preference, while the Windows focus action uses its own capability decision.

### Close

Normal close sends the ordinary close request to the resolved top-level window and waits for observed disappearance. It never kills a process merely because the window did not close. A timeout or application prompt is reported as `close-pending`; Coda does not claim the application closed.

Because close can discard unsaved work, it always requires fresh confirmation in this slice and cannot be remembered. Force close is not exposed by Coda in this slice.

### Restart

Restart records the current profile and surface binding, requests a normal close, waits for the old window to disappear, opens the same profile, reconciles the replacement window, restores the surface binding, and then focuses it. Restart always requires fresh confirmation because it includes a close. If close remains pending, launch is not attempted. If relaunch fails, the display surface remains present and visibly unavailable rather than disappearing.

## Application descriptors and launch profiles

### Application descriptor

An application descriptor has:

- durable application ID;
- display name and normalized aliases;
- launch kind: `executable`, `shortcut`, or `packaged`;
- typed locator appropriate to the launch kind;
- optional icon reference;
- source and last-observed timestamp;
- current running-window observations.

Raw command strings are not application descriptors. Launching uses structured `ProcessStartInfo` arguments or a Windows shell application identity, never command concatenation.

### Launch profile

A launch profile has:

- durable profile ID and display name;
- application ID;
- argument list;
- optional fully qualified working directory;
- launch policy: `reuse-or-launch` or `new-instance`;
- optional preferred display-surface ID;
- optional preferred presentation used when a new surface is needed.

Profiles contain no credentials or secret environment variables. They are owned by the Workspace Host and stored atomically at `%LOCALAPPDATA%\WorkspaceEnvironment\application-profiles.json` with an explicit schema version so native and fallback clients share one inventory. Saving or editing a profile requires a remembered-capable `application.profile.edit` grant scoped to that profile and workspace.

## Typed operations

The host protocol remains version 1 because these are additive operations. Every mutating result includes an operation ID and observed lifecycle state.

### `application.search`

Input:

- `query`: non-empty human-facing string;
- `limit`: optional integer from 1 through 10.

Result entries expose stable ID, display name, aliases, launch kind, running state, and available profile names. This operation is read-only.

### `application.open`

Input:

- exactly one of resolved `applicationId` or `profileId`;
- `launchPolicy` when overriding the profile;
- optional `targetSurfaceId`;
- optional explicit presentation when no surface exists.

Result:

- `operationId`;
- `applicationEntityId`;
- `windowEntityId` when resolved;
- `surfaceEntityId` when placed;
- `processId` as an ephemeral observation when launched;
- `disposition`: `reused`, `launched`, or `launched-without-window`;
- `surfaceState`: `available`, `unavailable`, or `not-resolved`;
- `focused`: Boolean observed result.

### `application.close`

Input identifies a window entity. Result state is `closed`, `close-pending`, or `not-running`.

### `application.restart`

Input identifies a window entity or profile. Result contains the old and replacement window identities, retained surface identity, and final lifecycle state.

### `surface.bindWindow`

Input identifies one display surface and one window. The host atomically updates their relationships and emits entity events. Binding never closes the previously displayed window.

### Existing operations

`application.list`, `application.launch`, `window.focus`, `surface.open`, and `entity.setPresentation` remain supported for compatibility. The client migrates to the higher-level operations where lifecycle truth matters.

## Coda directive boundary

The native application parses a separate allowlisted workspace directive rather than broadening scene directives into shell access. The directive may carry a human-facing query because the agent does not receive the full installed-app inventory:

```text
[[workspace:{"command":"application.open","args":{"query":"PythOS Codex","targetSurfaceId":"spatial.surface:west"}}]]
```

Allowed commands in this slice are:

- `application.search`;
- `application.open`;
- `application.close`;
- `application.restart`;
- `window.focus`;
- `surface.bindWindow`.

For `application.open`, the native coordinator first submits the query through the read-only search/resolve path. It rejects ambiguity, substitutes the resulting stable application or profile ID, classifies that exact target for approval, and only then sends the mutating host operation. Unknown commands, unknown fields, malformed JSON, oversized payloads, and invalid entity kinds are rejected before execution. The renderer remains unable to submit a raw executable path or shell command.

The native coordinator sends directive requests to the renderer through a typed, correlated message. The spatial client validates placement context, calls the host over the existing loopback WebSocket, updates scene selection from host events, and returns a correlated typed result. Only results matching a pending native request are accepted.

## Truthful conversational sequencing

Coda must not speak a completed-action claim before a mutating directive succeeds.

1. Coda proposes the typed action.
2. The native coordinator classifies its capability and checks remembered grants.
3. If approval is needed, Coda states the exact proposed application, profile, and target surface.
4. After approval, the operation executes.
5. Coda speaks a deterministic result derived from the host response.
6. Explanatory agent text may follow, but cannot replace or contradict the observed result.

For a mutating directive, untrusted assistant prose that asserts completion is withheld until the host result is known. Errors and partial outcomes are spoken as errors or partial outcomes. The activity panel records request, approval, start, result, and error as distinct events.

## Capability policy

Capabilities are distinct from conversation memory and user preferences.

- `application.search`: automatically allowed because it is read-only.
- `application.launch`: may be remembered for one stable application or launch profile.
- `surface.bind`: may be remembered for the current workspace.
- `window.focus`: may be remembered for one stable application within the workspace.
- `application.profile.edit`: may be remembered for one profile within the workspace.
- `application.close`: fresh confirmation every time.
- `application.restart`: fresh confirmation every time.
- replacing a nonblank surface: fresh confirmation for that replacement.

A remembered launch grant does not authorize profile edits, command execution, closing, restart, installation, elevation, filesystem writes, or arbitrary arguments. The approval UI and spoken prompt show the human-facing application name, profile name, selected target surface, and whether a new process may start.

## Persistence and audit

Workspace entities and surface bindings remain in the authoritative host workspace document. Launch profiles and Coda grants remain in their separate native stores.

A bounded application-control audit log records:

- timestamp and operation ID;
- semantic application, window, surface, and profile IDs;
- capability decision source: automatic read-only, allow once, or remembered grant;
- lifecycle transitions and final state;
- redacted error category.

The audit log does not retain captured pixels, raw microphone audio, window text, credentials, or full command lines containing sensitive values.

## Failure handling

- No match: return candidate-free `application-not-found`; do not attempt a path guess.
- Ambiguous match: return a short ranked candidate list and require a choice.
- Launch rejected by Windows: report the Windows error without claiming a process started.
- Process starts without a window: retain the application observation and report `launched-without-window`.
- Window appears but capture fails: place an unavailable surface with retry affordance and report `surface-unavailable`.
- Binding fails: preserve the prior surface binding atomically.
- Focus is denied by Windows: keep the surface and report `focus-denied`.
- Close times out: report `close-pending`; do not kill or restart.
- Restart launch fails: retain the display surface and profile for a later retry.
- Host, renderer, or native bridge disconnects: fail correlated requests, speak no success claim, and reconcile on reconnect.

## Testing

### Host unit and integration tests

- catalog normalization and deterministic ambiguity handling;
- executable, shortcut, and packaged launch-strategy dispatch;
- reuse versus new-instance policy;
- launched-process descendant window resolution;
- five-second window timeout result;
- durable entity creation and idempotent surface migration;
- atomic bind with preservation on failure;
- graceful close outcomes;
- restart retaining one surface identity;
- protocol validation and error mapping.

Tests exercise real domain and dispatcher code with fake Windows catalog, launcher, window catalog, capture, focus, and close boundaries. They assert semantic outcomes rather than calls to mocks.

### Native tests

- workspace-directive allowlist and strict JSON validation;
- mutating directives cannot speak success before completion;
- capability classification and remembered-scope matching;
- close, restart, and replacement always require fresh approval;
- correlated bridge results reject unsolicited or duplicate messages;
- voice and typed chat reach the same operation path.

### Spatial-client tests

- selected display-surface targeting;
- default in-front-of-camera placement;
- blank-surface binding;
- occupied-surface replacement refusal without confirmation;
- replica updates driven by host entity events;
- legacy window presentation migration without coordinate loss;
- host error and disconnect propagation.

### Windows acceptance

1. Search finds Edge, Notepad, Windows Terminal when installed, and at least one packaged application.
2. `Open Notepad here` on a selected blank display surface launches or reuses Notepad, binds its real window, captures it, focuses it, and reports the observed disposition.
3. Repeating the request reuses the existing window by default.
4. `Open a new Notepad` attempts a new instance and accurately reports reuse when Windows coalesces it.
5. Restart preserves the same display-surface position and reconnects input to the replacement window.
6. Close waits for the real window to disappear and never force-kills a resistant application.
7. Denying an approval produces no launch, close, restart, binding, or false success speech.
8. A remembered Notepad launch grant does not authorize another application or an arbitrary executable path.
9. Existing Edge, Notepad, and test-window capture/input/persistence acceptance remains green.
10. The Electron fallback installation and global Codex configuration remain untouched.

## Delivery boundary

The slice is complete only when voice and chat can perform the same typed application operations, the host result governs Coda's claim, display-surface placement persists across app restart, automated suites pass, a versioned native preview activates successfully, and the live Windows acceptance path has been exercised.

The next slice may add a visible command workspace and scoped shell runner using these durable application, profile, surface, approval, result, and audit boundaries. It must not retroactively turn application launch profiles into raw command strings.
