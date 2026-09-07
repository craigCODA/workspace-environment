using Workspace.Host.Windows;

namespace Workspace.Host.Tests;

public sealed class WindowReconcilerTests
{
    [Fact]
    public void RecreatedMainWindowKeepsSemanticIdentity()
    {
        var first = new WindowSnapshot(
            (nint)100,
            10,
            "Workspace Test Window",
            new WindowBounds(0, 0, 800, 600),
            true,
            false,
            "pc.application:workspace-test");
        var second = first with
        {
            Hwnd = (nint)900,
            ProcessId = 22,
            Title = "Workspace Test Window - changed runtime title",
        };
        var reconciler = new WindowReconciler();

        Assert.Equal(reconciler.ResolveEntityId(first), reconciler.ResolveEntityId(second));
    }

    [Fact]
    public void DifferentApplicationsDoNotShareMainWindowIdentity()
    {
        var edge = new WindowSnapshot((nint)1, 10, "Browser", new WindowBounds(0, 0, 800, 600), true, false, "pc.application:edge");
        var cursor = edge with { Hwnd = (nint)2, ProcessId = 20, ApplicationId = "pc.application:cursor" };
        var reconciler = new WindowReconciler();

        Assert.NotEqual(reconciler.ResolveEntityId(edge), reconciler.ResolveEntityId(cursor));
    }
}
