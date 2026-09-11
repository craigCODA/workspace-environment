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
| Interaction | Pointer/controller intent, transient selection, anchors, drag previews, transform/constraint requests, Build/Edit/Use mode | Direct persistence writes, guest execution on the pointer path, or bypasses around world commands |
| Coda | Conversation state, relevant context, authoring requests, agent/voice selection, progress | Root authority over the world, a privileged mutation path, or the application source |
| Platform Adapters | Windows discovery, capture, input, approved external actions, storage and process supervision | Invented model conclusions about what happened on Windows |

A narrow native composition layer wires these modules together. Concrete agent clients and speech engines are adapters behind Coda's interfaces. The launcher handles software-version activation separately from world-package activation.

The authority chain is explicit: Windows supplies facts about Windows resources. The host's World Core owns the durable representation and accepted spatial state. The renderer is a projection. Agents and packages submit requests through the same host contracts available to ordinary interaction. A failed renderer cannot rewrite the world to justify what it happened to draw.

### Closed host command surface

World Core accepts a closed, versioned command surface. Unknown creative ideas are expressed through package data, SDK resource descriptors, observations, relationships, and these existing commands. A package cannot register a new host verb. Coda has no private agent mutation API. Adding a new host command requires a shipped Workspace protocol version, review, and acceptance evidence.

The initial vNext command names are:

```text
Read / inspect
  world.read
  entity.inspect
  package.inspect
  capability.inspect

World mutation
  entity.create
  entity.remove
  entity.rename
  entity.reparent
  transform.set
  parameters.patch
  relationships.add
  relationships.remove
  reference.grant
  reference.revoke
  constraint.add
  constraint.remove

Edit-session lifecycle
  edit.begin
  edit.update
  edit.commit
  edit.cancel

Package / instance lifecycle
  instance.duplicate
  parameters.copy
  package.fork
  package.publish
  package.activate
  package.disable
  package.rollback
  package.delete
  package.state.patch

Import / export
  workspace.import
  workspace.export

Host-backed external actions
  application.search
  application.open
  window.focus
  surface.bindWindow
  application.profile.save
  application.profile.delete
  application.close
  application.restart
```

`reference.grant`, capability approvals, and other authority-changing operations are issued only through trusted host policy/user approval paths; a guest cannot grant itself authority by invoking or forging their payloads. `package.delete` removes a package from active/catalog references according to retention policy; immutable published blobs remain subject to normal garbage collection rather than in-place mutation.

### Process topology and lifecycle ownership

Logical modules are not automatically security boundaries. The intended product topology is:

```text
Trusted native host / World Core
  ├─ trusted renderer process / WebView
  │    └─ disposable guest worker
  │         └─ isolated JavaScript guest engine
  ├─ Workspace-private OpenCode process
  └─ voice service/adapter
```

The host owns guest lifecycle, package generation tokens, activation, budgets, and termination even when the physical worker is created beside the renderer. The renderer owns WebGL resources but not package authority. OpenCode owns model-provider interaction but never executes generated world code as part of its credential-bearing process.

M1 may temporarily co-locate the trusted host and renderer for the smallest proof. Co-location is not a security argument. If renderer/GPU failure takes down that combined process, A35 recovery is proven from committed disk state on restart, not by pretending process separation already exists.

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

A package definition, a published package revision, and an object instance are distinct. An instance keeps its ID when its package changes. Reusing a brick creates a new instance identity that can reference the same package revision. Saving or publishing a reusable assembly records semantic children and attachment rules, not a screenshot of the result.

Three operations remain distinct throughout the world model and Coda vocabulary:

- **Duplicate instance** creates a new instance/entity ID that references the same package revision and starts from copied instance parameters according to the operation's options.
- **Fork package** creates a new package identity with explicit source lineage from the prior package; existing instances keep their original package identity unless separately migrated.
- **Copy parameters** keeps all source and target instance identities intact and patches only the explicitly selected compatible authored parameters on the targets.

Selection itself is not persisted as world identity. Interaction resolves its transient selection set into explicit source/target IDs when `parameters.copy` is submitted.

New spatial entities receive persistent opaque IDs. Renaming changes the display name, not the ID. Existing pc.application:, pc.window:, and spatial.surface identities are preserved by import; aliases are explicit rather than reconstructed from a new naming scheme. [R2]

Each instance has an authoritative root transform, authored parameters, implementation revision, logical children, and optional host binding. Meaningful subparts have stable keys, such as board.end.east, rather than relying on a triangle index that changes when geometry regenerates.

Editable authored geometry belongs in instance parameters or immutable referenced blobs with instance-level references/overrides, not only as literals buried in package source. Package source defines algorithms, schemas, defaults, and rendering behavior. If a user can drag a curve control point, board endpoint, path waypoint, or other authored value and expects it to survive reload, that value must resolve to durable instance state. Renderer buffers and temporary generated vertices remain projections of that state.

Coordinates use metres internally, a right-handed frame with positive Y upward, radians for angular values, and normalized quaternions for orientation. The initial world compass maps east to positive X and north to negative Z; the compass is independent of camera yaw. Feet/inches are UI conversions. Presentation dimensions and dimensionless scale are distinct; the prototype's size field must not be blindly reinterpreted as a new scale field.

World, parent-local, and geometry-local coordinates are explicit in commands. A transform supplied in a parent frame includes the parent identity and relevant revision. Cyclic parenting, non-finite values, invalid quaternions, and non-invertible parent transforms are rejected. Unsupported shear/non-uniform-scale combinations produce a recoverable editing error rather than corrupted geometry.

Simulation variables and visual interpolation can remain transient. Parameters needed to reconstruct an authored creation are durable. A package declares what state is checkpointed; saving does not pretend to preserve arbitrary in-flight JavaScript stacks or every particle trajectory.

## 8. Placement and direct manipulation

