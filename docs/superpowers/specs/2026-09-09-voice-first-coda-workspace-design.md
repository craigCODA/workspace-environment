# Voice-First Coda Workspace Design

**Status:** Approved for implementation on 2026-09-09  
**Target:** Windows 11  
**Baseline:** Workspace Environment V0 at `c1fb488`

## Purpose

Replace the static first-run onboarding overlay with a persistent, voice-first local guide named Coda. Coda helps the user construct and operate the spatial workspace through natural speech, understands and manipulates the Three.js world directly, and can inspect, edit, build, test, save, package, and restart the application through a subscription-authenticated Codex agent.

The feature must not require OpenAI API billing. Codex uses the user's existing ChatGPT subscription login. Wake-word detection, speech recognition, and speech synthesis run locally on the Windows PC.

## Product experience

### First launch

The application opens directly into the navigable Three.js world. The current large left-side onboarding text is not rendered. Coda speaks the existing welcome language while synchronized captions appear in a two-line safe area at the bottom of the view.

The narration can be paused, skipped, repeated, or restarted by voice. After the welcome, Coda asks what it should call the user, stores the preferred name, explains the wake phrase, and offers to begin setting up the workspace.

### Returning launches

The full welcome does not repeat. Coda says `Welcome back, <preferred name>.` and returns to wake-word mode. A proactive status summary is spoken only when the selected alert policy permits it.

The user can say `reset onboarding` or change the preferred name at any time.

### Voice behavior

- Wake-word listening is continuous while the microphone master toggle is enabled.
- Ambient speech is not treated as a command until the configured wake phrase is recognized.
- An active conversation remains open across normal pauses and returns to wake-word mode after a configurable silence period.
- Speech can interrupt Coda. Playback stops immediately and the new utterance steers or interrupts the active agent turn.
- Raw microphone audio is not retained by default.
- Transcript retention is independently togglable.
- Captions are synchronized with speech, remain at the bottom of the view, and are independently togglable.
- A temporary text input is available only as an accessibility and recovery fallback.

### Proactive speech

Proactive speech is configurable independently from the microphone. Presets are:

- `critical-only`: data-loss risk, security or credential problems, failed build/install/restart, blocked decisions, or failure to recover after restart;
- `include-completion`: critical events plus successful completion;
- `quiet`: visual and sound cues only;
- `custom`: individually toggled event classes.

Proactive speech never turns ambient audio into commands. It only controls when Coda may speak without first hearing the wake phrase.

## Architecture

The target architecture has five isolated processes or components:

1. **Workspace Launcher** is a small, stable Windows executable. It selects the active application version, starts Workspace Desktop, observes health, activates a staged version, rolls back failed activation, and restarts the desktop when requested.
2. **Workspace Desktop** is a native .NET/WinUI 3 application. It owns the top-level window, WebView2, microphone and speaker devices, local voice runtime, Codex process, approval UI, caption stream, lifecycle, and health protocol.
3. **Spatial Client** remains TypeScript and Three.js inside WebView2. It owns rendering and interaction but has no direct filesystem, process, shell, credential, or unrestricted native access.
4. **Workspace Host** remains the authoritative C# service for semantic workspace state and deep Windows application/window behavior. Existing capture, input, reconciliation, and persistence contracts remain intact.
5. **Codex App Server** runs as a child process over stdio. It uses the user's ChatGPT subscription authentication and provides threads, turns, streamed events, tool activity, approvals, and local coding work.

The existing Electron shell remains available as a development and rollback fallback until the native shell passes the complete acceptance contract. It is not the long-term authority.

## Local voice runtime

Voice is defined behind replaceable interfaces:

- `IWakeWordEngine`
- `ISpeechRecognizer`
- `ISpeechSynthesizer`
- `IVoiceConversationController`

The first implementation uses the installed Windows speech recognizer for continuous wake-word detection and dictation, plus Windows speech synthesis for output. The recognizer interface is intentionally compatible with a later local Whisper-based adapter when higher technical-dictation accuracy justifies shipping model assets. No audio request is sent to an OpenAI API.

