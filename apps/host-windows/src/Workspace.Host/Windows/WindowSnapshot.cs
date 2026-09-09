namespace Workspace.Host.Windows;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

public sealed record WindowSnapshot(
    nint Hwnd,
    int ProcessId,
    string Title,
    WindowBounds Bounds,
    bool IsVisible,
    bool IsMinimized,
    string ApplicationId);
