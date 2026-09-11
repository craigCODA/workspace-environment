# Workspace Environment vNext: Live Creative Runtime

Date: 2026-09-11
Status: Written specification for owner review. Direction approved; implementation has not started.
Product owner: Craig Ramos
Repository: craigCODA/workspace-environment
Design branch: vnext/architecture-foundation
Reference commit: 58822736500e98192429297c6ab5cf3c919be14a

## 1. The product contract

Workspace Environment lets its user create, reshape, place, save, and operate a spatial desktop while that desktop remains open. Coda helps author new content and behavior. The user can also manipulate it directly. Real Windows applications remain real Windows applications.

The defining requirement is extensibility during use. A request for an unfamiliar visual, interaction, or observable condition creates or revises a world package. It does not normally create another feature in the Workspace application source tree.

Examples are acceptance scenarios, not the list of supported object types: a labeled grid suspended above a desk; an arbitrary colored line between two points in the air; fire falling from the sky; a board stretched from one end with the other pinned; a house assembled from saved bricks; a fireplace interaction that requests a Terminal launch.

The platform supplies stable identity, persistence, execution, rendering, interaction, and authorized access to the computer. Packages supply the invented things. The set of package implementations can grow while the application runs.

### The operational promise

Ordinary world authoring and package replacement keep the application session open. A rejected package leaves the prior working version available. Moving an object while it is being rebuilt preserves the user's current placement. The same creation remains identifiable and editable after saving and reopening the world.

This promise covers capabilities exposed by the installed runtime and the machine. New drivers, unsupported rendering backends, security fixes, and native platform changes can require a normal software update. No design can guarantee every conceivable future request, every third-party Three.js addon, unlimited GPU resources, or flawless generated code. Broad creative expression is a product requirement to demonstrate progressively, not an excuse to bypass these boundaries.

## 2. What exists and what is new

This document proposes vNext. It is not a report that the creative runtime already exists.

At the reference commit, the repository contains a native Windows desktop, an Electron fallback, a Windows resource host, a Three.js spatial client, shared protocol/world types, agent adapters, speech interfaces, and versioned native packaging. The current native coordinator constructs the host, bridge, voice controller, capability broker, and selected agent. The spatial composition function connects scene, replica, application surfaces, navigation, Coda UI, and presentation controls. These are useful port candidates; their presence does not prove the new architecture. [R1-R5]

The baseline Windows CI run is successful for the reference head. Live Codex-turn acceptance was blocked by the user's quota in this conversation. Voice quality was explicitly rejected by the user. OpenCode bundling, a neural voice replacement, arbitrary live packages, generative constraints, and the new persistence/transaction design are new work. [R6]

Commit count and PR size are not architectural evidence by themselves. The specific reason for this work is to separate world authoring from application-source maintenance and to introduce enforceable ownership and lifecycle boundaries before runtime-generated code is enabled.

## 3. Migration decision

Use a normal branch of the existing repository. Preserve the reference commit and leave main and pr/coda-grok-work unchanged during design work. Keep the current installed application available as a reference and fallback.

A worktree is a separate checkout of a branch, not a new security boundary and not automatically a blank codebase. The current execution environment could not clone GitHub directly because DNS resolution failed. The connected GitHub tools can still create the isolated branch and commit this specification. No worktree on the user's PC is implied.

The implementation begins with a clean composition and explicit contracts on the vNext line. Port tested components into those contracts rather than rewriting every Windows integration. Avoid a simultaneous whole-repository rename. Existing modules remain reference code until their replacements pass equivalent acceptance paths. Each port records its source commit and tests. Remove a superseded module on the vNext branch only after its replacement and rollback path are verified.

Three approaches were considered:

| Approach | Benefit | Cost | Decision |
| --- | --- | --- | --- |
| Continue enlarging the current coordinators | Fastest individual edit | New runtime boundaries become entangled with old startup and UI ownership | Use only for urgent prototype fixes |
| Unrelated repository and full rewrite | Clean-looking tree | Loses traceability and repeats working integrations | Reject |
| Isolated vNext branch with contract-led ports | Preserves evidence and history while rebuilding composition | Requires temporary compatibility adapters | Adopt |