A placement ball is a visible representation of a Spatial Anchor with a durable ID and pose. Its visible radius is a handle size, not automatically the intended size of the generated object. Creating a fireplace at that anchor preserves the selected pose and uses explicit object dimensions or a user-sized placement volume.

The user can grab a creation while it is building. The root pose remains controlled by the world/interaction transaction. Package geometry is generated in the instance's local frame. Replacing geometry never republishes a stale root pose as a side effect.

Desktop editing provides axis handles, free drag, depth adjustment, local/world frames, numeric entry, and optional snapping through a consistent manipulation service. Camera capture remains a separate input mode: primary click on empty space acquires camera control; Escape releases it. UI, active transform handles, application surfaces, and an ongoing drag do not accidentally capture the camera.

Build/Edit and Use modes, camera ownership, selection, and direct manipulation belong to Interaction. Packages can expose selectable handles and behavior targets but cannot steal camera control, change the current mode, replace the selection set, or manufacture a trusted selection result. Selection is a transient set of semantic instance/subpart IDs plus revision-aware hit information. `parameters.copy` uses that selection only to form an explicit command; World Core never persists "whatever is selected" as an implicit target.

Edit sessions are explicit host-issued leases scoped to the entity and affected fields. Local motion previews remain responsive while host updates arrive. A single completed drag is one undoable user operation, with bounded accepted checkpoints rather than a permanent log entry for every pointer pixel.

The crash/cancel rule is deterministic. During an edit, the last host-accepted checkpoint is authoritative and the local drag/deformation after that checkpoint is preview state. If the edit commits, World Core accepts the final command, releases the lease, and records one undoable operation. If the process crashes, the input device disappears, the connection drops, or the edit is cancelled before commit, Workspace restores the last accepted checkpoint, discards unaccepted preview state, expires/releases the lease, and creates no duplicate undo entry. Reload never revives an in-progress manipulation lease.

VR controllers and hands later produce the same begin/update/end interaction intents. The interface is device-neutral now; hardware tracking, locomotion comfort, headset rendering, and remote Windows capture still require their own VR acceptance work.

## 9. Concurrent user and agent edits

All accepted world mutation flows use the closed host commands defined in §4. The command surface is the authority boundary for user interaction, Coda, package behaviors, imports, and recovery. There is no agent-only `setWorld`, renderer-only persistence write, or migration-only command back door.

The write-bearing command groups and scopes are:

| Commands | Required scope |
| --- | --- |
| `entity.create`, `entity.remove`, `entity.rename`, `entity.reparent` | Explicit entity/parent scope established by the initiating actor and current world policy |
| `transform.set` | Target instance transform revision; denied when another actor holds an incompatible lease |
| `parameters.patch` | Named target parameter IDs and parameter revision; package schema validates the values |
| `relationships.add`, `relationships.remove` | Explicit source entity plus durable target reference; creation of write-capable relationships is separately authorized |
| `reference.grant`, `reference.revoke` | Trusted host/user policy only; never self-issued by guest code |
| `constraint.add`, `constraint.remove` | Target constraint set plus referenced handle/parameter keys and constraint revision |
| `edit.begin`, `edit.update`, `edit.commit`, `edit.cancel` | Host-issued lease over exactly the declared entity fields |
| `instance.duplicate`, `parameters.copy`, `package.fork` | Explicit source and target identities resolved before submission |
| `package.publish`, `package.activate`, `package.disable`, `package.rollback`, `package.delete` | Package identity, candidate generation, package lifecycle policy, and relevant implementation/state revisions |
| `package.state.patch` | The calling package instance's own declared non-leased checkpoint/local state only |
| `workspace.import`, `workspace.export` | Trusted import/export policy; never an arbitrary guest filesystem path |
| External action commands from §4 | Existing host capability/confirmation policy; package world authority does not imply Windows authority |

The host serializes accepted mutations and maintains separate revisions for placement, authored parameters, relationships/references, constraints, package implementation, and checkpointed package-local state. Commands carry a request ID, only the expected revisions they actually depend on, proposed changes, and an authenticated actor/generation context supplied by the host channel.

A geometry/color proposal must not fail merely because the user moved the object, unless the proposal actually depends on its world position. A root-placement change generated before a user drag is stale and cannot overwrite that drag. A proposal that depends on a pinned edge revalidates the edge/constraint revision before publication.

Example: Coda begins package revision 4 at transform revision 12. The user drags the instance to transform revision 18. The new implementation publishes under the same instance identity and current transform revision 18. The old position is never copied from generated source into accepted state.

Package code may update its own non-leased visual/checkpoint state through `package.state.patch` and may emit package-owned renderer descriptors within budget. It cannot write a field currently leased for direct manipulation. It cannot use ownership of package A to mutate package B or an unrelated entity. A cross-entity rule such as "this switch controls that wall" stores a durable target reference and, for world mutation, an explicit field/action grant describing exactly what can be requested on that target. A readable reference is not a write grant.

Geometry changes affecting a currently grabbed subpart wait for a safe interaction boundary unless stable-handle remapping is validated. A removed/redefined handle requires explicit re-selection; it never jumps the hand or mouse to a different vertex silently. Picking results are logical IDs/handle keys validated against current revisions, not durable triangle/primitive indices.

Undo records which revision planes an accepted operation changed. Undoing a constraint edit must not roll back an independent later placement edit; undoing a placement must not silently swap package implementation revisions. When an inverse can no longer be applied against the relevant revision plane, Workspace reports a conflict instead of reverting unrelated newer state.

Rollback reverts the failed implementation and its compatible authored/package-local state. It does not revert independent user moves, relationships, grants, or other entities edited since the upgrade began.

## 10. Constraints and the pinned board

A rigid transform, anchored resize, and constrained parameter mapping are different operations. The interaction contract declares which one is active.

