# Workspace Environment vNext

Status: architecture review. Application implementation has not started.

Start with the [Live Creative Runtime specification](../../superpowers/specs/2026-09-11-vnext-live-creative-runtime-design.md). It is the current proposed design for runtime-authored spatial objects, direct manipulation during generation, persistent packages, live behavior, isolated OpenCode, and the transition from the prototype.

The [baseline and reuse map](baseline-and-reuse.md) distinguishes inspected source, recorded verification, proposed ports, and work that still requires proof.

## Repository checkpoint

- Reference branch: `pr/coda-grok-work`.
- Reference commit: `58822736500e98192429297c6ab5cf3c919be14a`.
- Existing main: `230eb6622a5bf59e91db114498810607e13faea7`.
- Isolated design branch: `vnext/architecture-foundation`.

The design changes are documentation-only. Original source, workflows, installers, and user data remain unchanged. This branch inherits the reference code as evidence; it is not yet a new blank application shell.

Direct Git cloning was unavailable in the authoring environment. Branch and document operations use the connected GitHub interface. No local checkout or worktree on Craig's PC has been created by this work.

## First implementation gate

The owner reviews the written specification, especially sections 9 through 13: concurrent edits, constraints, execution isolation, hot replacement, and live behavior. After approval, the next artifact is the M1 implementation plan with executable failure tests.

M1 proves a runtime-authored object in arbitrary 3D space, authoritative user placement during a rebuild, save/reload, and recovery from bad code. It is intentionally testable without a live model account. The full product is built in subsequent vertical slices.

## Working rules

Keep platform code, bundled runtimes, generated packages, and world data separate. Port useful code with its tests and source provenance. Keep the renderer and agents subordinate to host-owned state and authority. Treat promises of broad Three.js compatibility, strong isolation, voice quality, and VR support as acceptance obligations rather than completed features.

Design review is not an implementation-complete claim. Baseline CI evidence is not a new vNext test run.