The design commit changes documentation only. It neither installs dependencies nor moves application files.

## 4. Ownership boundaries

Six logical modules define the initial architecture. They do not require six services or twenty empty packages on day one.

| Module | Owns | Must not own |
| --- | --- | --- |
| World Core | Durable entity state, revisions, transactions, package references, constraints, authoritative edit sessions | Three.js objects, voice synthesis, provider SDKs |
| Creative Runtime | Package validation, execution lifecycle, staging, hot replacement, resource accounting, observer scheduling | Unmediated Windows access or provider credentials |
| Scene Renderer | Three.js resources, projection of accepted state, transient visual state, picking output | Durable world truth or approval decisions |
| Interaction | Pointer/controller intent, selection, anchors, drag previews, transform/constraint requests | Direct persistence writes or bypasses around world commands |
| Coda | Conversation state, relevant context, authoring requests, agent/voice selection, progress | Root authority over the world or the application source |
| Platform Adapters | Windows discovery, capture, input, approved external actions, storage and process supervision | Invented model conclusions about what happened on Windows |

A narrow native composition layer wires these modules together. Concrete agent clients and speech engines are adapters behind Coda's interfaces. The launcher handles software-version activation separately from world-package activation.

The authority chain is explicit: Windows supplies facts about Windows resources. The host's World Core owns the durable representation and accepted spatial state. The renderer is a projection. Agents and packages submit proposals. A failed renderer cannot rewrite the world to justify what it happened to draw.

## 5. Repository structure and dependency direction

Use a small number of build units and focused modules. The target organization is:

```text
apps/
  desktop/                  native UI composition and trusted recovery controls
  host/                     authoritative host composition
  spatial/                  Three.js client composition
  launcher/                 software activation and rollback
src/
  Workspace.Core/           world, commands, revisions, constraints, policy ports
  Workspace.Runtime/        package lifecycle, Coda orchestration, service supervision
  Workspace.Windows/        Windows adapters and native integrations
  Workspace.Storage/        durable transaction and blob-store implementation
packages/
  contracts/                generated/client protocol types and validation
  creative-sdk/             versioned API used by world packages
  spatial-runtime/          renderer and interaction modules
contracts/
  schemas/                  language-neutral wire/package schemas
  fixtures/                 matching C# and TypeScript acceptance fixtures
examples/
  world-packages/           reviewed examples and regression fixtures
runtimes/
  manifests/                pinned dependency versions, digests, notices
scripts/                    build, staging, verification, packaging
tests/                     acceptance, security, and compatibility checks
  acceptance/
  security/
  compatibility/
docs/
  architecture/vnext/
  superpowers/specs/
  superpowers/plans/
```

The diagram describes ownership, not an instruction to create empty folders. Keep current package/project names until a deliberate port changes their build consumers. The authoritative contracts directory has one source for wire shapes; derived language types and shared fixtures prevent parallel C# and TypeScript inventions. Semantic rules remain host-owned even when the client performs early validation.

Dependency direction is enforced in CI. Core references contracts and domain utilities. Runtime references Core and adapter interfaces. Windows and Storage implement those ports. Composition roots construct concrete implementations. The renderer and interaction code consume the client contracts. Generated packages import only their declared SDK/dependency set, never apps/, internal runtime modules, native bridges, or credential storage.

A module needs a public entry point, an owner, and tests before becoming a separate package. New helpers stay near their consumer until actual reuse justifies extraction. Avoid a universal services object, an untyped global event bus, and a catch-all shared folder.

## 6. Four storage classes

Application source, bundled tools, authored packages, and world state have different lifecycles.

**Shipped application code** changes through reviewed commits, tests, and versioned releases. Runtime requests for visual content do not edit it.

**Bundled dependencies** are fetched/staged by the release build from pinned manifests with integrity checks and required notices. OpenCode binaries and model/voice assets do not become enormous ordinary Git blobs. Updates are deliberate and verified.

**World packages** contain source, manifests, locked dependencies, owned assets, state schema, and declared permissions. They live in the user's data location, not the product repository. A package's published revision is immutable and addressed by a content digest. Drafts are mutable and separate.