Constraint extensibility in M2 is **declarative**. Package activation may publish constraint declarations containing stable handles, anchor/reference identities, axes/frames, numeric limits, parameter IDs, and an operator ID from a closed host-owned operator set. Trusted Interaction / World Core validates the declaration and applies the selected operator to pointer/controller movement. A package cannot submit a function, evaluator, callback, shader, expression language, or executable mapping as a constraint operator.

The M2 operator set begins with two deterministic mappings:

```text
projectedDistanceAlongAxis
  pointer/controller displacement projected onto a declared axis
  -> bounded scalar parameter (for example length)

projectedAngleAboutAxis
  pointer/controller direction/arc around a declared axis
  -> bounded angular parameter (for example hinge angle)
```

No guest JavaScript, `eval`, model call, or per-frame guest callback executes on the pointer-move path. Adding a new host mapping operator is a shipped Runtime/World Core change with its own tests. Packages remain extensible by applying and composing the published declaration contract to arbitrary package-owned handles/parameters; they do not extend the trusted operator implementation at runtime.

For the first A05 fixture, resolve the board's east end once to a stable end handle and world anchor. The opposite handle maps through `projectedDistanceAlongAxis` against the board's declared local length axis. Its projected distance determines the authored length; the midpoint/root adjustment preserves the pinned end. Thickness and height remain unchanged unless explicitly included. A minimum positive length prevents inversion. The pinned end's world position is checked numerically after each accepted edit.

The second A05 fixture is intentionally a different mechanism use: a pinned hinge/lever exposes an angular handle and maps it through `projectedAngleAboutAxis` to an authored angle parameter with declared limits. The passing record names the same constraint declaration schema plus the specific operator ID; it does not treat "board" or "lever" as the mechanism.

Locking a point does not automatically lock a whole face or orientation. A face lock is a different declared constraint. World-axis words and local-axis operations retain their declared reference frame after an object rotates. The solver never guesses a new "east end" during an active drag.

Spline/cage deformation, arbitrary vertex constraints, conflicting multi-pin systems, inverse kinematics, and physical simulation require later host operators/solvers and dedicated acceptance work. Passing A05 proves the declaration/operator architecture and the two named mappings. It never establishes a universal CAD solver.

## 11. Creative execution and rendering

### Chosen safety boundary

The default execution path runs package JavaScript in an isolated guest engine, provisionally a QuickJS WebAssembly build inside a disposable worker. The trusted wrapper supplies a constrained module loader and only explicit Workspace Creative SDK imports. There are no guest bindings for the filesystem, environment variables, process execution, network, DOM, provider tokens, native bridge, or the application's Three.js module. QuickJS exposes memory/stack limits and an interrupt callback; the exact WebAssembly wrapper and limits must be proven in the first engineering spike before being pinned. [T1]

The worker adds responsiveness and termination control; the guest engine and its imports provide the execution boundary. A plain Worker or node:vm is not accepted as the security argument. Node's documentation explicitly rules out treating node:vm as a security mechanism. [T2]

**Three.js does not run inside the guest package.** `import "three"` fails in the M1–M2 guest profile unless a future reviewed compatibility shim converts that API into the same constrained descriptor protocol. Three.js and the WebGL/WebGPU renderer live only in the trusted Scene Renderer.

The guest SDK builds typed, versioned resource descriptors and update commands. The trusted runtime validates package ownership, schema, resource budgets, asset authority, and generation tokens before forwarding accepted descriptors to the renderer. The renderer converts them into Three.js resources. Guest output never passes an executable callback/function object into the trusted renderer.

```text
guest package
  -> Workspace Creative SDK
  -> typed descriptors / bounded updates
  -> trusted validation + ownership mapping
  -> trusted Three.js renderer
```

Authoritative entity roots, camera state, selection, Build/Edit/Use mode, approval UI, other packages' handles, and raw Three.js objects never become guest objects. Package-local public handles are opaque capabilities mapped by the trusted runtime; guessing a semantic ID or renderer resource ID does not create access.

Descriptor fields that refer to assets accept host-issued asset handles or package-owned validated blob references, not arbitrary `file://`, filesystem paths, HTTP(S) URLs, UNC paths, or other fetchable locators. This rule applies to textures, models, fonts, audio, shader includes, and future descriptor kinds. The trusted renderer must not become a confused deputy that performs a forbidden fetch on behalf of a guest.

### Creative breadth

The SDK descriptor family must expose low-level enough primitives that the core does not need a feature enum for every creative idea. M1's positive breadth matrix includes at least:

- indexed geometry plus custom vertex attributes;
- a curve/path representation beyond A02's straight line;
- custom shader source plus validated uniforms;
- a texture referenced by a host asset handle;
- a light contribution;
- procedural or point-buffer updates under the package tick budget;
- an instanced draw path, without claiming A49's large-group per-instance identity scale.

Lights created by a package may contribute to the scene's shared illumination because lighting is inherently world-visible. That does not grant access to another package's material, shader callback, resource handles, render callbacks, camera, selection, interaction mode, or trusted scene lifecycle. Labels in the initial guest profile are scene-rendered mesh/texture/SDF-style resources through the descriptor bridge; packages do not create arbitrary DOM overlays.

Build on Three.js and reviewed addons in the trusted renderer instead of inventing a second graphics engine. Generate mesh buffers and shader logic when no existing high-level descriptor matches an idea. Neither a FloorGrid enum nor a Fire feature switch belongs in the core. [T3]

This is broad rendering expressiveness, not a claim that every Three.js API can run unchanged through a restricted descriptor bridge. DOM-dependent loaders, external asset fetching, renderer-wide post-processing, arbitrary `onBeforeRender` callbacks, and backend-specific APIs require compatible reviewed adapters. A capability manifest makes support discoverable before generation.

