using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Workspace.Host.Windows;

public sealed class Win32WindowCatalog(Func<int, string> resolveApplicationId) : IWindowCatalog
{
    public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<WindowSnapshot>>(
            () => Discover(resolveApplicationId, cancellationToken),
            cancellationToken);
    }

    private static IReadOnlyList<WindowSnapshot> Discover(
        Func<int, string> resolveApplicationId,
        CancellationToken cancellationToken)
    {
        var windows = new List<WindowSnapshot>();

        var enumerated = EnumWindows((hwnd, _) =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (GetWindowThreadProcessId(hwnd, out var processId) == 0)
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var bounds))
            {
                return true;
            }

            var title = GetWindowTitle(hwnd);
            var applicationId = resolveApplicationId((int)processId);
            ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);

            windows.Add(new WindowSnapshot(
                hwnd,
                (int)processId,
                title,
                new WindowBounds(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right - bounds.Left,
                    bounds.Bottom - bounds.Top),
                IsWindowVisible(hwnd),
                IsIconic(hwnd),
                applicationId));

            return true;
        }, nint.Zero);

        if (!enumerated)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "Unable to enumerate top-level Windows windows.");
        }

        return windows;
    }

    private static string GetWindowTitle(nint hwnd)
    {
        var title = new StringBuilder(GetWindowTextLength(hwnd) + 1);
        GetWindowText(hwnd, title, title.Capacity);
        return title.ToString();
    }

    private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