**World state** contains entity identities, package-revision references, parameters, placement, relationships, instance overrides, constraints, saved behavior state, and history. It does not serialize executable closures, live GPU handles, process IDs as identities, or authorization tokens.

The initial side-by-side profile location is a vNext-specific subtree under the Workspace data root. An importer reads a copy of the prototype world. It preserves existing canonical IDs and writes a migration report. It does not mutate the original workspace.json, share the prototype's mutable database, or silently replace the installed application's profile.

## 7. Durable identity, coordinates, and state

A package definition, a published package revision, and an object instance are distinct. An instance keeps its ID when its package changes. Reusing a brick creates new instance IDs referencing the same revision. Saving a wall records an assembly with child identities and attachment rules, not a screenshot of the result.

New spatial entities receive persistent opaque IDs. Renaming changes the display name, not the ID. Existing pc.application:, pc.window:, and spatial.surface identities are preserved by import; aliases are explicit rather than reconstructed from a new naming scheme. [R2]

Each instance has an authoritative root transform, authored parameters, implementation revision, logical children, and optional host binding. Meaningful subparts have stable keys, such as board.end.east, rather than relying on a triangle index that changes when geometry regenerates.

Coordinates use metres internally, a right-handed frame with positive Y upward, radians for angular values, and normalized quaternions for orientation. The initial world compass maps east to positive X and north to negative Z; the compass is independent of camera yaw. Feet/inches are UI conversions. Presentation dimensions and dimensionless scale are distinct; the prototype's size field must not be blindly reinterpreted as a new scale field.

World, parent-local, and geometry-local coordinates are explicit in commands. A transform supplied in a parent frame includes the parent identity and relevant revision. Cyclic parenting, non-finite values, invalid quaternions, and non-invertible parent transforms are rejected. Unsupported shear/non-uniform-scale combinations produce a recoverable editing error rather than corrupted geometry.

Simulation variables and visual interpolation can remain transient. Parameters needed to reconstruct an authored creation are durable. A package declares what state is checkpointed and its migration function; saving does not pretend to preserve arbitrary in-flight JavaScript stacks or every particle trajectory.

## 8. Placement and direct manipulation

A placement ball is a visible representation of a Spatial Anchor with a durable ID and pose. Its visible radius is a handle size, not automatically the intended size of the generated object. Creating a fireplace at that anchor preserves the selected pose and uses explicit object dimensions or a user-sized placement volume.

The user can grab a creation while it is building. The root pose remains controlled by the world/interaction transaction. Package geometry is generated in the instance's local frame. Replacing geometry never republishes a stale root pose as a side effect.

Desktop editing provides axis handles, free drag, depth adjustment, local/world frames, numeric entry, and optional snapping through a consistent manipulation service. Camera capture remains a separate input mode: primary click on empty space acquires camera control; Escape releases it. UI, active transform handles, application surfaces, and an ongoing drag do not accidentally capture the camera.

Build/Edit and Use modes separate editing a switch from operating it. Selection and hover are transient session state. Edit sessions are explicit host-issued leases scoped to the entity and affected fields; they expire or cancel on disconnect. Local motion previews remain responsive while host updates arrive. A single drag is one undoable user operation, with bounded checkpoints rather than a permanent log entry for every pixel.

VR controllers and hands later produce the same begin/update/end interaction intents. The interface is device-neutral now; hardware tracking, locomotion comfort, headset rendering, and remote Windows capture still require their own VR acceptance work.

## 9. Concurrent user and agent edits

The host serializes accepted mutations and maintains separate revisions for placement, authored parameters, constraints, and package implementation. Commands carry a request ID, relevant expected revisions, the proposed changes, and an authenticated actor context supplied by the host channel.

A geometry/color proposal must not fail merely because the user moved the object, unless the proposal actually depends on its world position. A root-placement change generated before a user drag is stale and cannot overwrite that drag. A proposal that depends on a pinned edge revalidates the edge/constraint revision before publication.

Example: Coda begins package revision 4 at transform revision 12. The user drags the instance to transform revision 18. The new mesh publishes under the existing instance root at revision 18. The old position is never copied from generated source into the accepted state.