Renderer-wide custom modules are a separate advanced execution profile. They require isolated rendering/compositing and explicit threat/performance acceptance before enabling them for user-authored content. They must not be smuggled into the trusted main renderer as an escape hatch. Depth, picking, shadows, app-surface composition, and VR integration are proof obligations for that profile. Until those tests pass, its status is unsupported, not silently approximated as complete Three.js compatibility.

### Resource and failure control

The host owns guest lifecycle and CPU/execution, allocation, output-message, subscription, asset, geometry, texture, and draw-work budgets. Limits are visible in diagnostics. Repeated identical geometry shares resources where possible; individual bricks are not individual processes or independent render loops. Typed buffers are transferred/batched rather than JSON-serializing vertices every frame. The renderer owns its render loop; guest/behavior scheduling is bounded and independent of model latency.

Shader execution can exhaust or wedge a GPU despite JavaScript isolation. Test compilation failures, runaway resource creation, context/device loss, and repeated reloads. If the trusted control path remains responsive, it can disable/quarantine the offending package. If a graphics failure takes down the renderer or a co-located M1 process, committed world state must remain intact on disk and Workspace reconstructs from the last committed compatible state after restart. Recovery never grants the package additional authority. Resource lifecycle cleanup covers geometry, materials, textures, render targets, subscriptions, timers, audio, and temporary asset handles. A whole-machine GPU/driver failure cannot be guaranteed invisible or instantly recoverable.

## 12. Package lifecycle and hot replacement

A package includes a manifest, editable source, package-local locked dependency versions, owned assets with provenance, parameter/state schema, stable handle declarations, constraint/observation declarations, and requested capabilities. SDK, package-state, and renderer-profile compatibility are explicit. Hashes detect content changes; hashes alone do not establish trust.

For M1–M2, guest code may import only the versioned Workspace Creative SDK and dependencies resolved from that package's own approved lock/allowlist. Guest-to-guest executable imports are forbidden. One package may reference another entity through durable world relationships/grants, but it cannot import another package's executable module or share a global npm-style dependency environment. A future guest-to-guest module system is M5+ work and must satisfy A54 before it is enabled.

The publication pipeline is:

```text
request -> isolated draft -> deterministic compile -> validate
        -> candidate preparation -> preview/health checks
        -> revalidate current world revisions -> commit activation
        -> retire previous runtime resources
```

Compilation uses a pinned toolchain and approved resolver. It does not run package installation hooks, arbitrary npm scripts, lifecycle scripts, or untrusted build plugins. Generated code writes into a draft workspace, not the application source or active package store.

Package-state migrations are pure schema/state transformations. A migration receives only the prior declared package state/parameters and migration metadata and returns data validated against the next schema. It cannot issue world commands, access capabilities, launch processes, fetch the network, mutate another entity, invoke the renderer, or approve anything. Migration failure quarantines the candidate/unsupported state while preserving the prior compatible revision/state.

Candidate preparation has no external side effects. It cannot launch an application, submit a network action, commit world edits, issue cross-entity commands, or perform capability-granted actions while being evaluated. The old revision remains active until the candidate is ready. The host then commits the chosen package revision and compatible authored state together. Render-resource staging is coordinated with that commit; durable transaction atomicity is distinct from GPU frame timing.

Every execution instance has a generation token. Replies, timers, descriptor updates, observer emissions, and messages from a retired generation are rejected. Preparation, state migration, and cleanup have deadlines. Failed preparation disposes only candidate resources. Failed activation restores the prior compatible implementation without undoing later user operations.

Disable, rollback, duplicate-instance, fork-package, copy-parameters, delete, and activation are distinct lifecycle/authoring operations. Published revisions remain immutable. Rollback changes the active revision reference and compatible package state; it does not copy an old root transform over newer user placement.

The runtime tracks requested, drafting, validating, previewing, publishing, active, blocked, cancelled, and failed states. A candidate does not become active because source is syntactically partial-valid, because a model streamed a confident completion message, or because a preview rendered once. Coda reports success only after the host acknowledges publication/activation. Progressive construction is allowed through individually valid preview generations, never by evaluating half-written source in the active scene.

## 13. Live observations and behavior

A behavior is versioned package code plus subscriptions and declared checkpoint state. It computes predicates from authorized observations and requests actions through the closed host command surface. Custom events have a namespace, schema, producer identity, generation, sequence, and timing semantics; a new package can register new domain events without enlarging a central enum for every idea.

The base observation contract is intentionally low-level enough to express compound conditions without adding a host trigger for each sentence. It includes, subject to references/grants and privacy policy:

- accepted entity/property/revision changes;
- pose samples for referenced entities in declared world/parent/local frames;
- declared plane and volume crossing observations derived from authoritative pose samples;
- host-derived linear and angular rates with timestamps/revisions;
- interaction begin/update/end/cancel intents and relevant handle keys;
- package-owned checkpoint/local state changes;
- namespaced custom package events;
- installed platform-adapter signals; and
- host-provided monotonic tick/timer observations.

Behavior timing uses the host-provided monotonic clock/tick contract, not guest `Date.now()` as authoritative time. SDK timers are scheduled by the host/runtime and have explicit pause/resume/cancellation semantics. Tests can substitute the host clock deterministically.

The scheduler supports edge-triggered conditions, debounce/hysteresis, bounded timers, cancellation, deterministic same-tick ordering, cycle/recursion limits, and backpressure. A six-foot threshold emits on crossing, not sixty actions per second. A compound predicate such as "cross this plane while rotating clockwise" can be implemented from crossing plus angular-rate observations in package code rather than as a new host verb. A behavior feedback loop cannot grow without bounds merely because two packages emit events to one another.

Budgeted guest ticks may drive package-owned procedural visuals and may request `package.state.patch` for declared non-leased checkpoint state. They cannot write a transform/parameter/constraint field currently held by an edit lease, cannot move another entity through renderer descriptors, and cannot execute on the pointer-move path. User root movement remains authoritative while package-local fire, particles, or other procedural motion continues in the instance's local frame.

