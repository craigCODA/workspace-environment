# Default ChatGPT Surface Design

**Status:** Architectural direction approved in chat; implementation slice scoped for startup behavior only.

## Goal

When Workspace Environment starts, make the signed-in Windows ChatGPT application the default real application surface when it is installed, while keeping Workspace Environment fully usable when ChatGPT is absent or cannot open.

This slice uses the existing typed application-control path. It does not add a ChatGPT-specific launcher to the Windows host and it does not embed consumer ChatGPT as an inference backend.

## Startup behavior

After the spatial client has connected to the Workspace Host and posted `renderer.ready`:

1. Search the host application catalog for the exact human-facing query `ChatGPT`.
2. Continue only when search resolves to exactly one application descriptor.
3. Open the resolved stable application ID with launch policy `reuseOrLaunch`.
4. If a visible compatible ChatGPT window already exists, the host reuses it.
5. Otherwise the host launches the installed Windows ChatGPT application and waits for a compatible real top-level window.
6. Open with no requested target surface. The existing host application-control path reuses any durable surface already displaying that window; otherwise it creates the deterministic surface for the resolved window using the spatial client's normal camera-relative default presentation.
7. Host entity events drive the real surface into the scene through the existing replica/synchronizer path.

The startup action runs after `renderer.ready` and does not block Workspace initialization.

## Failure behavior

Default ChatGPT startup is optional product behavior, not a workspace health requirement.

- ChatGPT not found: no side effect, no error state.
- Search ambiguous: no launch and no guessed application.
- Launch rejected, no window appears, or open fails: Workspace remains active.
- Host disconnect: the existing connection failure path remains authoritative.

The startup helper catches application-specific failures and returns a typed status instead of throwing into the main initialization chain.

## Authority and identity

The spatial client supplies only the resolved stable application ID and `reuseOrLaunch`. It never supplies an executable path, shell command, package locator, or raw arguments.

The host remains authoritative for:

- installed application identity;
- reuse versus launch;
- process/window observation;
- durable application/window/surface entities;
- surface binding;
- focus result;
- persistence.

No new capability is exposed to Coda or external agents by this startup behavior.

## Component boundary

Add `DefaultApplicationStartup.ts` in the spatial client.

It consumes a minimal workspace-command interface compatible with `WorkspaceCommandController.handle()` and produces a typed result:

- `opened` when a resolved ChatGPT application reaches the existing open operation successfully;
- `unavailable` when search is not uniquely resolved;
- `failed` when the search/open command path returns an error or throws.

The helper owns result-shape validation for the `application.search` response. `createWorkspaceApp.ts` owns only when to invoke it.

## Testing

Spatial-client unit tests verify:

1. resolved search sends one `application.open` using the stable application ID and `reuseOrLaunch`;
2. not-found and ambiguous search results perform no open;
3. malformed results perform no open;
4. command failure/exception becomes `failed` and never rejects startup;
5. no executable path or raw launch data is introduced.

Existing application-control host tests already verify reuse, launch, deterministic existing-surface reuse, new-surface creation, and truthful lifecycle results. Those tests remain the source of truth for Windows behavior.

## Deferred

This slice does not implement:

- docking ChatGPT into a Coda-style right panel;
- dock/undock presentation modes;
- second ChatGPT instances;
- browser ChatGPT embedding;
- ChatGPT subscription authentication as an internal agent provider;
- semantic world authoring.

Dock/undock is the next presentation slice and continues to use the same one real ChatGPT window/surface identity established here.