Geometry changes affecting a currently grabbed subpart wait for a safe interaction boundary unless stable-handle remapping is validated. A removed/redefined handle requires explicit re-selection; it never jumps the hand or mouse to a different vertex silently. Conflicting constraints remain visible and leave the last valid shape in place.

Rollback reverts the failed implementation and its compatible authored state. It does not revert independent user moves or other entities edited since the upgrade began.

## 10. Constraints and the pinned board

A rigid transform, anchored resize, and free deformation are different operations. The interaction contract declares which one is active.

For an anchored board resize, resolve the east end once to a stable end handle and world anchor. Constrain the opposite handle to the board's authored length axis. Its projected distance from the anchor determines the new length; the midpoint determines the board centre. Thickness and height remain unchanged unless included in the edit. A minimum positive length prevents inversion. The pinned end's world position is checked numerically after each accepted edit.

Locking a point does not automatically lock a whole face or orientation. A face lock is a different declared constraint. World-axis words and local-axis operations retain their reference frame after the board rotates. The solver never guesses a new 'east end' during an active drag.

Packages can supply new parameter mappings, local deformation functions, and constraints through runtime APIs. First implementation uses a small deterministic anchored-resize solver. Arbitrary mesh deformation, conflicting multiple pins, and physical simulation require additional solver implementations and performance tests. The extension contract permits those implementations; a board demo is not evidence of a universal CAD solver.

## 11. Creative execution and rendering

### Chosen safety boundary

The default execution path runs package JavaScript in an isolated guest engine, provisionally a QuickJS WebAssembly build inside a disposable worker. The trusted wrapper supplies a constrained module loader and only explicit SDK imports. There are no guest bindings for the filesystem, environment variables, process execution, network, DOM, provider tokens, or the native bridge. QuickJS exposes memory/stack limits and an interrupt callback; the exact WebAssembly wrapper and limits must be proven in the first engineering spike before being pinned. [T1]

The worker adds responsiveness and termination control; the guest engine and its imports provide the execution boundary. A plain Worker or node:vm is not accepted as the security argument. Node's documentation explicitly rules out treating node:vm as a security mechanism. [T2]

The renderer receives validated, ownership-scoped resource descriptions and updates, not arbitrary executable objects or callbacks from the package. Authoritative entity roots and approval UI never become guest objects. Guest output cannot name another package's resources merely by guessing its ID. Package-local handles are mapped by the trusted runtime.

### Creative breadth

The SDK must support arbitrary vertex/index/attribute data, lines and curves, instancing, shader source and uniforms, textures from authorized assets, lights, groups, logical handles, animation data, and procedural updates. Build on Three.js and its reviewed addons instead of inventing a second graphics engine. Generate mesh buffers and shader logic when no existing high-level object matches an idea. Neither a FloorGrid enum nor a Fire feature switch belongs in the core. [T3]

This is broad rendering expressiveness, not a claim that every Three.js API can run unchanged inside a restricted guest. DOM-dependent loaders, external asset fetching, renderer-wide post-processing, arbitrary onBeforeRender callbacks, and backend-specific APIs require compatible adapters. A capability manifest makes support discoverable before generation.

Renderer-wide custom modules are a separate advanced execution profile. They require isolated rendering/compositing and explicit threat/performance acceptance before enabling them for user-authored content. They must not be smuggled into the trusted main renderer as an escape hatch. Depth, picking, shadows, app-surface composition, and VR integration are proof obligations for that profile. Until those tests pass, its status is unsupported, not silently approximated as complete Three.js compatibility.

### Resource and failure control

The host owns CPU/execution, allocation, output-message, subscription, asset, geometry, texture, and draw-work budgets. Limits are visible in diagnostics. Repeated identical geometry shares resources where possible; individual bricks are not individual processes or independent render loops. Typed buffers are transferred/batched rather than JSON-serializing vertices every frame. The renderer owns its render loop; behavior scheduling is bounded and independent of model latency.

Shader execution can exhaust a GPU despite JavaScript isolation. Test compilation failures, runaway resource creation, context loss, and repeated reloads. Recovery preserves committed world state and a trusted stop/disable control. It may restart the creative renderer and interrupt the visual view; a whole-machine GPU failure cannot be guaranteed invisible. Resource lifecycle cleanup covers geometry, materials, textures, render targets, subscriptions, timers, audio, and temporary asset handles.