New physical/external sensors require actual observations from an authorized platform adapter. A generated predicate cannot establish an external fact it has no input for. New native integrations remain platform work; new combinations of available observations are live package work.

Deletion revokes subscriptions and pending observations tied to the deleted identity. Pause/resume behavior is declared and tested; queued work does not fire after deletion merely because the scheduler resumes. A missing durable reference follows its declared missing-reference policy; the runtime does not retarget by display name, package digest, spatial proximity, or another coincident identifier.

External actions are routed through the host broker with event/request IDs. A saved world does not repeat past external actions merely because it was loaded. Launch/focus may use a narrowly remembered resource grant; close/restart and occupied-surface replacement retain fresh confirmation requirements. [R5]

A Windows side effect and a world transaction are not one atomic operation. Track requested, authorized, dispatched, observed, failed, and uncertain outcomes. Reconcile before retrying an uncertain action; never claim universal exactly-once execution or pretend undoing a world edit closes an application safely.

## 14. Persistence, compatibility, and undo

Use a single host-owned transaction store for world records and active package references, backed by immutable asset/package blobs. The first durable-store candidate is SQLite with crash tests; choose and pin the driver during implementation planning. There is one authoritative writer in the initial desktop product.

Publishing a revision stores validated blobs first and then commits references, parameters, and operation metadata together. A crash before commit can leave unreferenced blobs, which later garbage collection removes. It must not leave a committed entity referring to an unpublished or missing revision. Retain a recovery checkpoint and the last compatible revision.

Undo groups authored user operations. It does not replay every historical external action. Saving/exporting excludes credentials and grants that belong to the device/user profile. Import validates dependency graphs, archive paths, symlinks/reparse points, sizes, hashes, and state versions before activation.

glTF/GLB export is a geometry/material exchange operation. It does not preserve arbitrary behavior, code, permissions, or all custom shaders. A Workspace package/world export carries the richer editable representation. Label the difference in the UI.

On reload, load accepted data, resolve pinned packages/assets, reconstruct render resources, restore meaningful instance state, and only then subscribe active behaviors. Missing packages produce identifiable recoverable placeholders. Unsupported state versions stay quarantined with an explanation rather than disappearing or executing an incompatible migration.

## 15. Coda, OpenCode, and voice

Coda authoring consumes a relevant, bounded context: the user's request, explicit selected entity/subpart IDs, stable handle keys, placement anchors, world/parent/local frame definitions, current relevant revision numbers, package parameter/state schemas, the package's declared SDK capability manifest, package source/dependencies, and pertinent conventions/examples. Context is assembled from current authoritative state rather than cached scene coordinates so generated code does not hardcode a pose that was already moved.

Coda context does not contain capability tokens, raw remembered-grant secrets, provider credentials, OpenCode authentication state, unrelated filesystem contents, or surprise screenshots merely because those resources exist. A screenshot or other sensitive observation is included only through an explicit context capability/policy path relevant to the request.

Text from imported assets, package metadata, object names, files, web content, comments, and other untrusted world content is labeled and treated as data, not as system/developer/user authority. It cannot grant tools, approve permissions, replace higher-priority instructions, or tell Coda to escape the draft. A55 validates this boundary.

The agent receives one scoped draft workspace per authoring job. The default authoring path permits package drafting, validated reads, and approved tooling only. Shell commands, repository publication, installation, and arbitrary filesystem access are separate capabilities. Generated code compilation/execution never runs inside the credential-bearing OpenCode process.

Two simultaneous authoring jobs have distinct draft identities, source bases, expected world/package revisions, and cancellation state. Publishing one job revalidates the other before activation. A later model completion cannot overwrite a package simply because its prose arrived last; A58 proves the reconciliation path.

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

Interactive picking, selection, root transforms, ordinary parameter edits, edit-lease handling, and M2 host constraint operators run without a model call and without executing guest JavaScript on pointer/controller movement. Cached package instantiation and ordinary playback also do not require a model. Model generation is reserved for creating/revising implementations or genuinely ambiguous intent.

Guest JavaScript runs only through budgeted authoring/activation work, observation/event delivery, explicit SDK timers, and scheduled procedural ticks. Those ticks are decoupled from the pointer-move path and cannot write leased fields. A slow or terminated guest can freeze its own procedural effect without making an active drag wait for it.

The render loop never waits for OpenCode, TTS, a package compile, a guest response, or a network request. Batch accepted changes and transferable geometry buffers. Share immutable assets. Schedule observers only for relevant changes; bound polling and timer frequency. Large groups can use instanced rendering while maintaining logical instance IDs for selection and overrides where the selected acceptance profile supports them.

Reuse approved package code and compile-cache entries by digest. Direct edits operate on durable instance parameters/state rather than regenerating source for every numeric change. Source regeneration happens only when the package implementation actually needs to change.

Record input-to-preview latency, host acknowledgement latency, constraint-operator latency, guest tick/runtime latency, compile duration, model latency, time-to-first-audio, CPU time, memory, GPU resource counts, and reload cleanup. Use named benchmark fixtures and hardware records. Initial budgets are set from the first measured proof and versioned with the tests; this specification does not invent a universal frame-rate claim.

Keep development work small enough to inspect. Every code change names its owning module, public contract impact, tests, and recovery effect. Reuse existing passing tests and add regression tests at real boundaries. Mock providers for quota-independent automation; reserve live-provider checks for an explicitly selected account/model.

## 18. Delivery gates

These are architectural milestones, not an instruction to implement the whole tree at once. Each implementation slice receives a short concrete plan after this specification is approved. A milestone claim is limited to the generic mechanisms its acceptance record names; a representative demo object never expands the claim by implication.

**M0: Architecture checkpoint.** Record the exact prototype commit, create the isolated design branch, publish this specification, and check the change is documentation-only. Outcome: reviewable decisions and a preserved reference, not a new installer.

