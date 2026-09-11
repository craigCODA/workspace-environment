using Workspace.Desktop.Core.Launcher;

namespace Workspace.Desktop.Core.Tests;

public sealed class VersionSelectorTests
{
    [Fact]
    public void Pending_version_is_selected_and_becomes_known_good_only_after_health()
    {
        var state = VersionSelector.Stage(ActivationState.Empty, "v1");
        Assert.Equal("v1", VersionSelector.Select(state));
        Assert.Null(state.KnownGoodVersion);

        var healthy = VersionSelector.MarkHealthy(state, "v1");
        Assert.Equal("v1", healthy.ActiveVersion);
        Assert.Equal("v1", healthy.KnownGoodVersion);
        Assert.Null(healthy.PendingVersion);
    }

    [Fact]
    public void Failed_pending_version_rolls_back_to_known_good()
    {
        var state = new ActivationState(1, "v1", "v2", "v1");
        var rolledBack = VersionSelector.MarkFailed(state, "v2");
        Assert.Equal("v1", VersionSelector.Select(rolledBack));
        Assert.Null(rolledBack.PendingVersion);
    }
}