## 12. Package lifecycle and hot replacement

A package includes a manifest, editable source, locked dependency versions, assets with provenance, parameter/state schema, stable handle declarations, and requested capabilities. SDK, package-state, and renderer-profile compatibility are explicit. Hashes detect content changes; hashes alone do not establish trust.

The publication pipeline is:

```text
request -> isolated draft -> deterministic compile -> validate
        -> candidate preparation -> preview/health checks
        -> revalidate current world revisions -> commit activation
        -> retire previous runtime resources
```

Compilation uses a pinned toolchain and approved resolver. It does not run package installation hooks, arbitrary npm scripts, or untrusted build plugins. Generated code writes into a draft workspace, not the application source or active package store.

Candidate preparation has no external side effects. It cannot launch an application, submit a network action, or commit world edits while being evaluated. The old revision remains active until the candidate is ready. The host then commits the chosen package revision and compatible authored state together. Render-resource staging is coordinated with that commit; durable transaction atomicity is distinct from GPU frame timing.

Every execution instance has a generation token. Replies, timers, and messages from a retired generation are rejected. Preparation, state migration, and cleanup have deadlines. Failed preparation disposes only candidate resources. Failed activation restores the prior compatible implementation without undoing later user operations.

The runtime tracks requested, drafting, validating, previewing, publishing, active, blocked, cancelled, and failed states. Coda reports success only after the host acknowledges publication. Streaming model prose is not a completion signal. Progressive construction is allowed through individually valid previews, never by evaluating half-written source in the active scene.

## 13. Live observations and behavior

A behavior is versioned code plus subscriptions and state. It computes predicates from available observations and requests actions. Custom events have a namespace, schema, producer identity, sequence, and timing semantics; a new package can register new domain events without enlarging a central enum for every idea.

The base observation contract includes authorized state changes, interaction intents, time, package-owned state, and installed adapter signals. The scheduler supports edge-triggered conditions, debounce/hysteresis, bounded timers, cancellation, ordering, and backpressure. A six-foot threshold emits on crossing, not sixty Terminal launches per second. Time-based rules define behavior during pause/resume and reload. An object deletion revokes its subscriptions.

New sensors require actual observations from an authorized adapter. A generated predicate cannot establish an external fact it has no input for. New native integrations remain platform work; new combinations of available inputs are live package work.

External actions are routed through the host broker with event/request IDs. A saved world does not repeat past external actions merely because it was loaded. Launch/focus may use a narrowly remembered resource grant; close/restart and occupied-surface replacement retain fresh confirmation requirements. [R5]

A Windows side effect and a world transaction are not one atomic operation. Track requested, authorized, dispatched, observed, failed, and uncertain outcomes. Reconcile before retrying an uncertain action; never claim universal exactly-once execution or pretend undoing a world edit closes an application safely.

## 14. Persistence, compatibility, and undo

Use a single host-owned transaction store for world records and active package references, backed by immutable asset/package blobs. The first durable-store candidate is SQLite with crash tests; choose and pin the driver during implementation planning. There is one authoritative writer in the initial desktop product.

Publishing a revision stores validated blobs first and then commits references, parameters, and operation metadata together. A crash before commit can leave unreferenced blobs, which later garbage collection removes. It must not leave a committed entity referring to an unpublished or missing revision. Retain a recovery checkpoint and the last compatible revision.

Undo groups authored user operations. It does not replay every historical external action. Saving/exporting excludes credentials and grants that belong to the device/user profile. Import validates dependency graphs, archive paths, symlinks/reparse points, sizes, hashes, and state versions before activation.

glTF/GLB export is a geometry/material exchange operation. It does not preserve arbitrary behavior, code, permissions, or all custom shaders. A Workspace package/world export carries the richer editable representation. Label the difference in the UI.

On reload, load accepted data, resolve pinned packages/assets, reconstruct render resources, restore meaningful instance state, and only then subscribe active behaviors. Missing packages produce identifiable recoverable placeholders. Unsupported state versions stay quarantined with an explanation rather than disappearing or executing an incompatible migration.

