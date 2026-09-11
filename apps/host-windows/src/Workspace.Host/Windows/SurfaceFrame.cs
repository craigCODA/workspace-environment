namespace Workspace.Host.Windows;

public sealed record SurfaceFrame(
    string StreamId,
    long Sequence,
    int Width,
    int Height,
    string MimeType,
    byte[] Data);