The conversation controller coordinates wake state, active listening, silence timeout, partial and final transcripts, barge-in, caption timing, speech queues, and failure recovery. Device selection and recognition language are persisted user preferences.

## Codex integration

Workspace Desktop launches `codex app-server` over stdio and performs the documented initialize handshake. It starts or resumes a durable thread with the selected workspace source directory as `cwd`.

Codex authentication is never copied into application settings. Workspace Environment detects login state and invokes the normal `codex login` flow when needed. Credentials remain owned by Codex and the Windows credential store.

Agent events are translated into a stable internal model:

- spoken response fragments;
- caption fragments;
- thinking and working state;
- command start, output, and completion;
- file-change summaries;
- approval requests;
- turn completion, interruption, or failure.

The integration consumes generated schemas for the installed Codex version where possible and rejects malformed or unknown privileged messages.

## Capability broker and approval memory

Full access means Coda is technically capable of completing local engineering work; it does not mean every future action is silently authorized.

All privileged operations pass through a native capability broker. Remembered approval records include:

- capability identifier;
- exact workspace or resource scope;
- constraints such as command families or destination;
- approval source and timestamp;
- optional expiration;
- revocation state.

Examples include `workspace.inspect`, `workspace.edit`, `build.run`, `test.run`, `package.stage`, `desktop.restart`, `dependency.install`, `git.commit`, and `scene.control`.

The user can approve once, remember an approval, list remembered approvals, or revoke them by voice. Project-scoped approvals persist across restarts. Credentials, destructive changes outside the approved workspace, new publication destinations, remote publication, and system-wide configuration always require fresh confirmation.

Approval memory is stored separately from semantic workspace state under `%LOCALAPPDATA%\WorkspaceEnvironment`, written atomically, and never committed to a repository.

## Agent understanding of the spatial world

The agent receives a structured scene snapshot rather than inferring the world from pixels alone. It contains:

- camera pose and movement state;
- surface identifiers, kinds, bounds, and presentation state;
- selected and focused entities;
- landmarks and saved viewpoints;
- semantic relationships;
- current viewport and comfort settings;
- recent scene-command outcomes.

The spatial client exposes a narrow command set through validated WebView2 messages:

- `scene.inspect`
- `camera.navigate`
- `camera.focus`
- `camera.stop`
- `camera.return-home`
- `surface.open`
- `surface.move`
- `surface.resize`
- `surface.group`
- `landmark.create`

No scene command can invoke a native shell command directly.

## Navigation

Manual and agent movement use one navigation controller. Voice navigation does not synthesize keyboard input. Coda supplies a target, motion profile, and optional focus entity; the controller computes and animates the camera transition.

Agent navigation has remembered modes:

- `guide-freely`
- `ask-first`
- `voice-commands-only`

Mouse movement, the Escape key, or the spoken word `stop` immediately cancels agent motion. Smooth glide and teleport are both supported. Speed, acceleration, rotation, and comfort fade are user preferences.

## Visual presence

Coda is represented by a small spatial presence rather than a conventional chat panel. It communicates these states through restrained animation and color:

- dormant;
- listening;
- thinking;
- working;
- restarting;
- needs attention.

An optional terminal surface materializes while engineering work is active. It renders structured command and build events and can be minimized or hidden. It is an observer surface, not a raw untrusted terminal wired to the renderer.

Captions occupy a bottom safe area, use no more than two lines, remain readable across changing scene content, and expose appropriate accessibility semantics.

## Build, activation, and restart

Coda never overwrites a running executable in place.

1. Codex edits the source workspace.
2. The capability broker authorizes the requested build and test operations.
3. Build output is placed in a new versioned staging directory.
4. Required tests and package integrity checks run against the staged version.
5. Workspace Launcher records the current known-good version and atomically updates the pending-version pointer.
6. Workspace Desktop checkpoints the agent thread, voice preferences, approval memory, scene state, and pending spoken response.
7. Workspace Launcher closes the desktop, activates the staged version, and starts it.
8. The new desktop reports healthy only after the native window, WebView2 world, Workspace Host, voice runtime, and Codex connection reach their required states.
9. On success, the launcher commits the active-version pointer and Coda resumes the conversation.
10. On timeout or fatal failure, the launcher restores the known-good version, relaunches it, and explains the rollback.

