# Workspace Interaction Baseline Design

## Goal

Make the current Windows-first spatial workspace behave like a deliberate desktop/3D environment before adding the larger semantic authoring system.

This slice covers four user-facing behaviors:

1. Empty-workspace click enters true Pointer Lock camera control.
2. The native pointer remains visible during normal UI and application-surface interaction.
3. Coda starts compact with chat collapsed.
4. Agent provider selection is an explicit visible selector rather than a cycling button.

The default ChatGPT application surface and semantic authoring/prefab/behavior system are intentionally separate follow-on slices. Keeping them separate makes this baseline independently testable and avoids coupling Windows application startup to input behavior.

## Pointer Lock Navigation

### Entry

A primary-button click on empty workspace requests Pointer Lock on the workspace root.

Pointer Lock must not be requested when the click targets:

- a Coda interactive control or panel,
- a bound or unbound application surface,
- an Alt-drag presentation move/resize operation.

### While locked

`PointerEvent.movementX` and `PointerEvent.movementY` drive `SceneCommandController.manualLook()`.

The workspace root carries `is-looking` only while the document reports the root as the active `pointerLockElement`.

The native cursor is hidden only while Pointer Lock is active.

### Exit

The browser's normal Escape behavior releases Pointer Lock. The workspace listens to `pointerlockchange`, removes `is-looking`, and returns to normal pointer interaction.

Destroying the workspace while it owns Pointer Lock releases the lock and removes the listener.

### Surface interaction

Application-surface hover continues to update the in-world surface cursor and route pointer movement to the Windows surface, but it no longer hides the native cursor.

Application-surface pointer capture, wheel routing, Alt-drag move/resize, keyboard routing, and persisted presentation behavior remain unchanged.

## Coda Compact Startup

Coda chat starts collapsed. Voice state, captions, transcript preference, activity, and the beacon remain available.

The `Chat` control explicitly opens and closes the panel. Opening chat still closes activity; opening activity still closes chat.

## Agent Provider Selector

Replace the provider cycling button with a labeled `<select>` in the Coda controls.

Options:

- `Codex`
- `SpaceXAI`
- `Cursor (soon)` with value `Cursor`

The current stored preference selects the matching option. A user selection sends the same `agentProvider` preference change already understood by the native coordinator.

No provider runtime semantics change in this slice: Cursor remains the existing compatibility/fallback path until its implementation lands.

## Accessibility and Input Boundaries

- The selector has an accessible label.
- Pointer Lock is only entered by an explicit primary click on empty workspace.
- Coda controls remain ordinary pointer/keyboard UI and never trigger camera capture.
- Reduced-motion behavior is unchanged.

## Acceptance

- Hovering a live application surface leaves the OS cursor visible.
- Clicking empty workspace hides the cursor and enables relative mouse-look without holding a button.
- Escape restores the pointer and normal UI/surface interaction.
- The movement hint describes click-to-look and Escape-to-release behavior.
- Coda chat is hidden on startup.
- The provider control visibly shows the active provider and changes the existing preference when selected.
