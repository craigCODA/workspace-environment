using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Workspace.Host.Windows;

public sealed class Win32InputRouter : IInputRouter
{
    private const int TextChunkLength = 128;
    private static readonly TimeSpan MaximumPointerHold = TimeSpan.FromSeconds(5);
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseWheel = 0x0800;
    private const uint MouseHorizontalWheel = 0x1000;
    private const uint KeyUp = 0x0002;
    private const uint KeyUnicode = 0x0004;

    private static readonly IReadOnlyDictionary<string, ushort> VirtualKeys =
        new Dictionary<string, ushort>(StringComparer.OrdinalIgnoreCase)
        {
            ["Backspace"] = 0x08,
            ["Tab"] = 0x09,
            ["Enter"] = 0x0D,
            ["Shift"] = 0x10,
            ["Control"] = 0x11,
            ["Alt"] = 0x12,
            ["Pause"] = 0x13,
            ["CapsLock"] = 0x14,
            ["Escape"] = 0x1B,
            ["Space"] = 0x20,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["End"] = 0x23,
            ["Home"] = 0x24,
            ["ArrowLeft"] = 0x25,
            ["ArrowUp"] = 0x26,
            ["ArrowRight"] = 0x27,
            ["ArrowDown"] = 0x28,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Meta"] = 0x5B,
            ["F1"] = 0x70,
            ["F2"] = 0x71,
            ["F3"] = 0x72,
            ["F4"] = 0x73,
            ["F5"] = 0x74,
            ["F6"] = 0x75,
            ["F7"] = 0x76,
            ["F8"] = 0x77,
            ["F9"] = 0x78,
            ["F10"] = 0x79,
            ["F11"] = 0x7A,
            ["F12"] = 0x7B,
        };

    private readonly Dictionary<(nint Hwnd, string Button), PointerSession> _pointerSessions = [];
    private readonly object _pointerGate = new();

    public async Task RouteAsync(
        WindowSnapshot window,
        WindowInputIntent intent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(intent);
        cancellationToken.ThrowIfCancellationRequested();

        if (window.Hwnd == nint.Zero || !IsWindow(window.Hwnd))
        {
            throw new InputTargetNotPermittedException(
                "The target Windows window is no longer available.");
        }

        var previousForeground = GetForegroundWindow();
        FocusTarget(window.Hwnd);
        var dispatch = InputDispatch.Default;
        try
        {
            dispatch = Dispatch(window, intent, previousForeground);
            await Task.Delay(TimeSpan.FromMilliseconds(25), CancellationToken.None);
        }
        finally
        {
            dispatch.Cleanup?.Invoke();
            var foregroundToRestore = dispatch.ForegroundToRestore ?? previousForeground;
            if (!dispatch.KeepTargetForeground
                && foregroundToRestore != nint.Zero
                && foregroundToRestore != window.Hwnd)
            {
                TryFocusWindow(foregroundToRestore);
            }
        }

    }

    public async Task ReleaseAllAsync(CancellationToken cancellationToken)
    {
        KeyValuePair<(nint Hwnd, string Button), PointerSession>[] sessions;
        lock (_pointerGate)
        {
            sessions = [.. _pointerSessions];
            _pointerSessions.Clear();
        }

        foreach (var session in sessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            session.Value.Expiration.Cancel();
            await ReleasePointerAsync(session.Key, session.Value);
        }
    }

    private InputDispatch Dispatch(
        WindowSnapshot window,
        WindowInputIntent intent,
        nint previousForeground)
    {
        return intent.Kind switch
        {
            "pointer" => RoutePointer(window, intent, previousForeground),
            "wheel" => RouteWheel(window, intent),
            "key" => RouteKey(window.Hwnd, intent),
            "text" => RouteText(window.Hwnd, intent.Text!),
            _ => throw new ArgumentException($"Unsupported input intent: {intent.Kind}.", nameof(intent)),
        };
    }

