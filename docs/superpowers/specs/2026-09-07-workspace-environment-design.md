# Workspace Environment — Architecture Design

Date: 2026-09-07  
Status: Approved for V0 implementation
Initial platform: Windows 10/11 host, desktop Three.js client, Quest 2 WebXR path

## 1. Product intent

Workspace Environment is a spatial computing layer for a real Windows PC. The PC remains the authoritative computer. Applications, files, folders, projects, terminals, processes, devices, and other host resources remain real Windows resources. The workspace gives those resources persistent semantic identity and spatial presentation.

The first experience is not a conventional level editor or a fake VR desktop. The user enters an intentionally sparse workplace and is welcomed into a place that will be built around how they work. Real PC resources can then be brought into the environment and arranged spatially.

The defining principle is:

> The computer remains reality. The workspace is a persistent spatial interpretation of that reality.

## 2. V0 proof

V0 proves one hard claim end to end:

> A real Windows application can become a persistent, live, interactive object inside the spatial workspace.

The first retained demonstration uses Microsoft Edge because it is widely available and exercises application discovery, launch, window discovery, capture, input routing, live rendering, spatial placement, and persistence.

### V0 acceptance flow

1. Start the Windows Workspace Host.
2. Start the desktop spatial client.
3. Enter the sparse workspace and receive the welcome sequence.
4. The host has already discovered Microsoft Edge as an installed application.
5. Invoke `open Microsoft Edge` through the local UI or host command surface.
6. The real Windows application launches.
7. The host discovers its real top-level window.
8. The host captures that window as a live surface.
9. The desktop Three.js client renders that surface in the workspace.
10. The user can focus the surface, click, type, and scroll in the real application.
11. The user can move and resize the spatial representation without changing its semantic identity.
12. The workspace stores the presentation state.
13. Restart the workspace.
14. The same application entity resolves again and its prior spatial placement is restored.

No fake browser, screenshot-only demo, or application-specific Edge integration satisfies V0.

## 3. Architectural rule: authority and clients

The Windows Workspace Host is authoritative. Spatial renderers are replaceable clients.

### Workspace Host owns

- installed application inventory
- running applications and processes
- top-level windows
- file and folder identities
- project/work-context identities
- application/window capture sessions
- input routing to host resources
- workspace entities and semantic relationships
- host bindings
- durable presentation state
- operation history
- persistence
- capability exposure for agents and clients

### Spatial client owns

- geometry
- lighting
- camera and locomotion
- selection visuals
- application-surface presentation
- spatial interaction
- desktop navigation
- WebXR navigation
- onboarding presentation

The spatial client is never the source of truth for host resources or workspace identity.

## 4. Semantic object model

All meaningful things are represented as durable Workspace Entities.

A Workspace Entity describes:

- what the thing is
- what it is called
- what it is related to
- what can be done with it
- what real host resource it represents, when applicable
- how it is presented spatially

### Core fields

- `id` — durable workspace identity
- `kind` — semantic entity kind
- `name` — human-facing name
- `properties` — kind-specific semantic state
- `relationships` — typed edges to other entities
- `capabilities` — actions currently permitted on the entity
- `hostBinding` — optional descriptor for a real host resource
- `presentation` — spatial representation state

### Identity rule

Transient runtime handles are not durable identity.

Examples of transient state that must not become workspace identity:

- process IDs
- HWND values
- capture texture identifiers
- socket identifiers
- ephemeral stream identifiers

A durable entity may resolve to new transient Windows resources on each launch.

### Example semantic categories

Host-backed:

- `pc.application`
- `pc.window`
- `pc.process`
- `pc.file`
- `pc.folder`
- `pc.device`
- `workspace.project`
- `workspace.terminal`

Spatial-native:

- `spatial.wall`
- `spatial.floor`
- `spatial.light`
- `spatial.structure`
- `spatial.surface`

System-level:

- `workspace.place`
- `workspace.agent`
- `workspace.task`

### Application/window separation

An application and its windows are separate entities. One application may own multiple processes and windows. Window lifecycle must not destroy application identity.

## 5. Semantic verbs and capabilities

The workspace uses a small high-level vocabulary and refines meaning by entity capability.

Initial universal verbs:

- OPEN
- CREATE
- INSPECT
- REVISE
- MOVE
- CONNECT
- RUN
- STOP
- FOCUS
- HISTORY
- REMOVE

