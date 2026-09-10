using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class WindowReconcilerTests
{
    [Fact]
    public void RecreatedMainWindowKeepsSemanticIdentity()
    {
        var first = new WindowSnapshot(
            (nint)100,
            10,
            "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600),
            true,
            false,
            "pc.application:workspace-test");
        var second = first with
        {
            Hwnd = (nint)900,
            ProcessId = 22,
            Title = "Workspace Test Window - changed runtime title",
        };
        var reconciler = new WindowReconciler();

        Assert.Equal(reconciler.ResolveEntityId(first), reconciler.ResolveEntityId(second));
    }

    [Fact]
    public void DifferentApplicationsDoNotShareMainWindowIdentity()
    {
        var edge = new WindowSnapshot((nint)1, 10, "Browser", new WindowBounds(0, 0, 800, 600), true, false, "pc.application:edge");
        var cursor = edge with { Hwnd = (nint)2, ProcessId = 20, ApplicationId = "pc.application:cursor" };
        var reconciler = new WindowReconciler();

        Assert.NotEqual(reconciler.ResolveEntityId(edge), reconciler.ResolveEntityId(cursor));
    }

    [Fact]
    public async Task ClosingWindowStopsCaptureWithoutChangingSemanticIdentity()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var window = new WindowSnapshot(
            (nint)100,
            10,
            "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600),
            true,
            false,
            "pc.application:workspace-test");

        var opened = await reconciler.TrackAsync(window, CancellationToken.None);
        await reconciler.ReconcileAsync([], CancellationToken.None);

        Assert.Empty(reconciler.ActiveCaptureStreams);
        Assert.Equal(opened.Stream.StreamId, Assert.Single(capture.StoppedStreamIds));
        Assert.Equal(opened.EntityId, reconciler.ResolveEntityId(window with
        {
            Hwnd = (nint)999,
            ProcessId = 22,
        }));
    }

    [Fact]
    public async Task RecreatedWindowReplacesRuntimeStreamButKeepsEntityIdentity()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var first = new WindowSnapshot(
            (nint)100,
            10,
            "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600),
            true,
            false,
            "pc.application:workspace-test");

        var opened = await reconciler.TrackAsync(first, CancellationToken.None);
        var rebound = await reconciler.TrackAsync(first with
        {
            Hwnd = (nint)999,
            ProcessId = 22,
        }, CancellationToken.None);

        Assert.Equal(opened.EntityId, rebound.EntityId);
        Assert.NotEqual(opened.Stream.StreamId, rebound.Stream.StreamId);
        Assert.Contains(opened.Stream.StreamId, capture.StoppedStreamIds);
    }

    [Fact]
    public async Task ExactPersistedWindowIdentityResolvesToTheCurrentRuntimeForSurfaceCapture()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var window = new WindowSnapshot((nint)0x2a, 42, "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600), true, false, "pc.application:workspace-test");
        await reconciler.ReconcileAsync([window], CancellationToken.None);

        var stream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveExactEntityId(window), CancellationToken.None);

        Assert.Equal("stream-1", stream.StreamId);
    }

    private sealed class RecordingWindowCapture : IWindowCapture
    {
        private int _nextStreamId;
        private readonly HashSet<string> _activeStreamIds = [];

        public IReadOnlyCollection<string> ActiveStreamIds => _activeStreamIds;

        public List<string> StoppedStreamIds { get; } = [];

        public Task<SurfaceStreamHandle> StartAsync(nint hwnd, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stream = new SurfaceStreamHandle($"stream-{++_nextStreamId}", 800, 600);
            _activeStreamIds.Add(stream.StreamId);
            return Task.FromResult(stream);
        }

        public ValueTask<SurfaceFrame?> ReadLatestFrameAsync(
            string streamId,
            long afterSequence,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<SurfaceFrame?>(null);
        }

        public Task StopAsync(string streamId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _activeStreamIds.Remove(streamId);
            StoppedStreamIds.Add(streamId);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