    private InputDispatch RoutePointer(
        WindowSnapshot window,
        WindowInputIntent intent,
        nint previousForeground)
    {
        MoveToSurfacePoint(window, intent.X!.Value, intent.Y!.Value, out var previousCursor);
        if (intent.Phase == "move")
        {
            var isDragging = false;
            lock (_pointerGate)
            {
                isDragging = _pointerSessions.Keys.Any(key => key.Hwnd == window.Hwnd);
            }
            return isDragging
                ? new InputDispatch(KeepTargetForeground: true)
                : new InputDispatch(Cleanup: () => RestoreCursor(previousCursor));
        }

        var key = (window.Hwnd, intent.Button!);
        var flags = (intent.Button, intent.Phase) switch
        {
            ("primary", "down") => MouseLeftDown,
            ("primary", "up") => MouseLeftUp,
            ("secondary", "down") => MouseRightDown,
            ("secondary", "up") => MouseRightUp,
            _ => throw new ArgumentException("Unsupported pointer intent.", nameof(intent)),
        };

        if (intent.Phase == "down")
        {
            lock (_pointerGate)
            {
                if (_pointerSessions.ContainsKey(key))
                {
                    RestoreCursor(previousCursor);
                    throw new ArgumentException("The pointer button is already held.", nameof(intent));
                }
            }

            try
            {
                Send(window.Hwnd, [MouseInput(flags)]);
            }
            catch
            {
                RestoreCursor(previousCursor);
                throw;
            }

            var downSession = new PointerSession(
                previousCursor,
                previousForeground,
                new CancellationTokenSource());
            lock (_pointerGate)
            {
                _pointerSessions[key] = downSession;
            }
            _ = ReleaseExpiredPointerAsync(key, downSession);
            return new InputDispatch(KeepTargetForeground: true);
        }

        PointerSession? session;
        lock (_pointerGate)
        {
            _pointerSessions.TryGetValue(key, out session);
        }
        try
        {
            Send(window.Hwnd, [MouseInput(flags)]);
        }
        catch
        {
            lock (_pointerGate)
            {
                _pointerSessions.Remove(key);
            }
            session?.Expiration.Cancel();
            session?.Expiration.Dispose();
            TryReleaseButton(flags);
            RestoreCursor(session?.PreviousCursor ?? previousCursor);
            if (session?.PreviousForeground is { } foreground && foreground != nint.Zero)
            {
                TryFocusWindow(foreground);
            }
            throw;
        }
        lock (_pointerGate)
        {
            _pointerSessions.Remove(key);
        }
        session?.Expiration.Cancel();
        return new InputDispatch(
            ForegroundToRestore: session?.PreviousForeground,
            Cleanup: () =>
            {
                RestoreCursor(session?.PreviousCursor ?? previousCursor);
                session?.Expiration.Dispose();
            });
    }

    private async Task ReleaseExpiredPointerAsync(
        (nint Hwnd, string Button) key,
        PointerSession session)
    {
        try
        {
            await Task.Delay(MaximumPointerHold, session.Expiration.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_pointerGate)
        {
            if (!_pointerSessions.TryGetValue(key, out var current)
                || !ReferenceEquals(current, session))
            {
                return;
            }
            _pointerSessions.Remove(key);
        }
        await ReleasePointerAsync(key, session);
    }

    private static async Task ReleasePointerAsync(
        (nint Hwnd, string Button) key,
        PointerSession session)
    {
        try
        {
            if (IsWindow(key.Hwnd))
            {
                TryFocusWindow(key.Hwnd);
            }
            TryReleaseButton(key.Button == "primary" ? MouseLeftUp : MouseRightUp);
            await Task.Delay(TimeSpan.FromMilliseconds(25));
        }
        catch (Exception exception) when (
            exception is InputTargetNotPermittedException or ArgumentException)
        {
            // Cleanup is best effort across process exit and Windows integrity boundaries.
        }
        finally
        {
            RestoreCursor(session.PreviousCursor);
            if (session.PreviousForeground != nint.Zero
                && session.PreviousForeground != key.Hwnd)
            {
                TryFocusWindow(session.PreviousForeground);
            }
            session.Expiration.Dispose();
        }
    }

    private static InputDispatch RouteWheel(WindowSnapshot window, WindowInputIntent intent)
    {
        MoveToSurfacePoint(window, intent.X!.Value, intent.Y!.Value, out var previousCursor);
        try
        {
            var inputs = new List<NativeInput>(2);
            var horizontal = ToWheelDelta(intent.DeltaX!.Value);
            var vertical = ToWheelDelta(-intent.DeltaY!.Value);
            if (horizontal != 0) inputs.Add(MouseInput(MouseHorizontalWheel, horizontal));
            if (vertical != 0) inputs.Add(MouseInput(MouseWheel, vertical));
            if (inputs.Count > 0) Send(window.Hwnd, [.. inputs]);
            return new InputDispatch(Cleanup: () => RestoreCursor(previousCursor));
        }
        catch
        {
            RestoreCursor(previousCursor);
            throw;
        }
    }

    private static InputDispatch RouteKey(nint hwnd, WindowInputIntent intent)
    {
        var virtualKey = ResolveVirtualKey(intent.Key!);
        var flags = intent.Phase == "up" ? KeyUp : 0;
        Send(hwnd, [KeyboardInput(virtualKey, 0, flags)]);
        return InputDispatch.Default;
    }

    private static InputDispatch RouteText(nint hwnd, string text)
    {
        if (text.Length > WindowInputLimits.MaximumTextLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(text),
                $"Text input cannot exceed {WindowInputLimits.MaximumTextLength} UTF-16 code units.");
        }

        for (var offset = 0; offset < text.Length; offset += TextChunkLength)
        {
            var length = Math.Min(TextChunkLength, text.Length - offset);
            var inputs = new NativeInput[length * 2];
            for (var index = 0; index < length; index++)
            {
                inputs[index * 2] = KeyboardInput(0, text[offset + index], KeyUnicode);
                inputs[index * 2 + 1] = KeyboardInput(0, text[offset + index], KeyUnicode | KeyUp);
            }
            Send(hwnd, inputs);
        }

        return InputDispatch.Default;
    }

