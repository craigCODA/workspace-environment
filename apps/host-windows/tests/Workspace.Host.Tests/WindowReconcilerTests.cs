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
        Assert.Equal(reconciler.ResolveExactEntityId(window), opened.EntityId);
        Assert.Equal(reconciler.ResolveEntityId(window), reconciler.ResolveEntityId(window with
        {
            Hwnd = (nint)999,
            ProcessId = 22,
        }));
    }

    [Fact]
    public async Task DistinctTrackedWindowsUseExactRuntimeIdentityAndIndependentStreams()
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

        Assert.Equal(reconciler.ResolveExactEntityId(first), opened.EntityId);
        Assert.Equal(reconciler.ResolveExactEntityId(first with { Hwnd = (nint)999, ProcessId = 22 }), rebound.EntityId);
        Assert.NotEqual(opened.EntityId, rebound.EntityId);
        Assert.NotEqual(opened.Stream.StreamId, rebound.Stream.StreamId);
        Assert.Equal(2, reconciler.ActiveCaptureStreams.Count);
        Assert.Empty(capture.StoppedStreamIds);
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

    [Fact]
    public async Task Reconcile_preserves_distinct_runtime_windows_for_each_visible_same_application_instance()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var first = new WindowSnapshot((nint)0x2a, 42, "First", new WindowBounds(0, 0, 800, 600), true, false, "pc.application:workspace-test");
        var second = first with { Hwnd = (nint)0x2b, ProcessId = 43, Title = "Second" };
        await reconciler.ReconcileAsync([first, second], CancellationToken.None);

        var firstStream = await reconciler.OpenSurfaceAsync(reconciler.ResolveExactEntityId(first), CancellationToken.None);
        var secondStream = await reconciler.OpenSurfaceAsync(reconciler.ResolveExactEntityId(second), CancellationToken.None);

        Assert.NotEqual(firstStream.StreamId, secondStream.StreamId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => reconciler.OpenSurfaceAsync(
            reconciler.ResolveEntityId(first), CancellationToken.None));
    }

    [Fact]
    public async Task Legacy_surface_capture_is_stopped_when_its_resolved_runtime_disappears()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var window = new WindowSnapshot((nint)0x2a, 42, "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600), true, false, "pc.application:workspace-test");
        await reconciler.ReconcileAsync([window], CancellationToken.None);
        var stream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveEntityId(window), CancellationToken.None);

        await reconciler.ReconcileAsync([], CancellationToken.None);

        Assert.Empty(reconciler.ActiveCaptureStreams);
        Assert.Equal(stream.StreamId, Assert.Single(capture.StoppedStreamIds));
    }

    [Fact]
    public async Task Legacy_and_exact_ids_reuse_the_same_capture_stream()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var window = new WindowSnapshot((nint)0x2a, 42, "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600), true, false, "pc.application:workspace-test");
        await reconciler.ReconcileAsync([window], CancellationToken.None);

        var legacyStream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveEntityId(window), CancellationToken.None);
        var exactStream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveExactEntityId(window), CancellationToken.None);

        Assert.Equal(legacyStream, exactStream);
        Assert.Single(reconciler.ActiveCaptureStreams);
    }

    [Fact]
    public async Task Tracked_stream_reused_by_exact_id_survives_live_reconcile_and_stops_once_on_disappearance()
    {
        var capture = new RecordingWindowCapture();
        var reconciler = new WindowReconciler(capture);
        var window = new WindowSnapshot((nint)0x2a, 42, "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600), true, false, "pc.application:workspace-test");
        var tracked = await reconciler.TrackAsync(window, CancellationToken.None);

        var exactStream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveExactEntityId(window), CancellationToken.None);
        var legacyStream = await reconciler.OpenSurfaceAsync(
            reconciler.ResolveEntityId(window), CancellationToken.None);
        await reconciler.ReconcileAsync([window], CancellationToken.None);

        Assert.Equal(reconciler.ResolveExactEntityId(window), tracked.EntityId);
        Assert.Equal(tracked.Stream, exactStream);
        Assert.Equal(exactStream, legacyStream);
        Assert.Single(reconciler.ActiveCaptureStreams);
        Assert.Empty(capture.StoppedStreamIds);

        await reconciler.ReconcileAsync([], CancellationToken.None);

        Assert.Empty(reconciler.ActiveCaptureStreams);
        Assert.Equal(exactStream.StreamId, Assert.Single(capture.StoppedStreamIds));
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
