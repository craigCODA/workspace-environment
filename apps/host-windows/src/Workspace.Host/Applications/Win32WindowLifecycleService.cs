using System.Runtime.InteropServices;
using Workspace.Host.Windows;

namespace Workspace.Host.Applications;

public sealed class Win32WindowLifecycleService(IWindowCatalog windowCatalog) : IWindowLifecycleService
{
    private const uint WmClose = 0x0010;

    public async Task<WindowCloseState> RequestCloseAsync(
        string windowEntityId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowEntityId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var window = await FindWindowAsync(windowEntityId, cancellationToken);
        if (window is null) return WindowCloseState.NotRunning;

        _ = PostMessage(window.Hwnd, WmClose, nint.Zero, nint.Zero);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            if (await FindWindowAsync(windowEntityId, cancellationToken) is null)
            {
                return WindowCloseState.Closed;
            }
        }

        return WindowCloseState.ClosePending;
    }

    private async Task<WindowSnapshot?> FindWindowAsync(
        string entityId,
        CancellationToken cancellationToken) =>
        (await windowCatalog.ListAsync(cancellationToken)).FirstOrDefault(window =>
            window.Hwnd != nint.Zero
            && string.Equals($"pc.window:{window.ApplicationId}", entityId, StringComparison.Ordinal));

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);
}