    private static void MoveToSurfacePoint(
        WindowSnapshot window,
        double x,
        double y,
        out NativePoint? previousCursor)
    {
        if (!GetWindowRect(window.Hwnd, out var currentRect))
        {
            throw new InputTargetNotPermittedException(
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }

        var point = InputCoordinateMapper.Map(
            x,
            y,
            new WindowBounds(
                currentRect.Left,
                currentRect.Top,
                currentRect.Right - currentRect.Left,
                currentRect.Bottom - currentRect.Top));
        previousCursor = GetCursorPos(out var cursor) ? cursor : null;
        if (!SetCursorPos(point.X, point.Y))
        {
            throw new InputTargetNotPermittedException(
                new Win32Exception(Marshal.GetLastWin32Error()).Message);
        }
    }

    private static void RestoreCursor(NativePoint? point)
    {
        if (point is { } previous)
        {
            SetCursorPos(previous.X, previous.Y);
        }
    }

    private static void FocusTarget(nint hwnd)
    {
        if (GetForegroundWindow() == hwnd) return;
        if (!TryFocusWindow(hwnd))
        {
            throw new InputTargetNotPermittedException(
                "Windows did not permit input focus for the target window.");
        }
    }

    private static bool TryFocusWindow(nint hwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return true;

        var currentThread = GetCurrentThreadId();
        var foregroundThread = foreground == nint.Zero
            ? 0
            : GetWindowThreadProcessId(foreground, out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);
        var attachedForeground = false;
        var attachedTarget = false;

        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }
            if (targetThread != 0
                && targetThread != currentThread
                && targetThread != foregroundThread)
            {
                attachedTarget = AttachThreadInput(currentThread, targetThread, true);
            }

            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
            return GetForegroundWindow() == hwnd;
        }
        finally
        {
            if (attachedTarget)
            {
                AttachThreadInput(currentThread, targetThread, false);
            }
            if (attachedForeground)
            {
                AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static ushort ResolveVirtualKey(string key)
    {
        if (VirtualKeys.TryGetValue(key, out var virtualKey)) return virtualKey;
        if (key.Length == 1)
        {
            var mapped = VkKeyScan(key[0]);
            if (mapped != -1) return unchecked((ushort)(mapped & 0xff));
        }

        throw new ArgumentException($"Unsupported non-text key: {key}.", nameof(key));
    }

    private static int ToWheelDelta(double value) => checked((int)Math.Round(
        Math.Clamp(value, int.MinValue, int.MaxValue),
        MidpointRounding.AwayFromZero));

    private static NativeInput MouseInput(uint flags, int data = 0) => new()
    {
        Type = InputMouse,
        Data = new InputUnion
        {
            Mouse = new NativeMouseInput
            {
                MouseData = unchecked((uint)data),
                Flags = flags,
            },
        },
    };

    private static NativeInput KeyboardInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new NativeKeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags,
            },
        },
    };

    private static void Send(nint targetHwnd, NativeInput[] inputs)
    {
        if (inputs.Length == 0) return;
        if (GetForegroundWindow() != targetHwnd)
        {
            throw new InputTargetNotPermittedException(
                "Windows changed the foreground target before input could be injected.");
        }
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeInput>());
        if (sent != inputs.Length)
        {
            throw new InputTargetNotPermittedException(
                "Windows did not permit input injection for the target window.");
        }
    }

    private static void TryReleaseButton(uint flags)
    {
        var input = new[] { MouseInput(flags) };
        SendInput(1, input, Marshal.SizeOf<NativeInput>());
    }

    private sealed record PointerSession(
        NativePoint? PreviousCursor,
        nint PreviousForeground,
        CancellationTokenSource Expiration);

    private sealed record InputDispatch(
        bool KeepTargetForeground = false,
        nint? ForegroundToRestore = null,
        Action? Cleanup = null)
    {
        public static InputDispatch Default { get; } = new();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput
    {
        public uint Type;
        public InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public NativeMouseInput Mouse;

        [FieldOffset(0)]
        public NativeKeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeKeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool IsWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(nint hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(
        uint attachThreadId,
        uint attachToThreadId,
        bool attach);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char character);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(
        uint numberOfInputs,
        NativeInput[] inputs,
        int inputSize);

}
