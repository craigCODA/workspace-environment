# Baseline and reuse map

Date: 2026-09-11
Reference: `58822736500e98192429297c6ab5cf3c919be14a`
Purpose: preserve useful implementation evidence while giving vNext explicit boundaries.

## What was checked

The connected GitHub reads confirmed PR #2 remains open with `pr/coda-grok-work` at the reference commit and `main` at `230eb6622a5bf59e91db114498810607e13faea7`. The repository branch list and recursive tree were inspected. The README, world-schema, initial native and spatial composition paths, external-action policy, native/fallback workflow, and baseline workflow-run metadata were read.

This is a focused architectural reconnaissance, not a complete line-by-line audit of every source file, dependency, or test. It does not certify security or installed behavior.

The reference workflow run `34588178931` reports `completed/success` for the reference head. The PR records 165 Windows host tests and 125 native Coda tests. Those counts are attributed to the recorded PR verification, not a new locally executed run. The current environment has no usable direct GitHub Git connection and no .NET executable on PATH; native tests were not rerun here.

The user reported that the corrected native build runs, but a live Codex test was blocked by quota. The user also reported that the voice remains unsatisfactory. These are separate acceptance states and should remain separate in every handoff.

## Port decisions

| Existing source or subsystem | Evidence / responsibility | vNext disposition | Gate before retirement of old path |
| --- | --- | --- | --- |
| `packages/world-schema/src/index.ts` | Semantic IDs, properties, relationships, presentation, display-window resolution | Preserve canonical imported IDs and relationship meaning; replace permissive property conventions with versioned package/state contracts | Import fixture preserves identity; rename does not change new entity identity; dimensions are not silently converted to scale |
| `apps/desktop-native/src/Workspace.Desktop/Runtime/DesktopCoordinator.cs` | Constructs host, bridge, voice, capability broker, selected agent; routes startup/recovery | Extract composition and focused orchestration. Avoid adding generated-package execution to this class | Native startup, unavailable-provider recovery, and typed instruction flow remain testable independently |
| `apps/spatial-client/src/app/createWorkspaceApp.ts` | Wires scene, replica, socket, Coda UI, navigation, and ChatGPT presentation | Preserve useful small controllers; move orchestration behind ownership-aware renderer/interaction contracts | Direct drag, camera capture, UI focus, app input, and scene replication acceptance |
| `WorkspaceActionPolicy.cs` | Scoped launch/focus/bind rules and fresh confirmation for close/restart/replacement | Carry policy semantics through the host broker; review enforcement at every new caller | Preview, replay, imported package, and live behavior cannot bypass required confirmation |
| Existing Windows resource host | Reference implementation for real application/window capture and input | Port as platform adapters rather than inventing fake application surfaces | Ordinary first-party test window and installed-app acceptance, including recreated windows |
| Existing surface/docking modules | Separate durable surface placement from transient dock/collapse presentation | Preserve semantic surface identity and compatible placement rules | Dock/undock/rebind/restart does not duplicate windows or overwrite world pose |
| Existing native launcher and storage helpers | Versioned software activation and local persistence patterns | Reuse after tests; keep software activation distinct from package activation | Fresh install, failed pending version, existing user profile, and rollback tests |
| Existing agent interface and adapters | Codex and xAI backends, event handling, startup recovery | Preserve adapters behind Coda; distinguish engine, provider, and model | Accurate capability/status labels; no silent replacement of unavailable engines |
| Existing speech interfaces and Windows engines | Speech input/output and caption/cancellation behavior | Preserve interfaces; keep Windows output as an explicit fallback while evaluating neural output | Audible owner acceptance plus cancellation, resource, and failure tests |
| `.github/workflows/windows-ci.yml` | Distinct native Coda bundle and Electron fallback artifacts | Retain the packaging lesson; create deliberate vNext checks before source work | Install and smoke-test the exact artifact published, with commit/runtime provenance |
| `apps/desktop-shell` | Explicit fallback/development path | Keep isolated during transition; do not present it as the native agent build | Fallback identifies unavailable native capabilities rather than appearing operational |

Items listed as reuse candidates still require file-level review during their port. Existing tests are carried forward where they test behavior; brittle string/label assertions do not count as runtime integration proof.

## Gaps the new design must close

The current world type has a generic properties bag and a presentation record. It does not by itself define package revisions, state migrations, edit leases, per-field conflict handling, or safe hot activation. The native factory currently constructs Codex/xAI paths; OpenCode is not wired into that factory. These observations support introducing new contracts rather than treating a provider dropdown or additional entity kind as the whole feature.

The key new work is a constrained live execution path, validated render-resource exchange, authoritative edit transactions, durable package publication, custom observers, private runtime supervision, and artifact-level acceptance. These must have their own tests before being called proven.

## Repository discipline

The development unit is a vertical behavior proof with an owning module. A new folder is justified by a responsibility and a consumer, not by a theoretical future feature. Dependency checks, protocol fixtures, owned resource handles, and scoped source changes enforce structure more effectively than names alone.

Every port should record its reference path/commit, preserved behavior, intentional changes, tests executed, and rollback route. Preserve the prototype on its existing branch. Use copy/import fixtures for user-state migration. Do not run destructive resets or history rewrites to make the new tree look clean.

Runtime-authored worlds/packages remain in user data. Only reviewed examples and test fixtures live in the product repository. Binaries are staged from pinned manifests. Credentials, personal OpenCode state, live conversation logs, generated drafts, and build output remain outside commits.

## Evidence links

- [Reference PR and recorded verification](https://github.com/craigCODA/workspace-environment/pull/2).
- [Reference workflow result](https://github.com/craigCODA/workspace-environment/actions/runs/34588178931).
- [Reference source tree](https://github.com/craigCODA/workspace-environment/tree/58822736500e98192429297c6ab5cf3c919be14a).
- The [specification's references](../../superpowers/specs/2026-09-11-vnext-live-creative-runtime-design.md#21-baseline-and-technical-references) link directly to inspected source and the external technical documentation.

This record accompanies an architecture-only change. It does not claim that OpenCode, neural voice, the creative runtime, or VR has now been implemented.
