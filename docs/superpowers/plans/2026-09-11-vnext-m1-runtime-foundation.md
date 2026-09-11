# Workspace Environment vNext M1 Runtime Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the M1 vNext runtime foundation: a host-authoritative persistent world, isolated live package execution, a descriptor-only creative SDK projected by trusted Three.js, model-independent direct manipulation, revision-safe hot replacement, save/reload, and the M1 acceptance/security evidence required by A01–A04, A06–A11, A13, A15, A22–A24, A26, A30–A36, A42–A43, A47, A50, A52–A53, and A57.

**Architecture:** Build vNext alongside the existing prototype rather than refactoring the prototype in place. A new .NET 8 host owns World Core, command authorization, package publication, history, and SQLite persistence. A new Vite/Three.js client projects host state, owns trusted rendering and interaction, and supervises a QuickJS guest worker whose only output is validated descriptor/update data; guest code never imports Three.js or executes on the pointer path. M1 uses manually authored package fixtures and no model provider.

**Tech Stack:** .NET 8 / C# 12; `Microsoft.Data.Sqlite` 8.0.31; Node.js 24; TypeScript 5.9.2; Vite 8.2.2; Three.js 0.185.1 / `@types/three` 0.185.4; `quickjs-emscripten-core` 0.32.0; `@jitl/quickjs-singlefile-browser-release-sync` 0.32.0; `@jitl/quickjs-singlefile-mjs-release-sync` 0.32.0 for deterministic Node tests; Ajv 8.20.0; quicktype 26.0.0; Playwright Test 1.63.0; xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-09-11-vnext-live-creative-runtime-design.md`

## Global Constraints

- Start execution from the committed `vnext/architecture-foundation` line and create an isolated implementation worktree/branch named `vnext/m1-runtime-foundation`; do not implement directly on `main` or `pr/coda-grok-work`.
- Keep the existing prototype apps and packages intact unless a task explicitly modifies root build/workspace configuration. M1 is a side-by-side vNext proof.
- World Core is authoritative. Renderer state, QuickJS state, model output, WebView payload actor fields, triangle indices, HWND/PID values, and generated source literals are never durable authority.
- The host command vocabulary is closed. M1 may implement only commands already named by §4; no convenience back-door verbs.
- `history.undo`, `history.redo`, `workspace.save`, `reference.grant`, and `reference.revoke` are trusted-only. Guest code cannot invoke them.
- Three.js is trusted-renderer-only. `import "three"` in a guest must fail at every milestone.
- Guest source can import only the Workspace Creative SDK virtual module and package-local allowlisted dependencies. Guest-to-guest executable imports remain forbidden.
- Guest JavaScript never runs on pointer/controller movement. M1 direct transforms use trusted Interaction + host edit commands only. M2 constraint operators are not implemented in this plan.
- User placement wins over delayed package work. Package replacement swaps implementation resources under a stable entity root and cannot republish an old transform.
- Guest output is resource descriptors/updates, never World Core mutation commands. Guest world writes in M1 are limited to the explicitly mediated own-package `package.state.patch` path.
- Host owns guest lifecycle and generation tokens even when the worker physically runs beside the renderer.
- M1 proves single-package execution isolation and per-package budgets. It does not claim A38 multi-package/multi-tenant offender isolation.
- M1 uses no Codex, OpenCode, xAI, or other model calls. A31's real OpenCode credential-process half is a stub/sentinel fixture here and is re-proven with the real process in M3.
- Accepted world mutations are crash-safe SQLite transactions. `workspace.save` creates an explicit saved checkpoint of accepted state; it never persists local preview/lease state.
- M1 package source is JavaScript ES module source. TypeScript package authoring/bundling is not required until an authoring milestone needs it.
- No npm lifecycle scripts, package install hooks, arbitrary build plugins, external guest URLs, guest DOM, guest filesystem, guest process execution, guest network, or guest native bridge.
- Every task follows TDD: write the failing test, run it and observe the expected failure, implement the smallest passing behavior, rerun the focused tests, then run the relevant package/project suite before committing.

---

## File Structure Locked for M1

Create these vNext build units; do not rename the existing prototype projects during M1.

```text
Workspace.VNext.sln

contracts/
  schemas/
    vnext-envelope.schema.json
    creative-resource.schema.json
    world-package.schema.json
  fixtures/vnext/
    command-transform-set.json
    command-workspace-save.json
    descriptor-line.json
    descriptor-indexed-geometry.json
    package-valid.json
    package-forbidden-three.json

scripts/
  generate-vnext-contracts.mjs
  check-vnext-contracts.mjs

src/
  Workspace.Contracts/
    Workspace.Contracts.csproj
    Generated/
      VNextEnvelope.cs
      CreativeResource.cs
      WorldPackage.cs
  Workspace.Core/
    Workspace.Core.csproj
    World/
      EntityId.cs
      Vec3.cs
      Quaternion.cs
      TransformState.cs
      RevisionVector.cs
      PackageBinding.cs
      WorldRelationship.cs
      WorldEntity.cs
      WorldState.cs
    Commands/
      WorldCommandNames.cs
      CommandContext.cs
      WorldCommand.cs
      CommandResult.cs
      WorldEngine.cs
    History/
      HistoryEntry.cs
      HistoryPatch.cs
    Packages/
      PackageDefinition.cs
      PackageRevision.cs
      PackageDigest.cs
    Ports/
      IWorldStore.cs
      IRuntimeGateway.cs
  Workspace.Runtime/
    Workspace.Runtime.csproj
    Drafts/
      DraftWorkspace.cs
    Packages/
      PackageManifestPolicy.cs
      PackageCoordinator.cs
      PackageMigrationRunner.cs
  Workspace.Storage/
    Workspace.Storage.csproj
    Sqlite/
      SqliteWorldStore.cs
      SqliteSchema.cs
      StorageFaultPoint.cs

apps/
  host/
    Workspace.Host.csproj
    Program.cs
    Composition/VNextComposition.cs
    Protocol/SessionAuthenticator.cs
    Protocol/WorkspaceSocketEndpoint.cs
    Protocol/RendererRuntimeGateway.cs
  spatial/
    package.json
    tsconfig.json
    index.html
    src/main.ts
    src/host/HostConnection.ts
    src/runtime/RuntimeCoordinator.ts
    src/interaction/InteractionController.ts
    src/recovery/TrustedRecoveryControls.ts

packages/
  contracts/
    package.json
    src/index.ts
    src/generated/
      vnext-envelope.ts
      creative-resource.ts
      world-package.ts
  creative-sdk/
    package.json
    src/index.ts
    src/descriptors.ts
  creative-runtime/
    package.json
    src/index.ts
    src/guest/GuestProtocol.ts
    src/guest/GuestSupervisor.ts
    src/guest/QuickJsGuestEngine.ts
    src/guest/guest-worker.ts
    src/guest/sdk-module-source.ts
  spatial-runtime/
    package.json
    src/index.ts
    src/descriptors/DescriptorValidator.ts
    src/rendering/ResourceRegistry.ts
    src/rendering/ThreeResourceProjector.ts
    src/rendering/EntityRootRegistry.ts
    src/rendering/PickingResolver.ts
    src/assets/AssetResolver.ts

examples/world-packages/
  m1-line/
  m1-breadth/
  m1-procedural/
  m1-malformed/
  m1-forbidden-import/

tests/
  Workspace.Core.Tests/
  Workspace.Runtime.Tests/
  Workspace.Storage.Tests/
  Workspace.Host.Tests/
  acceptance/
    package.json
    playwright.config.ts
    helpers/start-state.ts
    m1-runtime.spec.ts
    m1-security.spec.ts
```