**M1: Runtime boundary and creative-resource proof.** Prove the host-owned world/command boundary, package lifecycle, isolated guest execution, descriptor pipeline, stable identity, durable authored parameters/blobs, direct root manipulation, revision-safe hot replacement, picking after regeneration, save/reload, fault recovery, and model-offline editing. Manually authored packages exercise the A47 descriptor-kind matrix through the Workspace Creative SDK; guest code does not import Three.js. A43 proves scheduled local procedural updates remain local while the user moves the root. A24/A53 prove structural identity/reference behavior. M1 proves package isolation, compilation-hook denial, descriptor confused-deputy denial, host-message forgery denial, resource budgets, generation retirement, and recovery from malformed code/GPU loss according to the tested process topology. M1's A34 fixture is narrow: a revision requesting unavailable/ungivable authority cannot activate as if the authority were granted. No model quota is required.

The allowed M1 claim is: **Workspace has a persistent, manipulable, model-independent live-package runtime with low-level scene descriptor expressiveness, authoritative host state, isolated guest execution, and revision-safe recovery for the mechanisms explicitly tested.** It is not yet a general constraint solver, AI authoring system, complete Windows product migration, or full Three.js API sandbox.

**M2: Deterministic constraints, interaction modes, and live behavior.** Prove package-declared constraints through the closed host mapping-operator contract, with A05 requiring both `projectedDistanceAlongAxis` (pinned board stretch) and `projectedAngleAboutAxis` (pinned hinge/lever angle). No guest JS runs on pointer movement. Add constraint×regeneration handling (A25), rotated-frame correctness (A45), Build/Edit/Use separation (A44), independent-revision undo (A56), concurrent/cancelled interaction intents, and device-neutral synthetic controller semantics. Prove generic observations/custom events/compound predicates, bounded feedback/cancellation, pause/resume/delete behavior, and cross-entity behavior references/grants. A12/A28/A29 records must name the observation/event APIs rather than the demo rule. M1–M2 continue to forbid guest-to-guest executable imports. Reusable assembly library export remains M4/A14, not an M2 claim.

The allowed M2 claim is: **Packages can declare and use the tested host constraint operators and define live behavior from generic observations/events while direct editing remains authoritative and model-free.** Passing A05 does not establish spline/cage/multi-pin physics or a universal CAD solver.

**M3: Private Coda/OpenCode authoring.** Bundle and isolate the Workspace-private OpenCode runtime, expose an accurate engine/provider/model selector, connect a user-selected available provider, and have Coda create/revise packages through the same draft, compile, validation, command, descriptor, and activation contracts used by manual packages. Prove private-vs-personal runtime isolation, quota/auth failure behavior, prompt/context injection resistance, model prose not substituting for host activation acknowledgement, provider/model independence, and two concurrent authoring-job reconciliation (A58). Generated package code still never executes inside OpenCode's credential-bearing process.

The allowed M3 claim is: **Coda/OpenCode can invent and revise packages through the proven runtime contracts under the tested authoring and isolation boundaries.** It is not a privileged world mutation path.

**M4: Product integration, migration, Windows authority, import/export, and voice.** Port real application surfaces, capture/input, docking, host capabilities, launcher recovery, compatible user-state migration, remembered-grant/revocation semantics, assembly import/export, uncertain external-action reconciliation, and voice into the new composition. Complete A34's grant-upgrade fixture here and A51 revocation. Validate one full installed native flow with no repository checkout or globally installed OpenCode. Evaluate and let the user select the new voice independently. A14 reusable assembly/library export belongs here.

The allowed M4 claim is: **The proven live-runtime architecture is integrated with the real Windows product, migration/import/export, remembered authority policy, and selected voice path for the accepted scenarios.**

**M5: Expansion and VR.** Expand package libraries, high-scale instancing/identity, richer package ecosystems, advanced dependency/import models, advanced graphics profiles, physics/solver operators, and VR adapters through their contracts. A54 gates any future guest-to-guest executable import model. VR hardware and advanced renderer profiles each have dedicated acceptance gates. They are not implied by setting `renderer.xr.enabled`.

## 19. Required acceptance evidence

An acceptance scenario proves only the capability explicitly named by that scenario. A representative object does not establish generality unless the passing record identifies and exercises the generic mechanism responsible for the capability. The passing record must name that mechanism, such as an SDK resource kind, observation API, constraint declaration/operator, world command, write-scope rule, or package lifecycle operation. A demo name such as "board", "fire", or "switch" is never itself the mechanism.

Schema/property/contract tests support these scenarios. They do not replace installed runtime, security, persistence fault-injection, GPU, or audible-voice checks. Every accepted gate records the command/input fixture, generic mechanism exercised, runtime versions, environment/process topology, observed output, and limitations.