Renderer-only changes use a faster path: rebuild the spatial client, validate its manifest, swap the static asset version, and reload WebView2 without terminating the native voice or Codex processes.

## Persistence

The following durable records are separate and inspectable:

- semantic world and presentation state;
- user voice profile and preferred name;
- onboarding completion;
- voice/caption/proactive/navigation toggles;
- approval memory;
- Codex thread identifier and workspace binding;
- launcher active, pending, and known-good version pointers;
- bounded operational logs.

Every file-backed store uses atomic replacement and explicit schema versions. Invalid records are quarantined and replaced with safe defaults rather than preventing startup.

## Security boundaries

- WebView2 loads only application-controlled local content and uses an explicit origin policy.
- Native messages are typed, versioned, size-bounded, and allowlisted.
- The renderer never receives Codex credentials or a general shell primitive.
- Codex App Server communicates over child-process stdio, not an unauthenticated network listener.
- Voice activation cannot bypass the capability broker.
- Surface content, captured windows, repository files, terminal output, and web content are treated as potentially hostile prompt-injection sources.
- Remote pushes, deployments, destructive filesystem operations, credential access, and system-wide changes require fresh user confirmation.
- Logs redact secrets and apply bounded retention.

## Failure handling

- Missing Codex: Coda explains how to install or locate the CLI; the spatial workspace remains usable.
- Signed out Codex: launch the standard ChatGPT subscription login and resume afterward.
- Voice model missing: keep captions and temporary text recovery available while offering local model installation.
- Microphone unavailable: show the device problem and preserve non-voice navigation.
- Codex crash: restart App Server and resume the durable thread once; surface repeated failure.
- Workspace Host crash: preserve voice and Codex, restart the host once, and reconcile the world.
- Renderer crash: recreate WebView2 without ending the voice conversation.
- Build failure: retain the running known-good version and make logs available in the terminal surface.
- Activation failure: roll back automatically.
- Corrupt preferences or approval memory: quarantine the record, use safe defaults, and report the reset.

## Testing

Unit tests cover voice state transitions, caption segmentation, onboarding persistence, proactive-event filtering, approval matching and revocation, scene-command validation, navigation interruption, Codex message translation, launcher version selection, and rollback decisions.

Integration tests use fake audio, fake Codex, fake WebView2 messages, and temporary version directories. They verify first-run narration, returning greeting, barge-in, remembered approvals, terminal event streaming, renderer reload, desktop restart, health timeout, and rollback.

Windows acceptance verifies:

1. A clean first launch speaks the approved welcome with bottom captions and no large text overlay.
2. A preferred name is collected and a returning launch says `Welcome back, <name>.`
3. Wake-word mode, microphone, captions, proactive speech, and agent-navigation modes are togglable and persist.
4. Ambient speech before the wake phrase does not execute commands.
5. Voice can ask Codex to inspect and modify a fixture project through ChatGPT subscription authentication.
6. Coda can build, test, stage, restart, resume the same thread, and report health.
7. A deliberately broken staged version rolls back to the known-good version.
8. Coda can navigate the camera and arrange a surface without keyboard input, and manual interruption stops motion immediately.
9. Existing application capture, input routing, presentation persistence, and restart/rebind acceptance remain green.
10. No OpenAI API key is requested, stored, or used.

## Delivery strategy

Implementation proceeds in vertical slices while preserving the existing Electron package as a fallback:

1. captioned voice-first onboarding and durable user preferences;
2. native shell and typed WebView2 bridge;
3. local voice runtime;
4. Codex App Server client and approval broker;
5. structured scene control and agent navigation;
6. launcher, staged activation, health checks, and rollback;
7. Windows packaging and full acceptance.

The first releasable milestone must provide an end-to-end spoken interaction that reaches a subscription-authenticated Codex thread and can perform a remembered, project-scoped build followed by a verified renderer restart.