Examples:

- OPEN + application => launch or focus
- OPEN + file => open with an associated application
- OPEN + project => activate the project context
- MOVE + application window => move the spatial presentation
- RUN + project => run an available project action
- INSPECT + process => expose host process state

Agents should primarily reason from semantic identity, relationships, and capabilities rather than renderer coordinates or internal implementation details.

## 6. Relationship model

Coordinates describe rendering. Relationships describe workspace meaning.

Examples:

- Cursor `edits` Project A
- Terminal 1 `works-in` Project A
- Edge Window 2 `previews` Project A
- Edge Window 2 `displayed-on` Surface 4
- Surface 4 `mounted-on` North Wall
- North Wall `part-of` Coding Place

The renderer may use coordinates to realize these relationships, but agents and higher-level logic should prefer semantic relationships.

## 7. Windows Workspace Host

V0 uses C#/.NET for the Windows host because it provides a practical path to Win32 and WinRT APIs while keeping native operating-system concerns outside the TypeScript renderer.

Initial host modules:

- Application Discovery
- Process Manager
- Window Manager
- Window Capture
- Input Router
- Filesystem / Shell
- Project Context
- Workspace Entity Store
- Presentation Store
- Workspace Protocol Server
- MCP Adapter boundary

The host runs unelevated by default.

Any future privileged capability must be isolated behind a separate narrow helper with explicit authorization. The main Workspace Host must not run as Administrator merely to broaden compatibility.

## 8. Application discovery

The host builds a semantic inventory of installed applications on startup and refreshes it when appropriate.

An application descriptor may include:

- durable workspace application identity
- display name
- icon/reference
- executable path when applicable
- App User Model identity when applicable
- launch strategy
- running instances
- current capabilities

The user should be able to request an application by human-facing name without knowing executable paths.

No per-application plugin is required for basic launch, capture, presentation, and input.

## 9. Window discovery and lifecycle

The host maintains a model of visible top-level windows and associates them with applications/processes.

A window entity tracks semantic state such as:

- title
- owning application
- current process association
- visibility
- minimized state
- focus state
- capture eligibility
- presentation binding

The system must tolerate windows appearing, disappearing, being recreated, or changing process association.

## 10. Live application-surface pipeline

For a normal capturable window, the intended pipeline is:

`HWND -> Windows Graphics Capture -> Surface Stream -> Spatial Client -> Three.js texture`

The client sees an abstract `SurfaceStream`, not a Windows-specific capture implementation.

This separation allows the transport to evolve without changing semantic entities or the renderer contract.

### Initial transport direction

- Semantic state: versioned JSON over WebSocket
- Visual surfaces: transport abstraction from the beginning
- Long-term remote visual target: WebRTC-compatible low-latency media transport

A desktop-only development transport may be simpler initially if it is hidden behind the same `SurfaceStream` abstraction.

## 11. Input routing

Input on a spatial application surface maps back to the real Windows window.

Conceptual flow:

`spatial hit -> normalized surface coordinates -> WindowEntity -> host window coordinates -> Input Router -> real application`

Initial supported interaction:

- focus
- pointer movement
- primary/secondary click
- wheel/scroll
- keyboard input

Input must respect Windows integrity/security boundaries. The Workspace Host does not bypass elevated-window restrictions by elevating the entire process.

Later semantic UI inspection may use Windows accessibility/UI-automation capabilities, but that is not required for V0.

## 12. Host protocol

Host/client semantics use a versioned protocol independent of renderer internals.

Initial transport: WebSocket.

Initial message classes:

- command
- result
- event
- snapshot
- error

Representative events:

- ENTITY_CREATED
- ENTITY_UPDATED
- ENTITY_REMOVED
- APPLICATION_LAUNCHED
- WINDOW_DISCOVERED
- WINDOW_CLOSED
- FOCUS_CHANGED
- PRESENTATION_UPDATED
- TRANSACTION_COMMITTED

Representative commands:

- application.list
- application.launch
- entity.inspect
- entity.focus
- entity.setPresentation
- window.list
- window.focus
- workspace.undo
- workspace.redo

Every message includes an explicit protocol version.

## 13. Spatial client

The desktop client uses TypeScript + raw Three.js.

Initial boundaries:

- rendering
- interaction
- world-replica
- application-surfaces
- onboarding
- xr
- protocol-client

The client maintains a read model/replica of authoritative host state and submits operations back to the host.