## 15. Coda, OpenCode, and voice

Coda authoring consumes a relevant, bounded context: user request, selected entities and subparts, placement anchors, frame definitions, current revisions, declared capabilities, package source/dependencies, and pertinent conventions. It retrieves relevant SDK documentation and examples. It does not ingest the entire repo every turn or treat text inside an imported asset as authority.

The agent receives one scoped draft workspace per authoring job. The default authoring path permits package drafting, validated reads, and approved tooling only. Shell commands, repository publication, installation, and arbitrary filesystem access are separate capabilities. Generated code compilation/execution never runs inside the credential-bearing OpenCode process.

OpenCode is added alongside the direct Codex and xAI adapters. Engine, model provider, model ID, and voice are distinct settings. The UI names Codex accurately, including its ChatGPT sign-in path, without implying that it is the ordinary ChatGPT app or a new quota pool. Cursor remains disabled/unavailable until a real adapter exists; selecting it must not silently select Codex.

The real ChatGPT Windows surface retains its independent launch/capture/dock behavior. It is not used as an unofficial programmable model endpoint. Successful desktop capture, successful model authentication, and a successful model turn are three different checks.

### Workspace-private OpenCode

Ship a tested, pinned runtime and launch its exact absolute path. Keep private config, data, state, cache, logs, sessions, and authentication separate from any system OpenCode. Construct the child environment explicitly and work from a Workspace-owned authoring directory. Inspect the pinned version's actual configuration search and merging behavior; changing one XDG variable or OPENCODE_CONFIG_DIR alone is not proof of isolation. OpenCode's documentation says configuration sources merge. [T4]

Workspace owns the private server lifecycle, uses a loopback-only bound address, disables discovery, authenticates its connection with a private per-launch secret, and keeps credentials out of renderer messages/logs. OpenCode documents server health, sessions, events, and authentication; adapt to a pinned tested API rather than scraping terminal output. [T5]

Provider/model discovery is explicit. An unavailable model produces an actionable state. Do not automatically switch to a paid model, reuse personal CLI credentials, or install third-party plugins. Fresh private-provider sign-in remains the user's action. Bundling the executable does not bundle a model subscription or defeat provider limits.

OpenCode permission settings are an integration layer, not a filesystem/process sandbox. Verify the denied/approved tool paths and contain generated execution independently. Keep a foreign personal OpenCode installation/config present during isolation tests so accidental reuse is detectable. [T6]

### Voice

Speech synthesis and speech recognition are separate replaceable services. Preserve the speech interfaces and cancellation/caption behaviors, while evaluating a local neural voice engine with a preview control and an honest displayed voice name. Benchmark CPU/GPU/RAM cost and first-audio latency on representative hardware. The user accepts voice quality by listening, not from a passing unit test or a renamed Windows voice. Windows speech remains an explicit fallback. Audio/voice failure does not disable typed authoring or world editing.

No model training is required for the first runtime. Any future learning dataset is opt-in, redacted, and separate from auth and source-control history.

## 16. Security and failure model

Protect the product binaries/source, private credentials, user files, Windows resources, saved worlds, other packages, and trusted approval controls. Treat model output, package code, imports, assets, shader source, and web messages as untrusted inputs. Treat the bundled toolchain as a supply-chain dependency requiring integrity and version control.

The host derives the actor and package generation from the authenticated channel, not an actor field supplied by generated code. Manifest capabilities are requests, not grants. Resource-scoped checks occur on every privileged operation. Narrow remembered grants have revocation and upgrade policy; a package revision that adds authority triggers review. A localhost address and CORS policy alone do not authenticate a caller.

Keep the native/WebView host unelevated. Limit navigation and native exposure to the intended origins and validate every message. Microsoft specifically recommends treating web content as insecure, validating messages, and avoiding generic proxies to native APIs. [T7]

Initial threats include an infinite loop, allocation flood, forged world edit, stale generation callback, path traversal, unauthorized fetch, plugin/config inheritance, a malicious build script, repeated behavior firing, and a GPU stall. Each has an executable acceptance test or a specifically recorded platform limitation. The release claim is bounded to the threat model tested; no claim of perfect sandboxing or protection against a compromised operating system is made.