### M1: runtime boundary and creative-resource evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A01 | Move an anchor in midair, create an object there | Exact pose preserved; marker display size does not distort object dimensions |
| A02 | Draw a colored line between arbitrary 3D points | Position/color match saved values; no floor-plane assumption; record the line/resource descriptor kind rather than treating the line as general rendering proof |
| A03 | Drag while a delayed package revision completes | Same entity ID and latest user pose; no snap-back; implementation replacement does not republish stale root placement |
| A04 | Change color while another independent field changes | Independent revision-plane update succeeds; genuinely stale dependent update is rejected |
| A06 | Change/delete a grabbed subpart during reload | Safe deferral or explicit re-selection; no handle jump and no stale primitive-index binding |
| A07 | Replace a valid package with malformed source | Prior revision and world stay usable; candidate resources cleaned; active generation unchanged |
| A08 | Run a looping/allocation-flood package | Guest execution/allocation budgets terminate or quarantine it; trusted recovery remains available according to recorded process topology |
| A09 | Attempt unauthorized file/process/network/native access from guest code | Denial observed at the actual guest/host boundary; no external side effect |
| A10 | Forge another package's resource or actor identity | Rejected by host/runtime ownership checks; actor/generation comes from authenticated channel, not payload |
| A11 | Reload while an old timer/callback is pending | Retired-generation effects rejected by generation token after replacement/reload |
| A13 | Save, crash at package-publication boundaries, reopen | Last committed compatible state; no missing active blobs or half revisions |
| A15 | Remove and reload packages repeatedly | Resource counts return to bounded baseline; no accumulating listeners, guest generations, buffers, textures, or streams |
| A22 | Request unsupported addon/native capability | Explicit compatibility result; no success claim, implicit `three` import, or covert privileged execution |
| A23 | Model-offline ordinary editing | Agents stopped, providers logged out, network blocked; grab/move/rotate/ordinary parameter edit/undo/save/reopen succeed and no model RPC is attempted |
| A24 | Nested parent/child authority | Move/grab child while parent package revises; move parent while child revises; IDs and child-local pose remain correct with no ancestor/descendant snap-back |
| A26 | Package/instance operation separation and migration | Disable remains recoverable; duplicate instance gets a new instance ID with same package revision; fork gets a new package identity/lineage; copy-parameters preserves identities; rollback to N-2 keeps independent user transforms; good migration succeeds and bad migration quarantines while prior compatible state remains |
| A30 | Stale publish after delete or lease expiry | Delete target or expire relevant lease while an authoring/result job is pending; late result cannot resurrect, bind-by-name, or overwrite expired state |
| A31 | Draft isolation from product and credential process | Authoring attempts writes outside its draft (`apps/`, `.git`, auth/config stores) and attempts to execute generated world code in OpenCode/Codex process; denied, sentinels intact, execution only through guest pipeline |
| A32 | Compile/toolchain hook denial | Package includes `postinstall`, lifecycle scripts, or unapproved build plugin; compile/publish pipeline refuses them with no side effect and prior revision stays active |
| A33 | Host IPC / WebView forgery | Forged messages claim privileged origin/actor/method; host rejects them and derives actor/generation from authenticated channel |
| A34 | Narrow M1 capability-widening fixture | Candidate revision requests an unavailable/ungivable filesystem/network/process/native capability; it cannot activate as if granted and the prior revision remains usable. Remembered-grant upgrade policy is completed in M4 |
| A35 | Shader/GPU exhaustion and context/device loss | Pathological shader/resource flood is attributed where possible; if trusted stop remains responsive it can quarantine the offender; committed world state survives renderer/co-located-process loss and reconstructs from disk; recovery grants no additional authority |
| A36 | Trusted recovery and no self-approval | Package renders fake approval/recovery UI and floods its own view; fake UI grants nothing and trusted pause/disable remains outside package authority and model dependence |
| A42 | M1 candidate activation half | Stream/prepare half-written source and valid progressive previews; incomplete source never becomes active, and fixture completion is accepted only after host publication/activation acknowledgement. Model-prose completion is tested in M3 |
| A43 | Local procedural motion versus root authority | Scheduled package-local particle/procedural updates continue while user moves root; root follows user, local motion stays local, guest tick cannot write leased root fields, ephemeral trajectories are not falsely persisted |
| A47 | Positive creative-breadth descriptor matrix | Manually authored packages exercise and reload: indexed geometry + attributes; curve beyond A02; custom shader + uniforms; texture from host asset handle; light; procedural/point-buffer update; instanced draw path. Record the SDK descriptor kind for each. Unsupported broader Three.js APIs remain A22, and large-group identity remains A49 |
| A50 | Confused-deputy descriptor denial | Guest places filesystem/network/traversal locators in texture/model/font/audio/shader-include or future asset descriptor paths; trusted pipeline accepts only host asset handles/validated package blobs and performs no forbidden fetch |
| A52 | Crash/save during active manipulation | During an uncommitted drag/deformation, crash/disconnect/reload restores last host-accepted checkpoint, drops the lease, discards preview, and creates no duplicate undo operation |
| A53 | Structural durable-reference fixture | Parent/child or subpart reference survives rename, package revision/rollback, and topology regeneration by stable identity/key; deletion follows declared missing-reference/cascade policy and never retargets by name/digest/proximity. Live-rule cross-entity fixture is completed in M2 |
| A57 | Pick/hit identity after regeneration | After topology/geometry regeneration, selecting "that" resolves current semantic instance/subpart key; stale triangle/primitive IDs do not become authority and missing handles require explicit re-selection |