### Renderer registry

Entity kinds map to renderer implementations, for example:

- `pc.window` -> ApplicationSurfaceRenderer
- `spatial.wall` -> WallRenderer
- `spatial.floor` -> FloorRenderer
- `workspace.project` -> ProjectRenderer

Semantic entities are therefore not coupled to one visual representation.

## 14. Spatial application surfaces

A host window is presented as a spatial object with:

- geometry
- live surface texture
- semantic entity identity
- focus state
- selection state
- input collider
- presentation transform
- physical/display dimensions

Changing its spatial size or position changes workspace presentation, not necessarily the underlying Windows desktop window bounds.

This lets a small desktop window become a large wall-mounted surface in VR without mutating the actual app layout unless explicitly requested.

## 15. Persistent workspace and places

The user has one persistent Workspace containing multiple persistent places/work contexts.

A place is a semantic context with spatial organization. It is not required to look like a room.

Possible places include:

- coding area
- research area
- project-specific environment
- table/workbench
- room
- warehouse bay
- outdoor space
- nonphysical spatial construction

Projects and tools may belong to or appear within places without changing their real host identity.

## 16. Persistence

Persist these categories separately:

### Workspace state

- places
- native spatial structures
- durable workspace organization

### Semantic state

- entity identities
- properties
- relationships
- durable capability metadata

### Presentation state

- position
- rotation
- size
- mounting/parent presentation
- representation choice

### Host bindings

- durable application identity
- file/folder path or stronger locator where available
- project root
- launch descriptors

Runtime-only Windows handles are re-resolved when the workspace starts.

For V0, persistence should favor inspectable local storage and avoid premature database complexity. A file-backed durable store is acceptable if its update semantics are safe and testable. The implementation plan will choose the smallest persistence mechanism that satisfies atomicity and restart recovery for V0.

## 17. First-run experience

The first-run experience is part of the product.

The user enters an intentionally sparse spatial place instead of a dashboard or template picker.

Initial orientation:

> Welcome to your workspace environment.
>
> This is the place where we'll build the way you work.
>
> The applications, files, projects, and tools on your computer can exist here, but they do not have to look or behave like a traditional desktop.
>
> This space is intentionally unfinished.
>
> Look around.
>
> When you're ready, we'll start by bringing something from your computer into the workspace.

The orientation may refer to actual scene placement, such as instructing the user to turn around only when there is something intentionally placed behind the initial viewpoint.

The onboarding demonstrates the product by bringing a real host application into the world, not by explaining editor controls first.

## 18. Agent boundary

The foundation is not dependent on any model provider or paid model API.

Agents connect to the Workspace Host through a capability-oriented MCP adapter.

The same workspace may eventually be controlled by:

- Cursor
- ChatGPT
- Codex
- Claude
- local models
- future MCP-capable agents

The host exposes semantic operations, not arbitrary internal memory or unrestricted operating-system access.

Representative future MCP capabilities:

- workspace.inspect
- entity.inspect
- application.list
- application.launch
- window.list
- window.focus
- filesystem.search
- filesystem.open
- project.open
- terminal.create
- world.create
- world.move
- world.resize
- history.undo
- history.redo

MCP is an agent interface to the workspace. It is not the authority for workspace state.

## 19. Quest/WebXR path

Quest 2 is an explicit first XR target, but not required to complete the first desktop capture proof.

The spatial-client architecture must preserve a WebXR path from the start:

- the host remains authoritative
- the client uses semantic protocol messages independent of desktop controls
- application surfaces remain stream abstractions
- interaction coordinates are normalized before host routing
- XR locomotion/input is isolated from semantic entity logic

The intended next-stage flow is:

`Windows Workspace Host -> LAN -> Quest Browser/WebXR client`

Remote PC-rendered streaming is a later optimization, not the initial architectural foundation.

## 20. Repository structure

Initial monorepo shape:

```text
workspace-environment/
├── apps/
│   ├── host-windows/        # C#/.NET
│   └── spatial-client/      # TypeScript/Three.js
├── packages/
│   ├── protocol/
│   ├── world-schema/
│   └── shared-fixtures/
├── docs/
│   └── superpowers/
│       └── specs/
└── tools/
```

Implementation code is isolated by runtime/language. Shared meaning travels through contracts and fixtures rather than cross-language implementation coupling.

## 21. Explicit non-goals for V0

