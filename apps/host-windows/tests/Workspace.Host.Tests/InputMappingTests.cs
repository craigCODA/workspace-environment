using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class InputMappingTests
{
    [Fact]
    public void HeldKeysAreReleasedInReverseOrderAfterAnInterruptedChord()
    {
        var tracker = new HeldKeyTracker();
        var routed = new List<string>();

        tracker.Route(0x11, keyUp: false, () => routed.Add("control-down"), () => { });
        tracker.Route(0x4C, keyUp: false, () => routed.Add("l-down"), () => { });
        tracker.Route(0x4C, keyUp: true, () => routed.Add("l-up"), () => { });
        var released = tracker.DrainHeldKeys();

        Assert.Equal(["control-down", "l-down", "l-up"], routed);
        Assert.Equal([0x11], released);
        Assert.Empty(tracker.DrainHeldKeys());
    }

    [Fact]
    public void FailedKeyUpUsesEmergencyReleaseAndClearsTrackedState()
    {
        var tracker = new HeldKeyTracker();
        var emergencyReleases = new List<ushort>();
        tracker.Route(0x12, keyUp: false, () => { }, () => { });

        Assert.Throws<InvalidOperationException>(() => tracker.Route(
            0x12,
            keyUp: true,
            () => throw new InvalidOperationException("foreground changed"),
            () => emergencyReleases.Add(0x12)));

        Assert.Equal([0x12], emergencyReleases);
        Assert.Empty(tracker.DrainHeldKeys());
    }

    [Fact]
    public async Task HeldKeyLeaseExpiresEvenWhileTheClientRemainsConnected()
    {
        var expiration = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = new TaskCompletionSource<ushort>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tracker = new HeldKeyTracker(
            TimeSpan.FromSeconds(5),
            (_, _) => expiration.Task);

        tracker.Route(
            0x11,
            keyUp: false,
            () => { },
            () => released.TrySetResult(0x11));
        expiration.SetResult();

        Assert.Equal((ushort)0x11, await released.Task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.Empty(tracker.DrainHeldKeys());
    }

    [Theory]
    [InlineData(0.0, 0.0, 100, 200)]
    [InlineData(1.0, 1.0, 900, 800)]
    [InlineData(0.5, 0.5, 500, 500)]
    public void MapsNormalizedCoordinates(
        double x,
        double y,
        int expectedX,
        int expectedY)
    {
        var result = InputCoordinateMapper.Map(
            x,
            y,
            new WindowBounds(100, 200, 800, 600));

        Assert.Equal((expectedX, expectedY), (result.X, result.Y));
    }

    [Theory]
    [InlineData(-0.01, 0.5)]
    [InlineData(1.01, 0.5)]
    [InlineData(0.5, -0.01)]
    [InlineData(0.5, 1.01)]
    public void RejectsCoordinatesOutsideTheNormalizedSurface(double x, double y)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            InputCoordinateMapper.Map(x, y, new WindowBounds(0, 0, 800, 600)));
    }

    [Fact]
    public async Task FocusServiceResolvesSemanticIdentityToTheCurrentHwnd()
    {
        var snapshot = new WindowSnapshot(
            (nint)777,
            42,
            "Current title",
            new WindowBounds(0, 0, 800, 600),
            true,
            false,
            "pc.application:test");
        await using var reconciler = new WindowReconciler();
        nint focused = nint.Zero;
        var service = new Win32WindowFocusService(
            new StaticWindowCatalog(snapshot),
            reconciler,
            hwnd =>
            {
                focused = hwnd;
                return true;
            });

        await service.FocusAsync("pc.window:pc.application:test", CancellationToken.None);

        Assert.Equal((nint)777, focused);
    }

    [Fact]
    public async Task FocusServiceResolvesTheExactPersistedHwndSpecificWindowIdentity()
    {
        var first = new WindowSnapshot((nint)777, 42, "First", new WindowBounds(0, 0, 800, 600), true, false, "pc.application:test");
        var second = new WindowSnapshot((nint)778, 43, "Second", new WindowBounds(0, 0, 800, 600), true, false, "pc.application:test");
        await using var reconciler = new WindowReconciler();
        nint focused = nint.Zero;
        var service = new Win32WindowFocusService(new StaticWindowCatalog(first, second), reconciler,
            hwnd => { focused = hwnd; return true; });

        await service.FocusAsync("pc.window:pc.application:test:30A", CancellationToken.None);

        Assert.Equal((nint)778, focused);
    }

    private sealed class StaticWindowCatalog(params WindowSnapshot[] windows) : IWindowCatalog
    {
        public Task<IReadOnlyList<WindowSnapshot>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WindowSnapshot>>(windows);
    }
}