`apps/spatial-client`, `apps/host-windows`, `apps/desktop-native`, `packages/protocol`, and `packages/world-schema` remain reference/prototype code during M1.

---

### Task 1: Scaffold vNext build units and generate cross-language contracts

**Acceptance coverage:** establishes the typed boundary used by A10, A22, A33, A47, A50.

**Files:**
- Create: `Workspace.VNext.sln`
- Create: `src/Workspace.Contracts/Workspace.Contracts.csproj`
- Create: `src/Workspace.Core/Workspace.Core.csproj`
- Create: `src/Workspace.Runtime/Workspace.Runtime.csproj`
- Create: `src/Workspace.Storage/Workspace.Storage.csproj`
- Create: `apps/host/Workspace.Host.csproj`
- Create: `tests/Workspace.Core.Tests/Workspace.Core.Tests.csproj`
- Create: `tests/Workspace.Runtime.Tests/Workspace.Runtime.Tests.csproj`
- Create: `tests/Workspace.Storage.Tests/Workspace.Storage.Tests.csproj`
- Create: `tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj`
- Create: `contracts/schemas/vnext-envelope.schema.json`
- Create: `contracts/schemas/creative-resource.schema.json`
- Create: `contracts/schemas/world-package.schema.json`
- Create: `contracts/fixtures/vnext/*.json`
- Create: `scripts/generate-vnext-contracts.mjs`
- Create: `scripts/check-vnext-contracts.mjs`
- Create: `packages/contracts/package.json`
- Create: `packages/contracts/src/index.ts`
- Generate: `packages/contracts/src/generated/*.ts`
- Generate: `src/Workspace.Contracts/Generated/*.cs`
- Modify: `package.json`
- Modify: `package-lock.json`

**Interfaces:**
- Produces JSON wire types `VNextEnvelope`, `CommandRequest`, `CommandResultEnvelope`, `RuntimePrepare`, `RuntimePrepared`, `RuntimeActivate`, `RuntimeRetire`, `RuntimeFailed`.
- Produces creative descriptor union `CreativeResourceDescriptor` and update union `CreativeResourceUpdate`.
- Produces `WorldPackageManifestContract` with `packageId`, `name`, `stateSchemaVersion`, `entry`, `requestedCapabilities`, `assets`, and optional `lineageParentPackageId`.
- Later tasks must consume generated types; do not create parallel handwritten wire DTOs.

- [ ] **Step 1: Add the failing contract generation check**

Create `scripts/check-vnext-contracts.mjs` so it regenerates into a temporary directory and byte-compares committed generated files. The core assertion is:

```js
import { mkdtemp, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import { spawnSync } from 'node:child_process';

const temp = await mkdtemp(path.join(tmpdir(), 'workspace-contracts-'));
try {
  const result = spawnSync(process.execPath, ['scripts/generate-vnext-contracts.mjs', '--out', temp], {
    cwd: process.cwd(),
    encoding: 'utf8',
  });
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);

  const pairs = [
    ['packages/contracts/src/generated/vnext-envelope.ts', 'ts/vnext-envelope.ts'],
    ['packages/contracts/src/generated/creative-resource.ts', 'ts/creative-resource.ts'],
    ['packages/contracts/src/generated/world-package.ts', 'ts/world-package.ts'],
    ['src/Workspace.Contracts/Generated/VNextEnvelope.cs', 'cs/VNextEnvelope.cs'],
    ['src/Workspace.Contracts/Generated/CreativeResource.cs', 'cs/CreativeResource.cs'],
    ['src/Workspace.Contracts/Generated/WorldPackage.cs', 'cs/WorldPackage.cs'],
  ];

  for (const [committed, generated] of pairs) {
    const [a, b] = await Promise.all([
      readFile(committed, 'utf8'),
      readFile(path.join(temp, generated), 'utf8'),
    ]);
    if (a !== b) throw new Error(`stale generated contract: ${committed}`);
  }
} finally {
  await rm(temp, { recursive: true, force: true });
}
```

- [ ] **Step 2: Run the check and verify RED**

Run: `node scripts/check-vnext-contracts.mjs`

Expected: FAIL because `scripts/generate-vnext-contracts.mjs` and generated files do not exist.

- [ ] **Step 3: Add schemas and deterministic quicktype generation**

Pin `quicktype` to `26.0.0`. `scripts/generate-vnext-contracts.mjs` invokes local `quicktype` three times for TypeScript and three times for C#; use `--src-lang schema`, `--just-types` for TypeScript, `--framework SystemTextJson`, `--namespace Workspace.Contracts.Generated`, and stable top-level names. Normalize generated line endings to `\n` before writing.

The command envelope schema must forbid unknown command names by enumerating the §4 list. The creative descriptor schema must set `additionalProperties: false` on every descriptor object and must not contain URL/path fields for assets; asset-bearing descriptors use `assetHandle` only.

Add root scripts:

```json
{
  "vnext:contracts": "node scripts/generate-vnext-contracts.mjs",
  "vnext:contracts:check": "node scripts/check-vnext-contracts.mjs",
  "vnext:test:dotnet": "dotnet test Workspace.VNext.sln --configuration Release",
  "vnext:test:node": "npm test --workspace @workspace/vnext-contracts --workspace @workspace/creative-sdk --workspace @workspace/creative-runtime --workspace @workspace/spatial-runtime --workspace @workspace/vnext-spatial",
  "vnext:typecheck": "npm run typecheck --workspace @workspace/vnext-contracts --workspace @workspace/creative-sdk --workspace @workspace/creative-runtime --workspace @workspace/spatial-runtime --workspace @workspace/vnext-spatial",
  "vnext:build": "npm run build --workspace @workspace/vnext-spatial && dotnet build Workspace.VNext.sln --configuration Release"
}
```

Update root workspaces to include `apps/spatial` in addition to the existing workspaces.

- [ ] **Step 4: Generate and run the contract check**

Run:

```powershell
npm install
npm run vnext:contracts
npm run vnext:contracts:check
```

Expected: PASS with no generated diff.

- [ ] **Step 5: Build only the empty vNext solution/contracts**

Run: `dotnet build Workspace.VNext.sln --configuration Release`

Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add Workspace.VNext.sln contracts scripts package.json package-lock.json packages/contracts src/Workspace.Contracts src/Workspace.Core src/Workspace.Runtime src/Workspace.Storage apps/host tests