V0 does not include:

- generative room construction
- speech control
- AI model integration
- full filesystem visualization
- project visualization
- arbitrary remote Internet access
- full Quest authoring UX
- multiplayer
- avatars
- remote PCVR frame streaming
- application-specific plugins
- UI Automation semantic control trees
- elevated/admin application manipulation
- full world-builder operation grammar
- polished environment art

These are deliberately postponed until the PC/application/spatial-object claim is proven.

## 22. Testing strategy

The implementation plan must preserve testability at each boundary.

### Protocol

- schema validation
- version rejection
- command/result correlation
- malformed-message handling

### Semantic model

- durable identity survives transient handle changes
- application and window lifecycles remain separate
- relationships round-trip through persistence
- presentation changes do not mutate semantic identity

### Windows host

- application discovery fixtures
- window classification fixtures
- lifecycle reconciliation
- capture-session ownership
- input coordinate mapping
- persistence recovery

### Spatial client

- world replica applies ordered host events
- renderer registry selects expected renderer
- surface transforms round-trip correctly
- interaction converts hits to normalized coordinates

### End-to-end V0

- launch known application
- discover window
- display live surface
- route click/typing/scroll
- change spatial placement
- restart and restore placement

The end-to-end test may use a deterministic first-party test window in automation while Edge remains the human acceptance target.

## 23. Error handling

Failures must preserve the distinction between semantic identity and current host availability.

Examples:

- installed app missing: entity becomes unavailable rather than being deleted
- application launch fails: return explicit failed operation; keep workspace state intact
- window capture unavailable: show unavailable-surface state while preserving entity
- window closes: mark runtime window unavailable; do not destroy application identity
- host disconnect: spatial client freezes last safe replica as disconnected and disables mutating actions
- invalid protocol version: fail connection clearly rather than guessing compatibility
- persistence failure: operation must not be reported committed until durable state succeeds

## 24. Security posture

The workspace is powerful because it can operate the user's real PC. Therefore authority is explicit.

Principles:

- host runs unelevated by default
- agent access is capability-scoped
- host/client protocol does not imply arbitrary shell access
- privileged actions require separate explicit mechanisms
- remote connections are not enabled by default in V0
- future LAN/Quest access must include authenticated pairing
- destructive filesystem/process operations require deliberate capability design before exposure

V0 focuses narrowly on application launch, window capture, input, and presentation persistence.

## 25. Evolution path

After V0 succeeds, the next architectural slices are expected to be:

1. generalize real-app surfaces beyond Edge
2. add Quest 2 WebXR client against the same host
3. add richer PC semantics: files, folders, projects, terminals, processes
4. expose capability-oriented MCP control
5. build semantic world-editing operations
6. let an attached agent construct and revise the environment live
7. expand into persistent project places and contextual onboarding
8. investigate remote-rendered Quest client only when local client rendering becomes limiting

The system must not pre-build later features before the preceding boundary has proven itself.

## 26. Design invariants

These are the architectural rules that future implementation must preserve unless explicitly revised in a later design decision.

1. The Windows PC remains authoritative.
2. The workspace is a spatial interpretation of real host resources, not a duplicate computer.
3. Three.js is a client renderer, not the world database.
4. Workspace Entity identity outlives transient Windows handles.
5. Semantic relationships carry meaning; coordinates carry presentation.
6. Real applications work generically before any application-specific enhancement exists.
7. Application windows are separate entities from applications.
8. Presentation changes do not silently mutate underlying application state.
9. The ordinary host remains unelevated.
10. Agents operate through capabilities and do not become workspace authority.
11. No model-provider API is required for the foundation.
12. Quest is a client of the same workspace, not a separate version of the world.
13. Remote rendering is an optimization path, not the semantic foundation.
14. The first-run experience begins as a place, not as an editor dashboard.
15. V0 is complete only when a real Windows application is live and usable inside the spatial workspace.

## 27. Design approval history

Approved in conversation:

- Windows 10/11 first host target
- Windows Workspace Host as authority
- separate spatial clients
- real Microsoft Edge window as the first proof
- semantic Workspace Entity model
- durable identity separate from transient host handles
- relationships/capabilities as first-class semantics
- provider-independent agent boundary
- Quest 2/WebXR as the first XR path

Sections 3–25 were approved as a group in conversation on 2026-09-07. The design is approved for V0 implementation planning and execution.
