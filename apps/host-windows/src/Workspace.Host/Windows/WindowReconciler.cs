namespace Workspace.Host.Windows;

public sealed record WindowRuntimeBinding(
    string EntityId,
    WindowSnapshot Runtime,
    SurfaceStreamHandle Stream);

public sealed class WindowReconciler : IAsyncDisposable
{
    private readonly IWindowCapture? _capture;
    private readonly Dictionary<string, WindowRuntimeBinding> _tracked = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WindowSnapshot> _runtime = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WindowReconciler()
    {
    }

    public WindowReconciler(IWindowCapture capture)
    {
        _capture = capture ?? throw new ArgumentNullException(nameof(capture));
    }

    public IReadOnlyCollection<string> ActiveCaptureStreams =>
        _capture?.ActiveStreamIds.ToArray() ?? [];

    public string ResolveEntityId(WindowSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshot.ApplicationId);

        return $"pc.window:{snapshot.ApplicationId}";
    }

    public async Task<WindowRuntimeBinding> TrackAsync(
        WindowSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_capture is null)
        {
            throw new InvalidOperationException("Window capture is not configured.");
        }

        var entityId = ResolveEntityId(snapshot);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _runtime[entityId] = snapshot;
            if (_tracked.TryGetValue(entityId, out var existing))
            {
                if (existing.Runtime.Hwnd == snapshot.Hwnd
                    && _capture.ActiveStreamIds.Contains(existing.Stream.StreamId, StringComparer.Ordinal))
                {
                    var refreshed = existing with { Runtime = snapshot };
                    _tracked[entityId] = refreshed;
                    return refreshed;
                }

                await _capture.StopAsync(existing.Stream.StreamId, cancellationToken);
                _tracked.Remove(entityId);
            }

            var stream = await _capture.StartAsync(snapshot.Hwnd, cancellationToken);
            var binding = new WindowRuntimeBinding(entityId, snapshot, stream);
            _tracked.Add(entityId, binding);
            return binding;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReconcileAsync(
        IReadOnlyCollection<WindowSnapshot> observations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (_capture is null)
        {
            return;
        }

        var observed = observations
            .Where(IsCapturable)
            .GroupBy(ResolveEntityId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var entityId in _runtime.Keys.ToArray())
            {
                if (!observed.TryGetValue(entityId, out var snapshot))
                {
                    if (_tracked.Remove(entityId, out var missingBinding))
                    {
                        await _capture.StopAsync(missingBinding.Stream.StreamId, cancellationToken);
                    }
                    _runtime.Remove(entityId);
                }
            }

            foreach (var (entityId, snapshot) in observed)
            {
                if (!_runtime.TryGetValue(entityId, out var previousRuntime))
                {
                    _runtime[entityId] = snapshot;
                    continue;
                }
                _runtime[entityId] = snapshot;
                if (!_tracked.TryGetValue(entityId, out var binding)) continue;
                if (snapshot.Hwnd == previousRuntime.Hwnd)
                {
                    _tracked[entityId] = binding with { Runtime = snapshot };
                    continue;
                }

                await _capture.StopAsync(binding.Stream.StreamId, cancellationToken);
                var replacement = await _capture.StartAsync(snapshot.Hwnd, cancellationToken);
                _tracked[entityId] = new WindowRuntimeBinding(entityId, snapshot, replacement);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<SurfaceStreamHandle> OpenSurfaceAsync(
        string entityId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (_capture is null)
        {
            throw new InvalidOperationException("Window capture is not configured.");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_tracked.TryGetValue(entityId, out var existing)
                && _capture.ActiveStreamIds.Contains(existing.Stream.StreamId, StringComparer.Ordinal))
            {
                return existing.Stream;
            }

            if (!_runtime.TryGetValue(entityId, out var runtime))
            {
                throw new KeyNotFoundException($"Window entity '{entityId}' has no current Windows window.");
            }

            var stream = await _capture.StartAsync(runtime.Hwnd, cancellationToken);
            _tracked[entityId] = new WindowRuntimeBinding(entityId, runtime, stream);
            return stream;
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask<SurfaceFrame?> ReadLatestFrameAsync(
        string streamId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        if (_capture is null)
        {
            return ValueTask.FromResult<SurfaceFrame?>(null);
        }
        return _capture.ReadLatestFrameAsync(streamId, afterSequence, cancellationToken);
    }

    public async Task CloseSurfaceAsync(string streamId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        if (_capture is null) return;

        await _capture.StopAsync(streamId, cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entityId = _tracked
                .FirstOrDefault(pair => string.Equals(
                    pair.Value.Stream.StreamId,
                    streamId,
                    StringComparison.Ordinal))
                .Key;
            if (entityId is not null)
            {
                _tracked.Remove(entityId);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_capture is null)
        {
            _gate.Dispose();
            return;
        }

        await _gate.WaitAsync();
        try
        {
            foreach (var streamId in _tracked.Values.Select(binding => binding.Stream.StreamId).ToArray())
            {
                await _capture.StopAsync(streamId, CancellationToken.None);
            }
            _tracked.Clear();
            _runtime.Clear();
            await _capture.DisposeAsync();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }

    private static bool IsCapturable(WindowSnapshot snapshot) =>
        snapshot.Hwnd != nint.Zero
        && snapshot.IsVisible
        && !snapshot.IsMinimized
        && snapshot.Bounds.Width > 0
        && snapshot.Bounds.Height > 0;
}
