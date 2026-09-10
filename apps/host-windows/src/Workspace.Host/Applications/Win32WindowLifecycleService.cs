using System.Runtime.InteropServices;
using Workspace.Host.Persistence;
using Workspace.Host.Windows;

namespace Workspace.Host.Applications;

public sealed class Win32WindowLifecycleService(
    IWindowCatalog windowCatalog,
    IWorkspaceStore? workspaceStore = null,
    Func<nint, bool>? postClose = null) : IWindowLifecycleService
{
    private const uint WmClose = 0x0010;

    public async Task<WindowCloseState> RequestCloseAsync(
        string windowEntityId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(windowEntityId);
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

        var hwnd = await ResolveHwndAsync(windowEntityId, cancellationToken);
        if (hwnd is null) return WindowCloseState.NotRunning;

        _ = (postClose ?? SendClose)(hwnd.Value);
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            var stillPresent = (await windowCatalog.ListAsync(cancellationToken))
                .Any(window => window.Hwnd == hwnd.Value);
            if (!stillPresent) return WindowCloseState.Closed;
        }

        return WindowCloseState.ClosePending;
    }

    private async Task<nint?> ResolveHwndAsync(string windowEntityId, CancellationToken cancellationToken)
    {
        if (workspaceStore is not null)
        {
            var entity = (await workspaceStore.LoadAsync(cancellationToken)).Entities.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, windowEntityId, StringComparison.Ordinal));
            if (entity is null || !TryParseHwnd(entity.HostBinding?.Locator, out var hwnd)) return null;
            return (await windowCatalog.ListAsync(cancellationToken)).Any(window => window.Hwnd == hwnd) ? hwnd : null;
        }

        var candidates = (await windowCatalog.ListAsync(cancellationToken)).Where(window =>
            string.Equals($"pc.window:{window.ApplicationId}", windowEntityId, StringComparison.Ordinal)).ToArray();
        return candidates.Length == 1 ? candidates[0].Hwnd : null;
    }

    private static bool TryParseHwnd(string? locator, out nint hwnd)
    {
        hwnd = nint.Zero;
        return locator?.StartsWith("hwnd:", StringComparison.OrdinalIgnoreCase) == true
            && long.TryParse(locator[5..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
            && (hwnd = (nint)value) != nint.Zero;
    }

    private static bool SendClose(nint hwnd) => PostMessage(hwnd, WmClose, nint.Zero, nint.Zero);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint hWnd, uint message, nint wParam, nint lParam);
}
