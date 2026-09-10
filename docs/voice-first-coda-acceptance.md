# Voice-first Coda native-preview acceptance

## Acceptance statement

The native preview replaces the static onboarding panel with a local voice-first Coda presence. First launch speaks the original welcome in order with bottom captions and asks for a preferred name. Returning launches greet that saved name. After onboarding, local Windows speech remains dormant except for the exact `Hey Coda` wake phrase; important proactive speech follows the selected alert policy.

Codex runs through `codex app-server --stdio` using the existing ChatGPT subscription login. No OpenAI API key is requested, inspected, persisted, or passed to the child process. The Three.js renderer receives typed messages but has no filesystem, process, credential, or general-shell access.

## Automated verification

Run from the repository root:

```powershell
npm install
npm test
npm run typecheck
npm run build
dotnet test apps/host-windows/Workspace.Host.sln --configuration Release
npm run native:test
npm run native:build
npm run native:stage
```

The suites cover local conversation state, wake/listen/speak transitions, first-run and returning greetings, profile persistence, exact scoped capability grants, forced fresh-confirmation classes, Codex protocol translation/redaction, renderer-message validation, camera cancellation/interpolation, structured scene inspection without pixels, scene directive validation, proactive speech policy, manifest hashes, staged activation, and rollback selection.

## Windows 11 observations — 2026-09-09

Observed in an unelevated Windows 11 interactive session:

1. Windows reported a compatible `en-US` local speech recognizer and five installed local synthesis voices.
2. `codex login status` used the existing ChatGPT login. A live read-only App Server turn returned the exact marker `WORKSPACE_CODEX_OK` without an API key.
3. The WinUI 3/WebView2 window opened as **Workspace Environment**, started its owned `Workspace.Host.exe`, connected the renderer, and started `codex app-server --stdio`.
4. The first visible workspace frame contained the Three.js world, no large or left-side onboarding text, a small Coda presence, and the synchronized bottom caption for `What should I call you?`.
5. A self-contained staged desktop executable launched from its version directory and completed the named-pipe health handshake. Activation then recorded it as active and known-good.
6. A deliberately incomplete pending version failed manifest validation. The stable launcher cleared it and launched the unchanged known-good version.
7. The existing Electron executable under `%LOCALAPPDATA%\Programs\workspace-environment` remained present throughout native staging and rollback acceptance.

Acoustic speaker output and physical microphone recognition were not independently judged by automation. The local engine invocation, installed devices, state transitions, and synchronized captions were observed; a human should confirm microphone distance/noise behavior and preferred voice quality.

## Spoken controls and boundaries

- Local controls include the optional click-to-talk button, stop, pause, repeat, show/hide activity, list/forget permissions, reset onboarding, preferred-name changes, microphone/caption/transcript toggles, alert modes, navigation modes, return home, stop camera, and focus by scene name.
- User speech overrides in-progress Coda speech. Manual mouse, keyboard, wheel, or Escape input cancels agent camera motion immediately.
- Agent scene requests are restricted to typed camera and surface commands. The agent receives a bounded structured scene snapshot and never captured pixels.
- `remember this` stores only the classified capability and exact project scope. Credential access, remote publication, system configuration, and destructive access outside the workspace cannot be persisted.
- Terminal output is visible in Coda activity but is not narrated verbatim. Final responses are spoken sentence by sentence.

## Packaging and rollback

`scripts/prepare-native-desktop.ps1` builds the renderer, publishes the self-contained native desktop and host into a new timestamp-plus-commit directory, hashes every declared file, and writes `version-manifest.json`. It does not alter activation until staging and launcher publication succeed.

`scripts/install-native-preview.ps1` installs side-by-side shortcuts named **Workspace Environment Native Preview**. The existing Electron installer and shortcuts are not deleted or overwritten. The preview is unsigned and intended for this local Windows 11 environment.
