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

public sealed class HeldKeyTracker
{
    private readonly object _gate = new();
    private readonly List<HeldKeyLease> _heldKeys = [];
    private readonly TimeSpan _maximumHold;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public HeldKeyTracker()
        : this(TimeSpan.FromSeconds(5), Task.Delay)
    {
    }

    public HeldKeyTracker(
        TimeSpan maximumHold,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        if (maximumHold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumHold));
        }
        ArgumentNullException.ThrowIfNull(delay);
        _maximumHold = maximumHold;
        _delay = delay;
    }

    public void Route(
        ushort virtualKey,
        bool keyUp,
        Action send,
        Action emergencyRelease)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(emergencyRelease);

        if (!keyUp)
        {
            send();
            var lease = new HeldKeyLease(
                virtualKey,
                emergencyRelease,
                new CancellationTokenSource());
            HeldKeyLease? previous = null;
            lock (_gate)
            {
                var index = _heldKeys.FindIndex(candidate => candidate.VirtualKey == virtualKey);
                if (index >= 0)
                {
                    previous = _heldKeys[index];
                    _heldKeys[index] = lease;
                }
                else
                {
                    _heldKeys.Add(lease);
                }
            }
            Cancel(previous);
            _ = ReleaseExpiredAsync(lease);
            return;
        }

        try
        {
            send();
        }
        catch
        {
            emergencyRelease();
            throw;
        }
        finally
        {
            Cancel(Remove(virtualKey));
        }
    }

    public IReadOnlyList<ushort> DrainHeldKeys()
    {
        HeldKeyLease[] leases;
        lock (_gate)
        {
            leases = _heldKeys.AsEnumerable().Reverse().ToArray();
            _heldKeys.Clear();
        }
        foreach (var lease in leases)
        {
            Cancel(lease);
        }
        return leases.Select(lease => lease.VirtualKey).ToArray();
    }

    private async Task ReleaseExpiredAsync(HeldKeyLease lease)
    {
        try
        {
            await _delay(_maximumHold, lease.Expiration.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            return;
        }

        var expired = Remove(lease.VirtualKey, lease);
        if (expired is null) return;

        try
        {
            expired.EmergencyRelease();
        }
        catch
        {
            // Emergency release is best effort after the target or session disappears.
        }
        finally
        {
            expired.Expiration.Dispose();
        }
    }

    private HeldKeyLease? Remove(ushort virtualKey, HeldKeyLease? expected = null)
    {
        lock (_gate)
        {
            var index = _heldKeys.FindIndex(candidate =>
                candidate.VirtualKey == virtualKey
                && (expected is null || ReferenceEquals(candidate, expected)));
            if (index < 0) return null;
            var lease = _heldKeys[index];
            _heldKeys.RemoveAt(index);
            return lease;
        }
    }

    private static void Cancel(HeldKeyLease? lease)
    {
        if (lease is null) return;
        lease.Expiration.Cancel();
        lease.Expiration.Dispose();
    }

    private sealed record HeldKeyLease(
        ushort VirtualKey,
        Action EmergencyRelease,
        CancellationTokenSource Expiration);
}

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