### M2: constraint, interaction, and live-behavior evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A05 | Two declarative constraint mappings | Fixture 1: pinned board uses `projectedDistanceAlongAxis` to drive length while pin/thickness remain correct and undo restores shape. Fixture 2: pinned hinge/lever uses `projectedAngleAboutAxis` to drive bounded angle. Both publish only handles/axes/limits/parameter IDs/operator ID; record declaration contract + operator ID; no guest JS/eval/per-frame callback |
| A12 | Add a crossing/time rule and modify it live | Correct edge/timer semantics and no duplicate storm; record generic observation/timer APIs, not a named host trigger |
| A21 | Send desktop and synthetic controller manipulation intents | Same begin/update/end/cancel host command semantics; hardware VR acceptance remains separate |
| A25 | Constraint drag under live regeneration | Pin/stretch while package hot-replaces relevant geometry; stable handle mapping, pin holds, current user parameter wins, and no cursor/hand jump |
| A27 | Behavior feedback and cancellation | A arms B and B arms A; cancellation mid-cascade and same-tick ordering remain bounded/deterministic with no runaway recursion |
| A28 | Package-defined compound predicate | "Cross plane while rotating clockwise" is built from granted pose/crossing/angular-rate observations in package logic; one direction-sensitive edge fire, survives reload, works with agents offline, no named host trigger added |
| A29 | Namespaced custom events | Package R emits namespaced/schema-versioned event and S subscribes; no central enum change; incompatible event schema revision is rejected/quarantined safely |
| A37 | Concurrent intents and mid-constraint cancellation | Two synthetic grips/intent streams plus tracking-loss/cancel during active edit leave no stuck lease; conflict/cancel/commit semantics are explicit and no silent handle rebind occurs |
| A39 | Pause/resume and deletion revoke | Time/rule behavior follows declared pause/resume semantics; deleting target/owner revokes queued work/subscriptions so nothing fires after deletion |
| A44 | Build/Edit versus Use and camera ownership | Operating a switch in Use and manipulating it in Edit do not cross-fire; package cannot switch mode/selection/camera and camera capture never steals active drag |
| A45 | Frame words after rotation and invalid parent/frame data | Pin/constraint declared in a frame, rotate object 90°, then edit; declared local/world frame meaning remains stable; cyclic/non-finite/non-invertible transforms are rejected |
| A53 | Live-rule cross-entity reference fixture | "This switch controls that wall" stores durable target identity plus explicit allowed field/action grant; rename/revision/rollback does not retarget; missing target follows declared policy and no guessed replacement is used |
| A56 | Undo versus independent revision planes | Constraint/parameter operation followed by independent placement/implementation change; undo touches only recorded planes or reports conflict, never rewinds unrelated newer state |

### M3: Coda/OpenCode authoring evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A16 | Author with private OpenCode beside a personal install | Workspace-private executable/config/data/auth/session paths used; personal sentinel files/config remain unchanged |
| A17 | Provider unauthenticated, quota-limited, or absent | Accurate state; direct/manual package editing and world interaction remain usable; no unapproved paid/provider fallback |
| A42 | M3 model-completion half | Model/agent emits prose or stream claiming "done" before/without host activation; Coda reports success only after matching host acknowledgement for the intended candidate generation |
| A46 | Engine/provider/model independence | Change OpenCode provider/model without changing engine; direct Codex/xAI remain distinct; unavailable Cursor is refused rather than silently routed to Codex |
| A55 | Prompt/context injection isolation | Imported asset/package/file/object metadata contains adversarial instructions; context labels it as untrusted data, no grant/tool/system instruction changes, and authoring stays in scoped draft |
| A58 | Two authoring jobs | Two concurrent drafts target overlapping package/entity work; each retains own base/revisions/lease/cancel state, publication revalidates the other, and last-arriving prose cannot silently clobber accepted newer work |

### M4: product integration, migration, Windows authority, import/export, and voice evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A14 | Export/import a reusable assembly | Stable semantic relationships and appropriate new instance/package IDs on import; no credentials/grants transferred; Workspace export preserves richer behavior/state while remaining distinct from GLB |
| A18 | Request a Windows action from a preview/reloaded rule | Preview/replay cannot perform it; only live authorized request follows existing confirmation semantics and replay does not repeat past effects |
| A19 | Test a real Windows surface in installed native build | Launch/capture/input/dock verified in exact installed artifact; provenance tied to commit/runtime and no globally installed OpenCode required |
| A20 | Switch/fail speech output | Typed interaction remains usable; cancellation works; exact engine/voice displayed; audible owner evaluation remains separate from unit tests |
| A34 | M4 remembered-grant upgrade fixture | Existing package has a narrow remembered grant; revision widens target/action/capability scope; new scope does not auto-inherit authority and requires the configured fresh review/approval path while prior revision can remain active |
| A40 | Uncertain external action and undo | Ambiguous launch/action outcome is reconciled before retry; no duplicate side effect; undo of world state does not claim to reverse an external process/action |
| A41 | Adversarial import, missing package, old schema/SDK | Traversal/symlink/oversize/missing blob/unsupported state or SDK fixtures reject, quarantine, or create recoverable placeholders as specified; no credential import or incompatible migration execution |
| A51 | Capability revocation while active | Revoke remembered capability with active rule and queued requests; new requests fail immediately, queued-undispatched operations are cancelled, stale cached grant is unusable, already-observed external outcomes remain reported accurately |

### M5: expansion evidence

| ID | Scenario | Passing evidence |
| --- | --- | --- |
| A38 | Offender quarantine among many packages | Multiple healthy packages plus one CPU/resource offender; only offender is quarantined where isolation permits, healthy package state remains, and resource attribution is visible |
| A48 | GLB honesty versus Workspace export | Same creation exported both ways; GLB is explicitly geometry/material exchange and does not claim behavior/permission fidelity; Workspace export/reimport preserves the supported rich representation |
| A49 | Instanced draw with per-instance semantic identity at scale | Large instanced group remains efficient while selecting one/matching selected peers preserves logical instance IDs and overrides according to declared scale limits |
| A54 | Dependency-version isolation before guest-to-guest imports | M1–M2 import attempts between guest packages are rejected. Before any future executable guest dependency sharing is enabled, incompatible dependency-version fixtures prove reproducible isolated resolution; no global npm-style environment silently mutates another package |

### Milestone claim discipline

After M1, Workspace may claim a persistent, manipulable, model-independent package runtime with the tested descriptor/resource breadth, authoritative state, isolation, and recovery. It may not claim general constraints or AI authoring.

After M2, Workspace may additionally claim the tested declarative constraint operators and package-defined live behavior mechanisms. It may not claim a universal CAD/physics solver.

After M3, Workspace may claim Coda/OpenCode package invention through the tested isolation and authoring contracts. The agent still has no privileged world mutation path.

After M4, Workspace may claim the tested Windows-product integration, migration/import/export, remembered authority, and voice paths.

M5 and later claims remain bounded by their own accepted profiles. Passing any representative visual or interaction never upgrades an unsupported renderer API, solver family, dependency model, or hardware path into a supported feature by implication.

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