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
