namespace Workspace.Host.Windows;

public sealed record WindowInputIntent(
    string Kind,
    string? Phase = null,
    double? X = null,
    double? Y = null,
    string? Button = null,
    double? DeltaX = null,
    double? DeltaY = null,
    string? Key = null,
    string? Text = null);

public sealed record ScreenPoint(int X, int Y);

public static class WindowInputLimits
{
    public const int MaximumTextLength = 4_096;
}

public interface IInputRouter
{
    Task RouteAsync(
        WindowSnapshot window,
        WindowInputIntent intent,
        CancellationToken cancellationToken);

    Task ReleaseAllAsync(CancellationToken cancellationToken);
}

public sealed class InputTargetNotPermittedException(string message) : Exception(message);

public static class InputCoordinateMapper
{
    public static ScreenPoint Map(double x, double y, WindowBounds bounds)
    {
        if (!double.IsFinite(x) || x < 0 || x > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Surface X must be between 0 and 1.");
        }
        if (!double.IsFinite(y) || y < 0 || y > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(y), "Surface Y must be between 0 and 1.");
        }
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Window bounds must have a positive size.");
        }

        return new ScreenPoint(
            checked(bounds.Left + (int)Math.Round(x * bounds.Width, MidpointRounding.AwayFromZero)),
            checked(bounds.Top + (int)Math.Round(y * bounds.Height, MidpointRounding.AwayFromZero)));
    }
}