Trusted recovery controls live outside package-authored UI. A package cannot approve its own request, change model configuration, or impersonate a successful host action. Global pause stops package behaviors and revokes pending execution requests without requiring a model response.

## 17. Efficiency rules

Interactive transforms, ordinary parameter edits, cached package instantiation, and playback do not require a model call. Use model generation when a new implementation or genuinely ambiguous instruction requires it. Reuse approved package code and compile-cache entries by digest.

The render loop never waits for OpenCode, TTS, a build, or a network request. Batch accepted changes and transferable geometry buffers. Share immutable assets. Schedule observers only for relevant changes; bound polling and timer frequency. Large groups can use instanced rendering while maintaining logical instance IDs for selection and overrides.

Record input-to-preview latency, host acknowledgement latency, compile duration, model latency, time-to-first-audio, CPU time, memory, GPU resource counts, and reload cleanup. Use named benchmark fixtures and hardware records. Initial budgets are set from the first measured proof and versioned with the tests; this specification does not invent a universal frame-rate claim.

Keep development work small enough to inspect. Every code change names its owning module, public contract impact, tests, and recovery effect. Reuse existing passing tests and add regression tests at real boundaries. Mock providers for quota-independent automation; reserve live-provider checks for an explicitly selected account/model.

## 18. Delivery gates

These are architectural milestones, not an instruction to implement the whole tree at once. Each implementation slice receives a short concrete plan after this specification is approved.

**M0: Architecture checkpoint.** Record the exact prototype commit, create the isolated design branch, publish this specification, and check the change is documentation-only. Outcome: reviewable decisions and a preserved reference, not a new installer.

**M1: Runtime boundary proof.** Build the smallest native-host/renderer harness and one manually authored package that generates a colored path at arbitrary 3D points. Give it stable identity, an anchor, direct manipulation, parameter edits, a source revision, hot replacement, save/reload, and failure recovery. Run the guest isolation/CPU/memory/ownership tests. Validate broad low-level geometry and at least one shader-backed construct. This milestone can run without any model quota.

**M2: Manipulation and live rules.** Add the pinned-board resize, persistent constraints, stable subpart selection, relevant revision conflicts, undo, and a live threshold rule. Demonstrate grabbing during generation and changing a rule without restarting. These are test packages using generic primitives, not hardcoded product features.

**M3: Private OpenCode authoring.** Bundle the isolated runtime, expose an accurate engine/provider/model selector, connect a user-selected available provider, and generate the same packages through Coda. Run clean-machine and contaminated-personal-config isolation tests. A quota error leaves the editor working and reports the actual limit.

**M4: Product integration and migration.** Port real application surfaces, capture/input, docking, host capabilities, launcher recovery, and compatible user state into the new composition. Keep native and fallback artifacts unmistakably separate. Validate one full installed native flow with no repository checkout or globally installed OpenCode. Evaluate and let the user select the new voice independently.

**M5: Expansion and VR.** Expand package libraries, assemblies, import/export, authorized external behaviors, advanced graphics profiles, and VR adapters through their contracts. VR hardware and advanced renderer profiles each have dedicated acceptance gates. They are not implied by setting renderer.xr.enabled.

