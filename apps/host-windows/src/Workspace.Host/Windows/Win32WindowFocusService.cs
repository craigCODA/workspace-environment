using System.Runtime.InteropServices;
using Workspace.Host.Protocol;

namespace Workspace.Host.Windows;

public sealed class Win32WindowFocusService : IWindowFocusService
{
    private readonly IWindowCatalog _windowCatalog;
    private readonly WindowReconciler _windowReconciler;
    private readonly Func<nint, bool> _tryFocusWindow;

    public Win32WindowFocusService(
        IWindowCatalog windowCatalog,
        WindowReconciler windowReconciler,
        Func<nint, bool>? tryFocusWindow = null)
    {
        _windowCatalog = windowCatalog;
        _windowReconciler = windowReconciler;
        _tryFocusWindow = tryFocusWindow ?? TryFocusWindow;
    }

    public async Task FocusAsync(string entityId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        var windows = await _windowCatalog.ListAsync(cancellationToken);
        var window = windows.FirstOrDefault(candidate =>
            candidate.IsVisible
            && !candidate.IsMinimized
            && candidate.Bounds.Width > 0
            && candidate.Bounds.Height > 0
            && string.Equals(
                _windowReconciler.ResolveEntityId(candidate),
                entityId,
                StringComparison.Ordinal));
        if (window is null)
        {
            throw new KeyNotFoundException(
                $"The Windows window '{entityId}' is not currently available.");
        }

        if (!_tryFocusWindow(window.Hwnd))
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
        if (targetThread == 0) return false;

        var attachedForeground = false;
        var attachedTarget = false;
        try
        {
            if (foregroundThread != 0 && foregroundThread != currentThread)
            {
                attachedForeground = AttachThreadInput(currentThread, foregroundThread, true);
            }
            if (targetThread != currentThread && targetThread != foregroundThread)
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
}