git commit -m "build(vnext): establish contracts and project boundaries"
```

---

### Task 2: Implement host-authoritative World Core, revision planes, trusted history, and structural identity

**Acceptance coverage:** A03, A04, A10, A23, A24, A53.

**Files:**
- Create: `src/Workspace.Core/World/*.cs`
- Create: `src/Workspace.Core/Commands/*.cs`
- Create: `src/Workspace.Core/History/*.cs`
- Create: `src/Workspace.Core/Ports/IWorldStore.cs`
- Test: `tests/Workspace.Core.Tests/WorldEngineTests.cs`
- Test: `tests/Workspace.Core.Tests/HistoryTests.cs`
- Test: `tests/Workspace.Core.Tests/HierarchyTests.cs`

**Interfaces:**

```csharp
public enum RevisionPlane { Transform, Parameters, Relationships, Implementation, PackageState }

public sealed record RevisionVector(
    long Transform,
    long Parameters,
    long Relationships,
    long Implementation,
    long PackageState);

public sealed record CommandContext(
    string ActorId,
    string ActorKind,
    bool Trusted,
    string SessionId,
    string? PackageInstanceId,
    string? GenerationToken);

public abstract record WorldCommand(string RequestId, IReadOnlyDictionary<RevisionPlane, long> Expected);

public interface IWorldStore
{
    Task<WorldState> LoadAsync(CancellationToken cancellationToken);
    Task PersistAcceptedAsync(WorldState state, HistoryEntry? history, CancellationToken cancellationToken);
    Task SaveCheckpointAsync(WorldState state, CancellationToken cancellationToken);
}

public sealed class WorldEngine
{
    public WorldState Current { get; }
    public ValueTask<CommandResult> ExecuteAsync(WorldCommand command, CommandContext context, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing tests for independent revision planes and stale transform rejection**

```csharp
[Fact]
public async Task Parameter_change_does_not_conflict_with_independent_transform_change()
{
    var engine = TestWorld.CreateWithBox("entity:box", transformRevision: 4, parameterRevision: 7);
    var move = new TransformSetCommand("m1", "entity:box", TestPose.At(2, 0, 0), new() { [RevisionPlane.Transform] = 4 });
    var color = new ParametersPatchCommand("p1", "entity:box", new() { ["color"] = Json("red") }, new() { [RevisionPlane.Parameters] = 7 });

    Assert.True((await engine.ExecuteAsync(move, TestActor.User, default)).Accepted);
    Assert.True((await engine.ExecuteAsync(color, TestActor.User, default)).Accepted);
}

[Fact]
public async Task Stale_transform_cannot_overwrite_newer_user_move()
{
    var engine = TestWorld.CreateWithBox("entity:box", transformRevision: 4);
    Assert.True((await engine.ExecuteAsync(
        new TransformSetCommand("user", "entity:box", TestPose.At(9, 0, 0), new() { [RevisionPlane.Transform] = 4 }),
        TestActor.User, default)).Accepted);

    var stale = await engine.ExecuteAsync(
        new TransformSetCommand("agent", "entity:box", TestPose.At(1, 0, 0), new() { [RevisionPlane.Transform] = 4 }),
        TestActor.TrustedCoda, default);

    Assert.False(stale.Accepted);
    Assert.Equal("revision_conflict", stale.ErrorCode);
    Assert.Equal(9, engine.Current.Entities["entity:box"].Transform.Position.X);
}
```

- [ ] **Step 2: Run focused tests and verify RED**

Run: `dotnet test tests/Workspace.Core.Tests/Workspace.Core.Tests.csproj --filter "FullyQualifiedName~WorldEngineTests"`

Expected: FAIL because World Core types do not exist.

- [ ] **Step 3: Implement IDs, transforms, hierarchy, revision vectors, relationships, and command records**

Use opaque `EntityId` values for new vNext entities; preserve imported prototype IDs exactly. `WorldEntity` must carry `ParentId`, `Transform`, `Parameters`, `Relationships`, `PackageBinding`, and `RevisionVector`. `WorldRelationship` stores durable `TargetId`, relation type, missing-target policy, and optional write-grant ID; never store display-name lookup as identity.

- [ ] **Step 4: Implement `WorldEngine.ExecuteAsync` with explicit command dispatch**

Use a closed `switch` on typed command classes. Unknown command names must fail in protocol parsing before reaching the engine. Every accepted command returns a new immutable `WorldState`, increments only affected revision planes, creates a scoped `HistoryEntry`, and calls `IWorldStore.PersistAcceptedAsync` before publishing `Current`.

Core commit rule:

```csharp
var persisted = await _store.PersistAcceptedAsync(next, history, cancellationToken)
    .ContinueWith(_ => next, cancellationToken, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
_current = persisted;
return CommandResult.AcceptedResult(_current, history?.OperationId);
```

Do not update `_current` before persistence succeeds.

- [ ] **Step 5: Add hierarchy and structural-reference tests**

Test parent move + child local transform preservation, child revision independent from parent implementation revision, rename preserving relationship target, and delete applying explicit `missing` policy rather than retargeting.

- [ ] **Step 6: Add trusted `history.undo` / `history.redo` tests**

Guest/untrusted context must return `forbidden_trusted_command`. A transform undo must restore only transform fields. A parameter undo must restore only parameter fields. Redo must fail after a new divergent mutation clears that redo branch.

- [ ] **Step 7: Run suite and commit**

Run: `dotnet test tests/Workspace.Core.Tests/Workspace.Core.Tests.csproj --configuration Release`

Expected: PASS.

```bash
git add src/Workspace.Core tests/Workspace.Core.Tests
git commit -m "feat(vnext): add host-authoritative world engine"
```

---

### Task 3: Add crash-safe SQLite persistence, immutable blobs, explicit save checkpoints, and fault injection

**Acceptance coverage:** A13, A23 persistence half, A52 persistence half.

**Files:**
- Modify: `src/Workspace.Storage/Workspace.Storage.csproj`
- Create: `src/Workspace.Storage/Sqlite/SqliteSchema.cs`
- Create: `src/Workspace.Storage/Sqlite/StorageFaultPoint.cs`
- Create: `src/Workspace.Storage/Sqlite/SqliteWorldStore.cs`
- Test: `tests/Workspace.Storage.Tests/SqliteWorldStoreTests.cs`
- Test: `tests/Workspace.Storage.Tests/PublicationCrashTests.cs`

**Interfaces:**

```csharp
public interface IStorageFaultInjector
{
    void Hit(StorageFaultPoint point);
}

public enum StorageFaultPoint
{
    AfterBlobInsert,
    BeforeStateCommit,
    AfterStateCommit
}
```

Pin `Microsoft.Data.Sqlite` `8.0.31` exactly in `Workspace.Storage.csproj`.

- [ ] **Step 1: Write the failing reopen test**

```csharp
[Fact]
public async Task Accepted_transform_survives_store_reopen()
{
    await using var temp = new TempSqliteStore();
    var store = await temp.OpenAsync();
    var state = TestWorld.StateWithBoxAt(3, 4, 5);
    await store.PersistAcceptedAsync(state, history: null, default);
    await store.DisposeAsync();

    await using var reopened = await temp.OpenAsync();
    var loaded = await reopened.LoadAsync(default);
    Assert.Equal(3, loaded.Entities["entity:box"].Transform.Position.X);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/Workspace.Storage.Tests/Workspace.Storage.Tests.csproj --filter "FullyQualifiedName~SqliteWorldStoreTests"`

Expected: FAIL because the store does not exist.

- [ ] **Step 3: Implement schema and transaction behavior**

Use SQLite WAL, foreign keys, and full synchronous durability:

```sql
PRAGMA journal_mode=WAL;
PRAGMA synchronous=FULL;
PRAGMA foreign_keys=ON;

CREATE TABLE IF NOT EXISTS world_state (
  singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
  world_revision INTEGER NOT NULL,
  document_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS explicit_checkpoints (
  id TEXT PRIMARY KEY,
  world_revision INTEGER NOT NULL,
  created_utc TEXT NOT NULL,
  document_json TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS package_blobs (
  digest TEXT PRIMARY KEY,
  media_type TEXT NOT NULL,
  content BLOB NOT NULL
);

CREATE TABLE IF NOT EXISTS package_revisions (
  digest TEXT PRIMARY KEY,
  package_id TEXT NOT NULL,
  manifest_json TEXT NOT NULL,
  source_digest TEXT NOT NULL,
  created_utc TEXT NOT NULL
);
```

World documents hold active package/instance references; blobs/revisions are immutable. All accepted command persistence runs in transactions. `workspace.save` inserts an explicit checkpoint row for the current accepted state and issues `PRAGMA wal_checkpoint(FULL)` after the transaction.

- [ ] **Step 4: Add publication fault tests**

Inject failure at `AfterBlobInsert` and `BeforeStateCommit`; reopen must resolve the old active revision and no active reference may point at a missing blob. An unreferenced blob may remain and is acceptable for later GC.

- [ ] **Step 5: Add `workspace.save` during preview test port**

At storage/Core level, simulate accepted transform revision 3 plus unpersisted UI preview revision. Call `workspace.save`; assert checkpoint contains revision 3 only. The browser drag path is completed in Task 9.

- [ ] **Step 6: Run and commit**

Run: `dotnet test tests/Workspace.Storage.Tests/Workspace.Storage.Tests.csproj --configuration Release`

Expected: PASS.

```bash
git add src/Workspace.Storage tests/Workspace.Storage.Tests
git commit -m "feat(vnext): persist accepted world state transactionally"
```

---

### Task 4: Implement package drafts, immutable revisions, migrations, lifecycle operations, and narrow capability policy

**Acceptance coverage:** A07, A13, A26, A30, A31 draft half, A32, A34, A42 candidate half.

**Files:**
- Create: `src/Workspace.Core/Packages/*.cs`
- Create: `src/Workspace.Runtime/Drafts/DraftWorkspace.cs`
- Create: `src/Workspace.Runtime/Packages/PackageManifestPolicy.cs`
- Create: `src/Workspace.Runtime/Packages/PackageCoordinator.cs`
- Create: `src/Workspace.Runtime/Packages/PackageMigrationRunner.cs`
- Create: `src/Workspace.Core/Ports/IRuntimeGateway.cs`
- Test: `tests/Workspace.Runtime.Tests/DraftWorkspaceTests.cs`
- Test: `tests/Workspace.Runtime.Tests/PackagePolicyTests.cs`
- Test: `tests/Workspace.Runtime.Tests/PackageCoordinatorTests.cs`
- Test: `tests/Workspace.Runtime.Tests/PackageMigrationTests.cs`

**Interfaces:**

```csharp
public sealed record PackageCandidate(
    string CandidateId,
    string PackageId,
    string RevisionDigest,
    string GenerationToken,
    string Source,
    string ManifestJson,
    int BaseImplementationRevision);

public interface IRuntimeGateway
{
    Task<RuntimePrepareResult> PrepareAsync(PackageCandidate candidate, CancellationToken cancellationToken);
    Task ActivateAsync(string entityId, string revisionDigest, string generationToken, CancellationToken cancellationToken);
    Task RetireAsync(string generationToken, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write failing manifest policy tests**

```csharp
[Theory]
[InlineData("filesystem.read")]
[InlineData("network.fetch")]
[InlineData("process.exec")]
[InlineData("native.bridge")]
public void M1_rejects_ungivable_capabilities(string capability)
{
    var result = PackageManifestPolicy.Validate(TestManifest.WithCapability(capability));
    Assert.False(result.AllowedToActivate);
    Assert.Equal("capability_not_available_in_m1", result.ErrorCode);
}

[Fact]
public void Manifest_rejects_lifecycle_scripts_and_plugins()
{
    var manifest = TestManifest.Raw("""{"packageId":"pkg:x","scripts":{"postinstall":"pwsh evil.ps1"}}""");
    Assert.Equal("forbidden_manifest_field", PackageManifestPolicy.Validate(manifest).ErrorCode);
}
```

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/Workspace.Runtime.Tests/Workspace.Runtime.Tests.csproj --filter "FullyQualifiedName~PackagePolicyTests"`

Expected: FAIL.

- [ ] **Step 3: Implement `DraftWorkspace` path containment**

`DraftWorkspace` owns one directory under an injected state root. Resolve every requested relative path with `Path.GetFullPath(Path.Combine(root, relative))`, require `candidate.StartsWith(rootWithSeparator, OrdinalIgnoreCase)`, reject rooted paths, `..` escape, reparse points/symlinks, and writes to product/auth sentinels in tests. No draft API accepts an arbitrary destination root from guest/model content.

- [ ] **Step 4: Implement deterministic package digests and immutable publication**

Digest each content item separately with SHA-256, then hash the ordered tuple `(manifestDigest, sourceDigest, orderedAssetDigests)`. Normalize source line endings to LF before hashing. Store source as an immutable blob and package revision record through `IWorldStore`.

- [ ] **Step 5: Implement prepare → commit activation sequencing**

`PackageCoordinator.PublishAsync` must:

```text
1. load draft by trusted draft ID;
2. validate manifest + source policy;
3. store immutable candidate blobs;
4. call IRuntimeGateway.PrepareAsync(candidate);
5. if prepare fails, leave old revision active;
6. re-read current entity/package implementation revision;
7. reject if entity deleted / lease expired / base implementation stale;
8. persist package revision + entity binding atomically;
9. call ActivateAsync with the committed generation token;
10. retire the previous generation after activation acknowledgement.
```

Do not let `RuntimePrepared` or model prose commit state by itself.

- [ ] **Step 6: Implement A26 operations as separate commands**

Implement and test:
- `instance.duplicate`: new entity ID, same package revision, copied compatible instance parameters.
- `package.fork`: new package ID + lineage, no implicit rebinding of existing instances.
- `parameters.copy`: explicit source/target IDs, compatible parameter IDs only, identities unchanged.
- `package.disable`: entity remains identifiable, implementation inactive.
- `package.rollback`: bind target entity to an older immutable revision without changing independent transform revision.

Sequence them in this order in tests; do not bundle all behavior into one mega-command implementation.

- [ ] **Step 7: Implement pure migration runner**

The M1 migration runner evaluates only a migration function with JSON input/output and no world/SDK capability functions. Good migration output must validate against the candidate state schema. Throw/invalid output returns `migration_failed` and leaves previous revision/state active.

- [ ] **Step 8: Run and commit**

Run: `dotnet test tests/Workspace.Runtime.Tests/Workspace.Runtime.Tests.csproj --configuration Release`

Expected: PASS.

```bash
git add src/Workspace.Core src/Workspace.Runtime tests/Workspace.Runtime.Tests
git commit -m "feat(vnext): add package publication and lifecycle"
```

---

### Task 5: Add authenticated host↔renderer protocol and derive actor/generation from the session

**Acceptance coverage:** A10, A33.

**Files:**
- Create: `apps/host/Program.cs`
- Create: `apps/host/Composition/VNextComposition.cs`
- Create: `apps/host/Protocol/SessionAuthenticator.cs`
- Create: `apps/host/Protocol/WorkspaceSocketEndpoint.cs`
- Create: `apps/host/Protocol/RendererRuntimeGateway.cs`
- Test: `tests/Workspace.Host.Tests/SessionAuthenticatorTests.cs`
- Test: `tests/Workspace.Host.Tests/WorkspaceSocketEndpointTests.cs`

**Interfaces:**

```csharp
public sealed record AuthenticatedSession(string SessionId, string ActorId, string ActorKind);

public sealed class SessionAuthenticator
{
    public string Issue();
    public bool TryConsume(string token, out AuthenticatedSession session);
}
```

- [ ] **Step 1: Write failing forged-actor test**

Send a valid authenticated envelope whose JSON body contains fake `actorId: "system"` and `generationToken: "other-package"`. Assert the `CommandContext` captured by a fake engine uses the authenticated session actor and the host-known generation mapping, never payload claims.

- [ ] **Step 2: Verify RED**

Run: `dotnet test tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --filter "FullyQualifiedName~WorkspaceSocketEndpointTests"`

Expected: FAIL.

- [ ] **Step 3: Implement loopback-only host and one-time session handshake**

Use ASP.NET Core/Kestrel on `127.0.0.1`. Production/dev start generates 32 random bytes with `RandomNumberGenerator.Fill`; encode Base64URL. The spatial URL receives the token only in the URL fragment so browsers do not send it in ordinary HTTP requests. First WebSocket message is `session.hello`; token is consumed once and replaced by server-side session identity.

A test-only `--acceptance --session-token <value>` mode is allowed. Reject `--session-token` without `--acceptance`.

- [ ] **Step 4: Validate every inbound envelope before dispatch**

Deserialize generated contract DTOs and reject unknown `type`, unknown `command`, extra properties, malformed request IDs, or wrong protocol version. The host creates `CommandContext`; never deserialize an actor context from the wire.

- [ ] **Step 5: Run and commit**

Run: `dotnet test tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release`

Expected: PASS.

```bash
git add apps/host tests/Workspace.Host.Tests
git commit -m "feat(vnext): authenticate renderer command sessions"
```

---

### Task 6: Build the descriptor-only Creative SDK and trusted Three.js projector

**Acceptance coverage:** A02, A22, A47, A50, A57 lower layer.

**Files:**
- Create: `packages/creative-sdk/package.json`
- Create: `packages/creative-sdk/src/index.ts`
- Create: `packages/creative-sdk/src/descriptors.ts`
- Create: `packages/spatial-runtime/package.json`
- Create: `packages/spatial-runtime/src/descriptors/DescriptorValidator.ts`
- Create: `packages/spatial-runtime/src/rendering/ResourceRegistry.ts`
- Create: `packages/spatial-runtime/src/rendering/ThreeResourceProjector.ts`
- Create: `packages/spatial-runtime/src/rendering/EntityRootRegistry.ts`
- Create: `packages/spatial-runtime/src/rendering/PickingResolver.ts`
- Create: `packages/spatial-runtime/src/assets/AssetResolver.ts`
- Test: matching `*.test.ts` files in each package

Pin: `three` `0.185.1`, `@types/three` `0.185.4`, `ajv` `8.20.0`.

**Interfaces:**

```ts
export type PackageResourceHandle = string & { readonly __packageResourceHandle: unique symbol };
export type HostAssetHandle = string & { readonly __hostAssetHandle: unique symbol };

export interface AssetResolver {
  resolve(handle: HostAssetHandle): Promise<{ width: number; height: number; rgba: Uint8Array }>;
}

export interface ThreeResourceProjector {
  applyBatch(owner: { entityId: string; generationToken: string }, batch: CreativeResourceDescriptor[]): Promise<void>;
  applyUpdate(owner: { entityId: string; generationToken: string }, update: CreativeResourceUpdate): void;
  retireGeneration(generationToken: string): void;
}
```

- [ ] **Step 1: Write failing confused-deputy validation tests**

```ts
for (const locator of [
  'file:///C:/Users/test/secret.png',
  'https://example.com/a.png',
  '..\\..\\secret.png',
  '\\\\server\\share\\image.png',
]) {
  test(`rejects locator ${locator}`, () => {
    const result = validateDescriptor({ kind: 'texture', id: 't', assetHandle: locator });
    assert.equal(result.ok, false);
  });
}
```

The valid fixture uses a host-issued opaque value such as `asset:sha256:...`, validated by syntax plus resolver ownership, not by treating any string as a fetch location.

- [ ] **Step 2: Verify RED**

Run: `npm test --workspace @workspace/spatial-runtime`

Expected: FAIL.

- [ ] **Step 3: Implement Creative SDK builders with no Three.js dependency**

Exports include builders for:
- indexed geometry + custom attributes;
- line;
- curve/path;
- shader material + JSON-safe uniforms;
- host asset texture;
- ambient/directional/point light;
- point buffer;
- instanced draw;
- group/local transform.

`packages/creative-sdk/package.json` must not depend on `three`.

- [ ] **Step 4: Implement Ajv descriptor validation and ownership registry**

`ResourceRegistry` keys resources by `(generationToken, packageLocalResourceId)`. A guest-provided ID can never directly address a resource in another generation. Updates resolve only through that ownership map.

- [ ] **Step 5: Implement Three.js projection without requiring WebGL for unit tests**

Map validated descriptors to `BufferGeometry`, `Line`, `CatmullRomCurve3`-sampled geometry, `ShaderMaterial`, `DataTexture`, light objects, `Points`, `InstancedMesh`, and `Group`. Dispose geometry/material/texture resources on retirement.

- [ ] **Step 6: Implement semantic picking resolution**

Every projected object carries trusted metadata assigned by `EntityRootRegistry`, not guest-controlled `userData`. `PickingResolver` walks from a Three.js raycast hit to the trusted root and returns `{ entityId, handleKey?, generationToken, implementationRevision }`. It never returns triangle index as durable identity.

- [ ] **Step 7: Run and commit**

Run:

```powershell
npm test --workspace @workspace/creative-sdk
npm test --workspace @workspace/spatial-runtime
npm run typecheck --workspace @workspace/creative-sdk
npm run typecheck --workspace @workspace/spatial-runtime
```

Expected: PASS.

```bash
git add packages/creative-sdk packages/spatial-runtime
git commit -m "feat(vnext): add descriptor-only creative renderer bridge"
```

---

### Task 7: Implement the QuickJS guest worker, module allowlist, budgets, and generation-token retirement

**Acceptance coverage:** A08, A09, A10 guest half, A11, A22, A31 execution half, A34 execution half, A43 scheduler primitive.

**Files:**
- Create: `packages/creative-runtime/package.json`
- Create: `packages/creative-runtime/src/guest/GuestProtocol.ts`
- Create: `packages/creative-runtime/src/guest/GuestSupervisor.ts`
- Create: `packages/creative-runtime/src/guest/QuickJsGuestEngine.ts`
- Create: `packages/creative-runtime/src/guest/guest-worker.ts`
- Create: `packages/creative-runtime/src/guest/sdk-module-source.ts`
- Test: `packages/creative-runtime/src/guest/*.test.ts`

Pin `quickjs-emscripten-core`, browser release-sync variant, and mjs release-sync variant at `0.32.0`.

**Interfaces:**

```ts
export interface GuestBudget {
  memoryLimitBytes: number;
  maxStackSizeBytes: number;
  deadlineMs: number;
  maxDescriptorsPerBatch: number;
  maxTransferredBytesPerBatch: number;
}

export interface PreparedGuest {
  generationToken: string;
  initialDescriptors: CreativeResourceDescriptor[];
  dispose(): void;
  tick(monotonicMs: number): CreativeResourceUpdate[];
}
```

- [ ] **Step 1: Write RED tests for forbidden globals/imports and interruption**

```ts
test('three import is rejected', async () => {
  await assert.rejects(
    () => engine.prepare(`import * as THREE from 'three'; export function start(){}`),
    /module_not_allowed:three/,
  );
});

test('filesystem network and process globals are absent', async () => {
  const result = await engine.evaluateProbe(`[
    typeof fetch, typeof process, typeof require, typeof WebSocket, typeof document
  ]`);
  assert.deepEqual(result, ['undefined', 'undefined', 'undefined', 'undefined', 'undefined']);
});

test('infinite loop is interrupted', async () => {
  await assert.rejects(() => engine.prepare(`while (true) {}`), /guest_interrupted/);
});
```

- [ ] **Step 2: Verify RED**

Run: `npm test --workspace @workspace/creative-runtime`

Expected: FAIL.

- [ ] **Step 3: Implement QuickJS runtime limits**

Create one QuickJS runtime per guest generation for M1. Apply `setMemoryLimit`, `setMaxStackSize`, and `setInterruptHandler` using a monotonic deadline. Set a module loader that returns source only for `@workspace/creative-sdk`; every other module name returns a deterministic `module_not_allowed:<name>` error.

- [ ] **Step 4: Implement the virtual SDK bridge**

Expose only host-created functions such as `__workspace_emitDescriptor`, `__workspace_emitUpdate`, and `__workspace_checkpointState`. The virtual module wraps them and exports frozen builders. Dump guest values into plain structured data, clone them, validate size, and pass them to the trusted descriptor validator. Never pass a QuickJS handle/function into the renderer.

- [ ] **Step 5: Implement worker supervision and retirement**

`GuestSupervisor.prepare` creates a disposable worker and candidate generation. `activate` marks exactly one generation active for an entity. `retireGeneration` terminates the worker and rejects any later message whose token is not currently active/candidate. Tests send a late timer/update after retirement and assert it is dropped.

- [ ] **Step 6: Implement trusted scheduled tick input**

The supervisor sends monotonic ticks from trusted JS outside QuickJS. Guest code may export `onTick(t)` and emit package-local resource updates. There is no guest timer global and no pointer event function. A slow tick can be skipped/terminated without blocking trusted interaction.

- [ ] **Step 7: Run and commit**

Run:

```powershell
npm test --workspace @workspace/creative-runtime
npm run typecheck --workspace @workspace/creative-runtime
```

Expected: PASS.

```bash
git add packages/creative-runtime package.json package-lock.json
git commit -m "feat(vnext): isolate live packages in bounded QuickJS workers"
```

---

### Task 8: Wire candidate preparation, staged resources, atomic activation, and cleanup across host and renderer

**Acceptance coverage:** A03, A07, A11, A15, A30, A42.

**Files:**
- Create: `apps/spatial/package.json`
- Create: `apps/spatial/tsconfig.json`
- Create: `apps/spatial/index.html`
- Create: `apps/spatial/src/host/HostConnection.ts`
- Create: `apps/spatial/src/runtime/RuntimeCoordinator.ts`
- Create: `apps/spatial/src/main.ts`
- Modify: `apps/host/Protocol/RendererRuntimeGateway.cs`
- Modify: `src/Workspace.Runtime/Packages/PackageCoordinator.cs`
- Test: `apps/spatial/src/runtime/RuntimeCoordinator.test.ts`
- Test: `tests/Workspace.Runtime.Tests/PackageActivationIntegrationTests.cs`

**Interfaces:**

```ts
export class RuntimeCoordinator {
  prepare(message: RuntimePrepare): Promise<RuntimePrepared | RuntimeFailed>;
  activate(message: RuntimeActivate): void;
  retire(message: RuntimeRetire): void;
}
```

- [ ] **Step 1: Write failing staged-swap test**

Prepare generation B while A is active. Assert B's Three.js group is not attached to the visible entity root until `RuntimeActivate(B)`. A remains visible if B preparation fails.

- [ ] **Step 2: Verify RED**

Run: `npm test --workspace @workspace/vnext-spatial`

Expected: FAIL.

- [ ] **Step 3: Implement staged candidate groups**

`RuntimeCoordinator` keeps `candidateGroups` detached from the visible scene. On host activation, replace only the implementation child under the stable entity root. Preserve root transform object and semantic root identity. Retire/dispose the old generation after swap.

- [ ] **Step 4: Implement generation-aware host gateway**

`RendererRuntimeGateway` correlates `candidateId` request/response pairs, times out preparation, and ignores prepared/failed responses from a different session or retired generation. It never commits host state; it only reports candidate readiness to `PackageCoordinator`.

- [ ] **Step 5: Add 100-cycle cleanup test**

Prepare/activate/retire a package 100 times with geometry/material/texture resources. Assert `ResourceRegistry.snapshotCounts()` returns to the baseline counts after each retirement and no worker remains alive.

- [ ] **Step 6: Run and commit**

Run:

```powershell
npm test --workspace @workspace/vnext-spatial
npm test --workspace @workspace/spatial-runtime
npm test --workspace @workspace/creative-runtime
dotnet test tests/Workspace.Runtime.Tests/Workspace.Runtime.Tests.csproj --configuration Release
```

Expected: PASS.

```bash
git add apps/spatial apps/host src/Workspace.Runtime packages/spatial-runtime packages/creative-runtime tests/Workspace.Runtime.Tests package.json package-lock.json
git commit -m "feat(vnext): hot-swap validated package generations"
```

---

### Task 9: Add trusted anchors, root manipulation, hierarchy projection, and semantic picking

**Acceptance coverage:** A01, A03, A04, A06, A23 interaction half, A24, A43, A52 interaction half, A53, A57.

**Files:**
- Create: `apps/spatial/src/interaction/InteractionController.ts`
- Modify: `packages/spatial-runtime/src/rendering/EntityRootRegistry.ts`
- Modify: `packages/spatial-runtime/src/rendering/PickingResolver.ts`
- Modify: `apps/spatial/src/main.ts`
- Test: `apps/spatial/src/interaction/InteractionController.test.ts`
- Test: `packages/spatial-runtime/src/rendering/EntityRootRegistry.test.ts`
- Test: `packages/spatial-runtime/src/rendering/PickingResolver.test.ts`

**Interfaces:**

```ts
export interface EditGateway {
  begin(entityId: string, fields: readonly string[], expectedTransformRevision: number): Promise<{ leaseId: string }>;
  commit(leaseId: string, transform: TransformContract): Promise<CommandResult>;
  cancel(leaseId: string): Promise<void>;
}
```

- [ ] **Step 1: Write failing no-snap-back test**

Create entity root at X=1, begin local drag to X=8, then simulate a package implementation swap. Assert the root remains X=8 and only the implementation child changes.

- [ ] **Step 2: Verify RED**

Run: `npm test --workspace @workspace/vnext-spatial -- --test-name-pattern="snap"`

Expected: FAIL.

- [ ] **Step 3: Implement Spatial Anchor representation**

An anchor is a normal semantic entity whose trusted visible handle is created by the renderer. Handle display radius is local UI state, not object size. Placement commands use the anchor's host transform.

- [ ] **Step 4: Implement transform interaction with start/preview/commit**

Use trusted Three.js TransformControls or equivalent trusted controller. On pointer-down/drag start, call `edit.begin` for transform fields. During drag, change only the trusted local root preview. Do not call guest JS or the model. On pointer-up, send one `edit.commit`; on Escape/disconnect, restore last host-accepted root and send `edit.cancel` when possible.

- [ ] **Step 5: Implement nested semantic roots**

Parent entity root groups by host `ParentId`. Child local transform comes from child state. Re-parenting is a host command; renderer never infers hierarchy from spatial proximity.

- [ ] **Step 6: Implement revision-aware picking after regeneration**

Raycast to resource child → trusted entity root metadata → stable handle key. If a handle key disappeared in the new implementation revision, return `missing_handle` and require explicit re-selection. Never remap by triangle order.

- [ ] **Step 7: Add procedural-local-versus-root test**

Run trusted guest ticks that change point-buffer local positions while moving the entity root. Assert tick updates never modify host/root transform and remain local beneath the moved root.

- [ ] **Step 8: Run and commit**

Run:

```powershell
npm test --workspace @workspace/vnext-spatial
npm test --workspace @workspace/spatial-runtime
npm run typecheck --workspace @workspace/vnext-spatial
```

Expected: PASS.

```bash
git add apps/spatial packages/spatial-runtime
git commit -m "feat(vnext): add authoritative direct spatial manipulation"
```

---

### Task 10: Prove model-offline undo/save/reopen and crash-during-drag behavior

**Acceptance coverage:** A23, A52.

**Files:**
- Create: `tests/acceptance/package.json`
- Create: `tests/acceptance/playwright.config.ts`
- Create: `tests/acceptance/helpers/start-state.ts`
- Create: `tests/acceptance/m1-runtime.spec.ts`
- Modify: root `package.json`
- Modify: `package-lock.json`

Pin `@playwright/test` `1.63.0`.

**Interfaces:**
- Test host runs with `--acceptance --session-token m1-acceptance-token --state-root <temp>`.
- Test spatial app receives `#session=m1-acceptance-token&host=ws://127.0.0.1:<port>/workspace`.

- [ ] **Step 1: Add failing model-offline acceptance**

```ts
test('A23 ordinary editing is model independent', async ({ page }) => {
  await page.goto(appUrl);
  await expect(page.getByTestId('agent-network-calls')).toHaveText('0');
  await dragEntity(page, 'entity:box', { x: 120, y: 0 });
  await page.getByRole('button', { name: 'Undo' }).click();
  await page.getByRole('button', { name: 'Save' }).click();
  await page.reload();
  await expectEntityPose(page, 'entity:box', initialPose);
  await expect(page.getByTestId('agent-network-calls')).toHaveText('0');
});
```

M1 app must not even register model-provider clients; the diagnostic remains zero by construction.

- [ ] **Step 2: Verify RED**

Run: `npx playwright test tests/acceptance/m1-runtime.spec.ts -g "A23"`

Expected: FAIL.

- [ ] **Step 3: Add A52 save-mid-drag acceptance**

Begin drag without pointer-up, trigger trusted Save control, forcibly close the page/connection, reopen. Assert last host-accepted pose, no active lease, and one subsequent undo behaves normally with no duplicate history entry.

- [ ] **Step 4: Make acceptance helpers deterministic**

Expose test-only diagnostics from trusted app code: current entity transforms, revisions, active lease count, active generation count, resource counts. Do not expose capability grant mutation methods through the diagnostic API.

- [ ] **Step 5: Run and commit**

Run: `npx playwright test tests/acceptance/m1-runtime.spec.ts`

Expected: PASS.

```bash
git add tests/acceptance package.json package-lock.json apps/spatial apps/host
git commit -m "test(vnext): prove model-offline editing and crash recovery"
```

---

### Task 11: Implement A47 breadth packages, host asset handles, shader/context-loss recovery, and trusted recovery UI

**Acceptance coverage:** A02, A35, A36, A47, A50.

**Files:**
- Create: `examples/world-packages/m1-line/*`
- Create: `examples/world-packages/m1-breadth/*`
- Create: `examples/world-packages/m1-procedural/*`
- Create: `apps/spatial/src/recovery/TrustedRecoveryControls.ts`
- Modify: `packages/spatial-runtime/src/assets/AssetResolver.ts`
- Modify: `packages/spatial-runtime/src/rendering/ThreeResourceProjector.ts`
- Modify: `tests/acceptance/m1-runtime.spec.ts`
- Create: `tests/acceptance/m1-renderer-failure.spec.ts`

- [ ] **Step 1: Create the A47 manual breadth package source**

The package emits one resource for each required descriptor family: indexed geometry with a custom float attribute, curve, shader + uniform, texture by `asset:sha256:*` handle, point light, points/point-buffer update, and instanced draw. It must not import `three`.

Example entry shape:

```js
import { scene } from '@workspace/creative-sdk';

export function start() {
  scene.geometry('g', { positions: [...], indices: [...], attributes: { heat: { itemSize: 1, values: [...] } } });
  scene.shaderMaterial('m', { vertexShader: '...', fragmentShader: '...', uniforms: { uTime: 0 } });
  scene.points('p', { positions: [...], material: { color: '#ffffff', size: 0.05 } });
  scene.light('l', { lightType: 'point', color: '#ffffff', intensity: 2, position: [0, 2, 0] });
}

export function onTick(t) {
  scene.update('p', { positions: proceduralPoints(t) });
  scene.update('m', { uniforms: { uTime: t / 1000 } });
}
```

- [ ] **Step 2: Add Playwright breadth test and verify RED**

Assert each descriptor kind is projected, renders without WebGL error, survives page reload through package revision reconstruction, and remains associated with the same entity root.

- [ ] **Step 3: Implement host asset-handle resolution**

The host registers an RGBA fixture blob and emits an opaque handle. The renderer asks the trusted asset endpoint using that handle/session. Requests containing URL/path locator syntax are rejected before any fetch. Build `DataTexture` from trusted RGBA bytes.

- [ ] **Step 4: Add context loss recovery test**

Use `WEBGL_lose_context` in Chromium acceptance to lose the context after a saved checkpoint. Assert committed state remains in host/SQLite. Restore or reload the renderer and assert the package reconstructs from the saved active revision.

- [ ] **Step 5: Add trusted recovery controls**

Create HTML controls outside the guest canvas/resource tree for `Pause packages` and `Disable current package`. Guest-created label/mesh text that says “Approve” has no click route to capabilities. Acceptance clicks a fake guest approval object and verifies grants unchanged, then uses the trusted control successfully.

- [ ] **Step 6: Run and commit**

Run:

```powershell
npx playwright test tests/acceptance/m1-runtime.spec.ts tests/acceptance/m1-renderer-failure.spec.ts
npm test --workspace @workspace/spatial-runtime
```

Expected: PASS.

```bash
git add examples/world-packages apps/spatial packages/spatial-runtime tests/acceptance
git commit -m "test(vnext): prove M1 creative breadth and renderer recovery"
```

---

### Task 12: Complete the M1 security/failure matrix, including draft/process stub evidence

**Acceptance coverage:** A08, A09, A10, A22, A31, A32, A33, A34, A36, A50.

**Files:**
- Create: `tests/acceptance/m1-security.spec.ts`
- Create: `tests/acceptance/fixtures/fake-credential-process.mjs`
- Create: `examples/world-packages/m1-malformed/*`
- Create: `examples/world-packages/m1-forbidden-import/*`
- Modify: `tests/Workspace.Runtime.Tests/PackagePolicyTests.cs`
- Modify: `tests/Workspace.Host.Tests/WorkspaceSocketEndpointTests.cs`

- [ ] **Step 1: Add the matrix as individually named tests**

Tests must include:

```text
A08 infinite CPU loop interrupted
A08 memory/allocation ceiling enforced
A09 fetch/process/require/document/WebSocket unavailable
A10 guessed other-generation resource handle rejected
A22 import "three" rejected with explicit compatibility error
A31 draft .. / rooted path / product sentinel / auth sentinel writes rejected
A31 fake credential process never receives generated-code execution message
A32 postinstall/lifecycle/plugin fields rejected
A33 invalid session token rejected
A33 forged actor/generation payload ignored/rejected
A34 ungivable capability request blocks candidate activation
A36 guest fake approval UI cannot change grants
A50 descriptor locator cannot induce trusted renderer fetch
```

- [ ] **Step 2: Verify at least one test is RED before each missing control is added**

Do not add all controls first. Run each focused test once against the missing behavior and record the expected failure in the implementation notes/commit description.

- [ ] **Step 3: Implement the fake credential-process stub**

The stub writes a sentinel file and listens only for a health ping. The M1 authoring/draft path must not send package source or an execution request to it. This is explicitly not the M3 real OpenCode test.

- [ ] **Step 4: Run the full security matrix**

Run:

```powershell
npx playwright test tests/acceptance/m1-security.spec.ts
dotnet test tests/Workspace.Runtime.Tests/Workspace.Runtime.Tests.csproj --configuration Release
dotnet test tests/Workspace.Host.Tests/Workspace.Host.Tests.csproj --configuration Release
npm test --workspace @workspace/creative-runtime --workspace @workspace/spatial-runtime
```

Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests examples/world-packages src/Workspace.Runtime apps/host apps/spatial packages/creative-runtime packages/spatial-runtime
git commit -m "test(vnext): enforce M1 isolation and authority boundaries"
```

---

### Task 13: Add the M1 acceptance record, CI gate, and exact verification commands

**Acceptance coverage:** records every M1 gate without expanding claims beyond the tested mechanism.

**Files:**
- Create: `docs/architecture/vnext/m1-acceptance.md`
- Create: `scripts/verify-vnext-m1.ps1`
- Modify: `.github/workflows/windows-ci.yml`
- Modify: `docs/architecture/vnext/README.md`

**Interfaces:** none; this task is evidence/automation only.

- [ ] **Step 1: Create the acceptance record template with every M1 ID**

For each M1 acceptance ID, record:

```markdown
### A47 — Positive creative-breadth descriptor matrix
- Status: PASS / FAIL / BLOCKED
- Commit:
- Command:
- Environment / process topology:
- Generic mechanism exercised:
- Fixture:
- Observed result:
- Limits / non-claims:
```

Include every M1 ID from §19 exactly once. Do not list M2+ IDs as passed by implication.

- [ ] **Step 2: Create one verification script**

`scripts/verify-vnext-m1.ps1` runs, in order:

```powershell
$ErrorActionPreference = 'Stop'
npm ci
npm run vnext:contracts:check
npm run vnext:test:node
npm run vnext:typecheck
npm run vnext:build
dotnet test Workspace.VNext.sln --configuration Release
npx playwright install chromium
npx playwright test tests/acceptance/m1-runtime.spec.ts tests/acceptance/m1-renderer-failure.spec.ts tests/acceptance/m1-security.spec.ts
```

If any command exits non-zero, stop immediately.

- [ ] **Step 3: Add a separate vNext CI job**

Do not make the existing prototype/native artifact job consume vNext outputs. Add `verify-vnext-m1` on `windows-latest` that runs the verification script. Keep current native/fallback artifacts unchanged.

- [ ] **Step 4: Run fresh full verification**

Run: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/verify-vnext-m1.ps1`

Expected: exit code 0. Record actual test counts and actual browser/GPU backend in `m1-acceptance.md`; do not pre-fill invented counts.

- [ ] **Step 5: Review acceptance claims against §18–§19**

Explicitly verify:
- no claim of M2 constraint support;
- no claim of Coda/OpenCode authoring;
- no claim of multi-package offender isolation;
- no claim of full Three.js API compatibility;
- no claim that Playwright/SwiftShader equals all physical GPUs;
- no claim of Windows app surface integration in vNext M1.

- [ ] **Step 6: Commit**

```bash
git add .github/workflows/windows-ci.yml docs/architecture/vnext scripts/verify-vnext-m1.ps1
git commit -m "ci(vnext): gate and record M1 runtime acceptance"
```

---

## Plan Self-Review Checklist

### Spec coverage

- A01: Task 9
- A02: Tasks 6 and 11
- A03: Tasks 2, 8, 9
- A04: Task 2
- A06: Task 9
- A07: Tasks 4 and 8
- A08: Tasks 7 and 12
- A09: Tasks 7 and 12
- A10: Tasks 2, 5, 7, 12
- A11: Tasks 7 and 8
- A13: Tasks 3 and 4
- A15: Task 8
- A22: Tasks 6, 7, 12
- A23: Tasks 2, 3, 9, 10
- A24: Tasks 2 and 9
- A26: Task 4
- A30: Task 4
- A31: Tasks 4, 7, 12; real OpenCode process re-test explicitly deferred to M3
- A32: Tasks 4 and 12
- A33: Tasks 5 and 12
- A34 narrow M1: Tasks 4 and 12; remembered-grant widening remains M4
- A35: Task 11
- A36: Tasks 11 and 12
- A42 M1 half: Tasks 4 and 8
- A43: Tasks 7 and 9
- A47: Tasks 6 and 11
- A50: Tasks 6, 11, 12
- A52: Tasks 3, 9, 10
- A53 structural fixture: Tasks 2 and 9; live-rule fixture remains M2
- A57: Tasks 6 and 9

### Explicit non-goals for this plan

M1 does not implement A05/A12/A21/A25/A27/A28/A29/A37/A39/A44/A45/A53 live-rule/A56, any real model provider, OpenCode private runtime, real Windows app surfaces, remembered capability grants, voice replacement, reusable assembly export/import, guest-to-guest imports, advanced renderer-wide passes, multi-package offender isolation, or VR hardware.

### Dependency pins verified for planning on 2026-09-11

- `quickjs-emscripten-core` 0.32.0 and matching `@jitl` single-file variants.
- `quicktype` 26.0.0.
- `ajv` 8.20.0.
- `@playwright/test` 1.63.0.
- `Microsoft.Data.Sqlite` 8.0.31 on the .NET 8 servicing line.
- Reuse the repository's existing TypeScript 5.9.2, Vite 8.2.2, Three.js 0.185.1, and xUnit 2.9.2 versions.

No placeholder task remains. Implementation begins only after execution mode is chosen and an isolated M1 worktree is created from the plan-bearing architecture branch.