## 19. Required acceptance evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A01 | Move an anchor in midair, create an object there | Exact pose preserved; marker display size does not distort object dimensions |
| A02 | Draw a colored line between arbitrary 3D points | Position/color match saved values; no floor-plane assumption |
| A03 | Drag while a delayed package revision completes | Same entity ID and latest user pose; no snap-back |
| A04 | Change color while another field changes | Independent update succeeds; genuinely stale dependent update is rejected |
| A05 | Pin a board end and stretch the other | Pinned point error within declared tolerance; thickness stable; undo restores shape |
| A06 | Change/delete a grabbed subpart during reload | Safe deferral or explicit re-selection; no handle jump |
| A07 | Replace a valid package with malformed source | Prior revision and world stay usable; candidate resources cleaned |
| A08 | Run a looping/allocation-flood package | Budget enforcement terminates/quarantines it; recovery controls remain available |
| A09 | Attempt unauthorized file/process/network/native access | Denial observed at the actual boundary; no side effect |
| A10 | Forge another package's resource or actor identity | Rejected by host/runtime ownership checks |
| A11 | Reload while an old timer/callback is pending | Retired-generation effects rejected |
| A12 | Add a crossing/time rule and modify it live | Correct edge and timer semantics; no duplicate storm |
| A13 | Save, crash at publication boundaries, reopen | Last committed compatible state; no missing active blobs or half revisions |
| A14 | Export/import a reusable assembly | Stable relationships, appropriate new instance IDs, no credentials transferred |
| A15 | Remove and reload packages repeatedly | Resource counts return to bounded baseline; no accumulating listeners/streams |
| A16 | Author with private OpenCode beside a personal install | Private executable/config/auth used; personal sentinel files unchanged |
| A17 | Provider unauthenticated, quota-limited, or absent | Accurate state; typed/direct editing remains usable; no unapproved paid fallback |
| A18 | Request a Windows action from a preview/reloaded rule | Preview/replay cannot perform it; live action follows existing approval semantics |
| A19 | Test a real Windows surface in the installed native build | Launch/capture/input/dock verified; artifact provenance tied to commit and runtime |
| A20 | Switch/fail speech output | Text remains usable; cancellation works; exact engine/voice displayed |
| A21 | Send desktop and synthetic controller manipulation intents | Same host command semantics; hardware VR acceptance remains separately recorded |
| A22 | Request unsupported addon/native capability | Visible compatibility result; no success claim or covert privileged execution |

Schema/property/contract tests support these scenarios. They do not replace installed runtime, security, persistence fault-injection, GPU, or audible-voice checks. Record command, input fixture, runtime versions, environment, output, and limitations for each accepted gate.

## 20. Decisions required before code publication

The immediate review is this specification's ownership, concurrency, creative-execution, and milestone design. Once approved, write the M1 implementation plan and test cases, then implement test-first in the isolated line.

The M1 execution spike must verify the chosen guest engine/wrapper, supported module imports, bounded execution, renderer resource bridge, and performance. Failure changes that runtime adapter before broad authoring work proceeds; it never justifies running generated code directly inside the privileged application.

The SQLite driver, pinned OpenCode version, neural voice runtime/model, and advanced renderer isolation profile are milestone-specific selections, each made against a concrete test. They are intentionally not installed or asserted working by a documentation commit. The contracts above remain the acceptance basis for those choices.

## 21. Baseline and technical references

Repository facts refer to the pinned reference commit, not mutable main. Technical documentation was checked on 2026-09-11. External documentation supports the named underlying mechanisms; the vNext design is our proposal and still needs implementation evidence.

- [R1: Repository README](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/README.md).
- [R2: Existing world-schema](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/packages/world-schema/src/index.ts).
- [R3: Native composition and provider/voice startup](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/apps/desktop-native/src/Workspace.Desktop/Runtime/DesktopCoordinator.cs).
- [R4: Spatial composition, native bridge routing, and surface controls](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/apps/spatial-client/src/app/createWorkspaceApp.ts).
- [R5: Existing external-action policy](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/apps/desktop-native/src/Workspace.Desktop.Core/Runtime/WorkspaceActionPolicy.cs).
- [R6: Successful baseline Windows CI run](https://github.com/craigCODA/workspace-environment/actions/runs/34588178931). This is historical baseline evidence, not a new vNext test run.
- [R7: Native versus fallback artifact workflow](https://github.com/craigCODA/workspace-environment/blob/58822736500e98192429297c6ab5cf3c919be14a/.github/workflows/windows-ci.yml).
- [T1: QuickJS embedding, memory handling, and execution interrupts](https://bellard.org/quickjs/quickjs.html).
- [T2: Node.js VM security warning](https://nodejs.org/api/vm.html#vm-executing-javascript).
- [T3: Three.js API reference](https://threejs.org/docs/).
- [T4: OpenCode configuration merging and locations](https://opencode.ai/docs/config/).
- [T5: OpenCode server API](https://opencode.ai/docs/server/).
- [T6: OpenCode permissions](https://opencode.ai/docs/permissions/).
- [T7: Microsoft WebView2 security guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/security).
