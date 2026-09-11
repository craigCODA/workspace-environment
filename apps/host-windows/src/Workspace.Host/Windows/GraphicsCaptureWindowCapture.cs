using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT;

namespace Workspace.Host.Windows;

public sealed class GraphicsCaptureWindowCapture : IWindowCapture
{
    private readonly IDirect3DDevice _device;
    private readonly ConcurrentDictionary<string, CaptureSession> _sessions = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private long _nextStreamId;
    private int _disposed;

    public GraphicsCaptureWindowCapture()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new PlatformNotSupportedException(
                "Windows Graphics Capture is not supported on this Windows installation.");
        }

        _device = Direct3DInterop.CreateDevice();
    }

    public IReadOnlyCollection<string> ActiveStreamIds => _sessions.Keys.ToArray();

    public async Task<SurfaceStreamHandle> StartAsync(
        nint hwnd,
        CancellationToken cancellationToken)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            if (hwnd == nint.Zero)
            {
                throw new ArgumentException("A real top-level window handle is required.", nameof(hwnd));
            }

            var streamId = $"surface-{Interlocked.Increment(ref _nextStreamId):x16}-{Guid.NewGuid():N}";
            var item = Direct3DInterop.CreateItemForWindow(hwnd);
            var session = new CaptureSession(
                streamId,
                item,
                _device,
                () => StopFromCaptureCallback(streamId));

            if (!_sessions.TryAdd(streamId, session))
            {
                session.Dispose();
                throw new InvalidOperationException("Unable to allocate a surface stream id.");
            }

            try
            {
                if (session.ClosedSignaled)
                {
                    throw new InvalidOperationException("The Windows window closed before capture started.");
                }

                session.Start();
                return new SurfaceStreamHandle(
                    streamId,
                    item.Size.Width,
                    item.Size.Height);
            }
            catch
            {
                _sessions.TryRemove(streamId, out _);
                session.Dispose();
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public ValueTask<SurfaceFrame?> ReadLatestFrameAsync(
        string streamId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);

        if (!_sessions.TryGetValue(streamId, out var session))
        {
            return ValueTask.FromResult<SurfaceFrame?>(null);
        }

        return ValueTask.FromResult(session.ReadLatest(afterSequence));
    }

    public Task StopAsync(string streamId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(streamId);
        StopCore(streamId);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycleGate.WaitAsync();
        try
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            Volatile.Write(ref _disposed, 1);
            foreach (var streamId in _sessions.Keys.ToArray())
            {
                StopCore(streamId);
            }
            (_device as IDisposable)?.Dispose();
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void StopFromCaptureCallback(string streamId)
    {
        ThreadPool.QueueUserWorkItem(_ => StopCore(streamId));
    }

    private void StopCore(string streamId)
    {
        if (_sessions.TryRemove(streamId, out var session))
        {
            session.Dispose();
        }
    }

    private sealed class CaptureSession : IDisposable
    {
        private readonly string _streamId;
        private readonly IDirect3DDevice _device;
        private readonly GraphicsCaptureItem _item;
        private readonly Action _closed;
        private readonly Direct3D11CaptureFramePool _framePool;
        private readonly GraphicsCaptureSession _session;
        private SurfaceFrame? _latest;
        private long _sequence;
        private int _encodingFrame;
        private int _disposed;
        private int _closedSignaled;
        private SizeInt32 _lastSize;

        public CaptureSession(
            string streamId,
            GraphicsCaptureItem item,
            IDirect3DDevice device,
            Action closed)
        {
            _streamId = streamId;
            _item = item;
            _device = device;
            _closed = closed;
            _framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                2,
                item.Size);
            _session = _framePool.CreateCaptureSession(item);
            _lastSize = item.Size;
            _framePool.FrameArrived += OnFrameArrived;
            _item.Closed += OnItemClosed;
        }

        public void Start() => _session.StartCapture();

        public bool ClosedSignaled => Volatile.Read(ref _closedSignaled) != 0;

        public SurfaceFrame? ReadLatest(long afterSequence)
        {
            var frame = Volatile.Read(ref _latest);
            return frame is not null && frame.Sequence > afterSequence ? frame : null;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _item.Closed -= OnItemClosed;
            _framePool.FrameArrived -= OnFrameArrived;
            _session.Dispose();
            _framePool.Dispose();
        }

        private void OnItemClosed(GraphicsCaptureItem sender, object args)
        {
            Volatile.Write(ref _closedSignaled, 1);
            _closed();
        }

        private async void OnFrameArrived(Direct3D11CaptureFramePool sender, object args)
        {
            var ownsEncoding = false;

            try
            {
                if (Volatile.Read(ref _disposed) != 0) return;
                if (Interlocked.Exchange(ref _encodingFrame, 1) != 0)
                {
                    using var skipped = sender.TryGetNextFrame();
                    return;
                }
                ownsEncoding = true;

                SizeInt32 size;
                byte[] data;
                using (var frame = sender.TryGetNextFrame())
                {
                    if (frame is null) return;
                    size = frame.ContentSize;
                    data = await EncodePngAsync(frame.Surface);
                }

                var next = new SurfaceFrame(
                    _streamId,
                    Interlocked.Increment(ref _sequence),
                    size.Width,
                    size.Height,
                    "image/png",
                    data);
                Volatile.Write(ref _latest, next);

                if (size.Width > 0
                    && size.Height > 0
                    && (size.Width != _lastSize.Width || size.Height != _lastSize.Height))
                {
                    _lastSize = size;
                    _framePool.Recreate(
                        _device,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized,
                        2,
                        size);
                }
            }
            catch (Exception) when (Volatile.Read(ref _disposed) != 0)
            {
            }
            catch
            {
                _closed();
            }
            finally
            {
                if (ownsEncoding)
                {
                    Volatile.Write(ref _encodingFrame, 0);
                }
            }
        }

        private static async Task<byte[]> EncodePngAsync(IDirect3DSurface surface)
        {
            using var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(
                surface,
                BitmapAlphaMode.Premultiplied);
            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(bitmap);
            await encoder.FlushAsync();

            if (stream.Size > int.MaxValue)
            {
                throw new InvalidOperationException("Captured surface frame is too large.");
            }

            stream.Seek(0);
            using var reader = new DataReader(stream.GetInputStreamAt(0));
            var byteCount = checked((uint)stream.Size);
            await reader.LoadAsync(byteCount);
            var data = new byte[byteCount];
            reader.ReadBytes(data);
            return data;
        }
    }

    private static class Direct3DInterop
    {
        private const uint D3D11SdkVersion = 7;
        private const uint D3D11CreateDeviceBgraSupport = 0x20;
        private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
        private static readonly Guid DxgiDeviceGuid = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

        [ComImport]
        [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IGraphicsCaptureItemInterop
        {
            nint CreateForWindow(nint window, in Guid iid);
            nint CreateForMonitor(nint monitor, in Guid iid);
        }

        public static GraphicsCaptureItem CreateItemForWindow(nint hwnd)
        {
            var interop = GraphicsCaptureItem.As<IGraphicsCaptureItemInterop>();
            var itemPointer = interop.CreateForWindow(hwnd, GraphicsCaptureItemGuid);
            try
            {
                return MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPointer);
            }
            finally
            {
                Marshal.Release(itemPointer);
            }
        }

        public static IDirect3DDevice CreateDevice()
        {
            var result = D3D11CreateDevice(
                nint.Zero,
                D3DDriverType.Hardware,
                nint.Zero,
                D3D11CreateDeviceBgraSupport,
                nint.Zero,
                0,
                D3D11SdkVersion,
                out var nativeDevice,
                out _,
                out var immediateContext);
            Marshal.ThrowExceptionForHR(result);

            nint dxgiDevice = nint.Zero;
            nint inspectableDevice = nint.Zero;
            try
            {
                var iid = DxgiDeviceGuid;
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(nativeDevice, ref iid, out dxgiDevice));
                Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(
                    dxgiDevice,
                    out inspectableDevice));
                return MarshalInterface<IDirect3DDevice>.FromAbi(inspectableDevice);
            }
            finally
            {
                if (inspectableDevice != nint.Zero) Marshal.Release(inspectableDevice);
                if (dxgiDevice != nint.Zero) Marshal.Release(dxgiDevice);
                if (immediateContext != nint.Zero) Marshal.Release(immediateContext);
                if (nativeDevice != nint.Zero) Marshal.Release(nativeDevice);
            }
        }

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int D3D11CreateDevice(
            nint adapter,
            D3DDriverType driverType,
            nint software,
            uint flags,
            nint featureLevels,
            uint featureLevelCount,
            uint sdkVersion,
            out nint device,
            out uint featureLevel,
            out nint immediateContext);

        [DllImport("d3d11.dll", ExactSpelling = true)]
        private static extern int CreateDirect3D11DeviceFromDXGIDevice(
            nint dxgiDevice,
            out nint graphicsDevice);

        private enum D3DDriverType : uint
        {
            Hardware = 1,
        }
    }
}
