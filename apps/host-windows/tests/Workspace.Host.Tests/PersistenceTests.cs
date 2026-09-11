using System.Text;
using Workspace.Host.Domain;
using Workspace.Host.Persistence;
using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class PersistenceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"workspace-host-tests-{Guid.NewGuid():N}");

    public PersistenceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task SaveThenLoadRoundTripsWorkspaceDocument()
    {
        var store = new AtomicWorkspaceStore(Path.Combine(_tempDir, "workspace.json"));
        var expected = WorkspaceDocumentFixtures.SingleApplication();

        await store.SaveAsync(expected, CancellationToken.None);

        Assert.Equal(expected, await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task MissingStoresReturnIndependentEmptyDocuments()
    {
        var first = new AtomicWorkspaceStore(Path.Combine(_tempDir, "first.json"));
        var second = new AtomicWorkspaceStore(Path.Combine(_tempDir, "second.json"));

        var firstDocument = await first.LoadAsync(CancellationToken.None);
        firstDocument.Entities.Add(
            WorkspaceEntity.CreateApplication("pc.application:notepad", "Notepad"));

        var secondDocument = await second.LoadAsync(CancellationToken.None);

        Assert.Empty(secondDocument.Entities);
    }

    [Fact]
    public void Migration_creates_one_surface_and_preserves_window_placement()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge") with
        {
            Presentation = PresentationState.Default with { Position = new Vec3(4.2, 1.4, -3) },
        };

        var migrated = new WorkspaceDocument(1, [window]).MigrateToCurrent();
        var surface = Assert.Single(migrated.Entities, entity => entity.Kind == EntityKinds.Surface);

        Assert.Equal(new Vec3(4.2, 1.4, -3), surface.Presentation.Position);
        Assert.Contains(surface.Relationships, relationship =>
            relationship.Type == "displays" && relationship.TargetId == window.Id);
        Assert.Single(migrated.MigrateToCurrent().Entities, entity => entity.Kind == EntityKinds.Surface);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public void Migration_rejects_unsupported_schema_versions(int schemaVersion)
    {
        var document = new WorkspaceDocument(schemaVersion, []);

        Assert.Throws<InvalidDataException>(() => document.MigrateToCurrent());
    }

    [Theory]
    [MemberData(nameof(MalformedDocuments))]
    public void Migration_rejects_malformed_workspace_structure(WorkspaceDocument document)
    {
        Assert.Throws<InvalidDataException>(() => document.MigrateToCurrent());
    }

    [Fact]
    public async Task EnsureCurrent_rejects_malformed_deserialized_document_without_rewriting_it()
    {
        var path = Path.Combine(_tempDir, "malformed-workspace.json");
        const string malformedJson = "{\"schemaVersion\":1,\"entities\":null}";
        await File.WriteAllTextAsync(path, malformedJson, Encoding.UTF8);
        var store = new AtomicWorkspaceStore(path);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None));

        Assert.Equal(malformedJson, await File.ReadAllTextAsync(path, Encoding.UTF8));
    }

    [Fact]
    public void Migration_rejects_deterministic_surface_id_collision()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var collidingEntity = WorkspaceEntity.CreateApplication(
            $"{EntityKinds.Surface}:{window.Id}",
            "Not a display surface");

        Assert.Throws<InvalidDataException>(() =>
            new WorkspaceDocument(1, [window, collidingEntity]).MigrateToCurrent());
    }

    [Fact]
    public void Migration_rejects_duplicate_entity_ids()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var duplicate = window with { Name = "Duplicate Edge" };

        Assert.Throws<InvalidDataException>(() =>
            new WorkspaceDocument(1, [window, duplicate]).MigrateToCurrent());
    }

    [Fact]
    public void Migration_rejects_partial_surface_at_the_deterministic_id()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var partialSurface = WorkspaceEntity.CreateDisplaySurface(
            $"{EntityKinds.Surface}:{window.Id}",
            "Edge",
            PresentationState.Default);

        Assert.Throws<InvalidDataException>(() =>
            new WorkspaceDocument(1, [window, partialSurface]).MigrateToCurrent());
    }

    [Fact]
    public void Migration_preserves_a_matching_deterministic_surface()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var surface = WorkspaceEntity.CreateDisplaySurface(
            $"{EntityKinds.Surface}:{window.Id}",
            "Edge",
            window.Presentation,
            window.Id);

        var migrated = new WorkspaceDocument(1, [window, surface]).MigrateToCurrent();

        Assert.Equal(WorkspaceDocument.CurrentSchemaVersion, migrated.SchemaVersion);
        Assert.Equal(2, migrated.Entities.Count);
        Assert.Same(surface, Assert.Single(migrated.Entities, entity => entity.Id == surface.Id));
    }

    [Fact]
    public async Task EnsureCurrent_saves_a_migration_once()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var store = new ControlledWorkspaceStore(new WorkspaceDocument(1, [window]));

        await WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None);
        await WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None);

        Assert.Equal(1, store.SaveCount);
        Assert.Equal(WorkspaceDocument.CurrentSchemaVersion, store.Document.SchemaVersion);
        Assert.Single(store.Document.Entities, entity => entity.Kind == EntityKinds.Surface);
    }

    [Fact]
    public async Task EnsureCurrent_does_not_rewrite_a_current_document()
    {
        var current = new WorkspaceDocument(WorkspaceDocument.CurrentSchemaVersion, []);
        var store = new ControlledWorkspaceStore(current);

        await WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None);

        Assert.Equal(0, store.SaveCount);
        Assert.Same(current, store.Document);
    }

    [Fact]
    public async Task EnsureCurrent_leaves_the_old_document_intact_when_persistence_fails()
    {
        var original = LegacyWindowDocument();
        var store = new ControlledWorkspaceStore(original, _ => new IOException("Disk unavailable."));

        await Assert.ThrowsAsync<IOException>(() =>
            WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None));

        Assert.Equal(1, store.SaveCount);
        Assert.Same(original, store.Document);
    }

    [Fact]
    public async Task EnsureCurrent_leaves_the_old_document_intact_when_persistence_is_cancelled()
    {
        var original = LegacyWindowDocument();
        var store = new ControlledWorkspaceStore(original, cancellationToken =>
            cancellationToken.IsCancellationRequested
                ? new OperationCanceledException(cancellationToken)
                : null);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            WorkspaceMigrator.EnsureCurrentAsync(store, cancellation.Token));

        Assert.Equal(1, store.SaveCount);
        Assert.Same(original, store.Document);
    }

    [Fact]
    public async Task EnsureCurrent_atomic_publication_failure_after_temp_creation_preserves_file_and_cleans_up()
    {
        var statePath = Path.Combine(_tempDir, "migration-failure.json");
        var original = LegacyWindowDocument();
        await new AtomicWorkspaceStore(statePath).SaveAsync(original, CancellationToken.None);
        var originalBytes = await File.ReadAllBytesAsync(statePath);
        var temporaryFileCreated = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var allowFailure = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var store = new AtomicWorkspaceStore(
            statePath,
            async (temporaryPath, _) =>
            {
                temporaryFileCreated.TrySetResult(temporaryPath);
                await allowFailure.Task;
                throw new IOException("Injected publication failure.");
            });

        var migrationTask = WorkspaceMigrator.EnsureCurrentAsync(store, CancellationToken.None);
        var temporaryPath = await temporaryFileCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(temporaryPath));
        allowFailure.SetResult();

        await Assert.ThrowsAsync<IOException>(() => migrationTask);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(statePath));
        Assert.False(File.Exists(temporaryPath));
        Assert.Empty(Directory.EnumerateFiles(_tempDir, "migration-failure.json.*.tmp"));
    }

    [Fact]
    public async Task EnsureCurrent_atomic_publication_cancellation_after_temp_creation_preserves_file_and_cleans_up()
    {
        var statePath = Path.Combine(_tempDir, "migration-cancellation.json");
        var original = LegacyWindowDocument();
        await new AtomicWorkspaceStore(statePath).SaveAsync(original, CancellationToken.None);
        var originalBytes = await File.ReadAllBytesAsync(statePath);
        var temporaryFileCreated = new TaskCompletionSource<string>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var store = new AtomicWorkspaceStore(
            statePath,
            async (temporaryPath, cancellationToken) =>
            {
                temporaryFileCreated.TrySetResult(temporaryPath);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        var migrationTask = WorkspaceMigrator.EnsureCurrentAsync(store, cancellation.Token);
        var temporaryPath = await temporaryFileCreated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(File.Exists(temporaryPath));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migrationTask);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(statePath));
        Assert.False(File.Exists(temporaryPath));
        Assert.Empty(Directory.EnumerateFiles(_tempDir, "migration-cancellation.json.*.tmp"));
    }

    [Fact]
    public void Bind_window_replaces_only_the_display_relationship()
    {
        var first = WorkspaceEntity.CreateWindow("pc.window:first", "First", "pc.application:first");
        var second = WorkspaceEntity.CreateWindow("pc.window:second", "Second", "pc.application:second");
        var surface = WorkspaceEntity.CreateDisplaySurface(
            "spatial.surface:desk",
            "Desk",
            PresentationState.Default,
            first.Id) with
        {
            Relationships =
            [
                new Relationship("owned-by", "workspace.place:desk"),
                new Relationship("displays", first.Id),
            ],
        };
        var document = new WorkspaceDocument(2, [first, second, surface]);

        Assert.True(document.TryBindWindow(surface.Id, second.Id, out var updated));
        Assert.Contains(updated!.Relationships, relationship =>
            relationship.Type == "displays" && relationship.TargetId == second.Id);
        Assert.DoesNotContain(updated.Relationships, relationship =>
            relationship.Type == "displays" && relationship.TargetId == first.Id);
        Assert.Contains(updated.Relationships, relationship =>
            relationship.Type == "owned-by" && relationship.TargetId == "workspace.place:desk");
    }

    [Fact]
    public void Bind_window_rejects_invalid_entities_without_changing_the_surface()
    {
        var window = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");
        var surface = WorkspaceEntity.CreateDisplaySurface(
            "spatial.surface:desk",
            "Desk",
            PresentationState.Default,
            window.Id);
        var document = new WorkspaceDocument(2, [window, surface]);

        Assert.False(document.TryBindWindow(surface.Id, "missing-window", out var updated));
        Assert.Null(updated);
        Assert.Same(surface, Assert.Single(document.Entities, entity => entity.Id == surface.Id));
        Assert.Contains(surface.Relationships, relationship =>
            relationship.Type == "displays" && relationship.TargetId == window.Id);
    }

    [Fact]
    public async Task PresentationSurvivesHostRestartAndRebindsToANewHwnd()
    {
        const string applicationId = "pc.application:workspace-test";
        const string windowId = "pc.window:pc.application:workspace-test";
        var statePath = Path.Combine(_tempDir, "restart.json");
        var store = new AtomicWorkspaceStore(statePath);
        var presentation = PresentationState.Default with
        {
            Position = new Vec3(3, 1.5, -2),
            Size = new Vec3(4.2, 2.4, 1),
        };
        var window = WorkspaceEntity.CreateWindow(
            windowId,
            "Workspace Test Window",
            applicationId) with
        {
            Presentation = presentation,
        };
        await store.SaveAsync(new WorkspaceDocument(1, [window]), CancellationToken.None);

        var capture = new RecordingWindowCapture();
        await using var restartedReconciler = new WindowReconciler(capture);
        var replacement = new WindowSnapshot(
            (nint)999,
            77,
            "Workspace Test Window - restarted",
            new WindowBounds(50, 80, 1_000, 700),
            true,
            false,
            applicationId);

        await restartedReconciler.ReconcileAsync([replacement], CancellationToken.None);
        await restartedReconciler.OpenSurfaceAsync(windowId, CancellationToken.None);
        var reopened = await new AtomicWorkspaceStore(statePath).LoadAsync(CancellationToken.None);

        Assert.Equal(windowId, Assert.Single(reopened.Entities).Id);
        Assert.Equal(presentation, reopened.Entities[0].Presentation);
        Assert.Equal((nint)999, Assert.Single(capture.StartedHwnds));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    public static IEnumerable<object[]> MalformedDocuments()
    {
        var valid = WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge");

        yield return [new WorkspaceDocument(1, null!)];
        yield return [new WorkspaceDocument(1, [null!])];
        yield return [new WorkspaceDocument(1, [valid with { Id = "" }])];
        yield return [new WorkspaceDocument(1, [valid with { Kind = "" }])];
        yield return [new WorkspaceDocument(1, [valid with { Name = "" }])];
        yield return [new WorkspaceDocument(1, [valid with { Presentation = null! }])];
        yield return [new WorkspaceDocument(1, [valid with { Relationships = null! }])];
        yield return [new WorkspaceDocument(1, [valid with { Properties = null! }])];
        yield return [new WorkspaceDocument(1, [valid with { Capabilities = null! }])];
        yield return [new WorkspaceDocument(1, [valid with
        {
            Relationships = [new Relationship("displays", "")],
        }])];
    }

    private static WorkspaceDocument LegacyWindowDocument()
    {
        return new WorkspaceDocument(
            1,
            [WorkspaceEntity.CreateWindow("pc.window:edge", "Edge", "pc.application:edge")]);
    }

    private sealed class RecordingWindowCapture : IWindowCapture
    {
        private readonly HashSet<string> _active = [];

        public IReadOnlyCollection<string> ActiveStreamIds => _active;

        public List<nint> StartedHwnds { get; } = [];

        public Task<SurfaceStreamHandle> StartAsync(nint hwnd, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartedHwnds.Add(hwnd);
            var stream = new SurfaceStreamHandle($"stream-{StartedHwnds.Count}", 1_000, 700);
            _active.Add(stream.StreamId);
            return Task.FromResult(stream);
        }

        public ValueTask<SurfaceFrame?> ReadLatestFrameAsync(
            string streamId,
            long afterSequence,
            CancellationToken cancellationToken) => ValueTask.FromResult<SurfaceFrame?>(null);

        public Task StopAsync(string streamId, CancellationToken cancellationToken)
        {
            _active.Remove(streamId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class ControlledWorkspaceStore(
        WorkspaceDocument document,
        Func<CancellationToken, Exception?>? saveFailure = null) : IWorkspaceStore
    {
        public WorkspaceDocument Document { get; private set; } = document;

        public int SaveCount { get; private set; }

        public Task<WorkspaceDocument> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Document);

        public Task SaveAsync(WorkspaceDocument document, CancellationToken cancellationToken)
        {
            SaveCount++;
            var failure = saveFailure?.Invoke(cancellationToken);
            if (failure is not null)
            {
                return Task.FromException(failure);
            }

            Document = document;
            return Task.CompletedTask;
        }
    }
}

internal static class WorkspaceDocumentFixtures
{
    public static WorkspaceDocument SingleApplication()
    {
        return new WorkspaceDocument(
            1,
            [WorkspaceEntity.CreateApplication("app:microsoft-edge", "Microsoft Edge")]);
    }
}
