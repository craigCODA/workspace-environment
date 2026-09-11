# ChatGPT Dock / Undock Presentation Design

## Goal

Let the one real Windows ChatGPT window move between two Workspace Environment presentations without creating or launching a second ChatGPT instance:

1. **Docked** — screen-anchored at the right side like a Coda tool panel.
2. **Spatial** — the normal durable world surface, movable and resizable in 3D.

The same semantic `pc.window` and `spatial.surface` remain authoritative in both modes.

## Core invariant

Docking is a renderer presentation override, not a host/window lifecycle operation.

The durable spatial presentation remains stored by the host while docked. Docking never rewrites the world position with camera-local coordinates. Undocking removes the transient override and reveals the latest authoritative world presentation exactly.

The Windows process, HWND observation, capture stream, input sink, durable surface ID, and presentation sink are unchanged.

## Renderer model

### ApplicationSurface

Add an optional transient presentation override to `ApplicationSurface`.

- `setTransientPresentation(presentation)` applies the presentation to the texture target without replacing the durable/authoritative presentation fields.
- `setTransientPresentation(null)` removes the override and reapplies the current displayed durable presentation.
- `acceptAuthoritativePresentation()` and persistent preview/commit operations continue updating durable state while an override is active, but do not visually displace the override.
- `displayedPresentation` continues to mean the durable world presentation. Camera-local dock geometry is deliberately not reported as durable world state.

### WorkspaceScene

Track two renderer-only sets:

- docked surface IDs;
- collapsed surface IDs.

`setSurfaceDocked(entityId, true)`:

- records the desired state even if the entity has not arrived yet;
- reparents the existing surface object from the world scene to the camera;
- applies a camera-local right-panel presentation;
- keeps the same `ApplicationSurface`, stream, and input sink.

`setSurfaceDocked(entityId, false)`:

- reparents the object back to the world scene;
- removes the transient override;
- restores the latest durable presentation.

`setSurfaceCollapsed(entityId, true)` hides that renderer object without closing/minimizing the Windows window or stopping semantic identity. Showing it again restores whichever dock/spatial mode was active before collapse.

Entity replacement/removal must call `removeFromParent()` rather than assuming a surface is a direct child of the world scene, because a docked object is parented to the camera.

### Responsive dock geometry

The dock presentation is computed from camera FOV/aspect and a fixed camera-local depth. It remains inset from the right edge and vertically centered as the renderer resizes.

A pure helper calculates the dock presentation from:

- camera aspect;
- fixed FOV 52°;
- fixed depth;
- margin and maximum panel dimensions.

Resize reapplies that transient presentation to every docked surface.

## ChatGPT startup identity

Extend `openDefaultChatGpt()` successful result to expose the IDs already returned by the existing `application.open` operation:

- `applicationId`;
- `windowEntityId` when available;
- `surfaceEntityId` when available.

No new host fields are invented. If a successful open result lacks a usable `surfaceEntityId`, ChatGPT stays spatial/unmanaged by dock controls for that run.

## Controls

Add a compact ChatGPT control strip near the existing Coda controls after the default startup resolves a surface.

Visible actions:

- **Dock** when spatial;
- **Undock** when docked;
- **Collapse** when visible;
- **Show** when collapsed;
- **Focus** while a window entity ID is known.

Controls operate on the resolved stable surface/window IDs. `Focus` uses the existing typed `application.focus` workspace command. Dock/undock/collapse are renderer-local presentation actions.

The strip does not appear when ChatGPT is unavailable.

## Startup mode

After default ChatGPT startup resolves a usable surface, it starts **docked**. This gives Workspace Environment the compact right-side ChatGPT tool experience by default. Choosing **Undock** places the same real surface back at its durable spatial position.

## Input behavior

A docked ChatGPT surface remains a normal `ApplicationSurface` for hit testing and Windows input routing.

- OS pointer stays visible over the panel.
- Primary clicks route to ChatGPT.
- keyboard/text/wheel routing is unchanged.
- empty-space Pointer Lock never begins from the panel because it is still a surface hit.
- Alt-drag/Alt-arrow durable presentation editing is disabled while a surface is docked; dock geometry is renderer-controlled.

## Persistence

Only spatial/world presentation is persisted by the host.

Docked/collapsed view mode is session-local in this slice. Persisting UI mode can be added later as a user preference without contaminating world presentation coordinates.

## Failure behavior

- If the surface disappears, controls may remain but actions become no-ops until the durable entity returns.
- If ChatGPT is unavailable, no controls appear and Workspace continues normally.
- A failed Focus operation can surface through the existing Coda attention/error presentation but does not alter dock state.

## Tests

Automated tests cover:

1. transient override preserves durable ApplicationSurface presentation;
2. clearing override restores latest authoritative presentation;
3. dock presentation helper stays on the right side and within camera bounds for common aspects;
4. WorkspaceScene dock/undock reparents the same object without stream/input replacement;
5. authoritative upserts received while docked update durable state without moving the docked panel;
6. collapse/show preserves prior dock/spatial mode;
7. successful default ChatGPT startup returns stable surface/window IDs;
8. ChatGPT control state maps actions to scene dock/collapse and typed focus operations;
9. durable Alt-drag/keyboard presentation editing is not entered for a docked surface.

## Deferred

- persisted dock/collapse preference;
- multiple ChatGPT windows;
- arbitrary application docking through a generic UI;
- VR wrist/panel presentation;
- semantic authoring objects that can bind the same dock action.

The implementation should keep the underlying primitives generic enough that the next iteration can expose docking to any semantic surface instead of permanently hard-coding it to ChatGPT.
