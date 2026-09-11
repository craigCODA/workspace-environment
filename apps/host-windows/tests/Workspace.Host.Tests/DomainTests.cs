using Workspace.Host.Domain;

namespace Workspace.Host.Tests;

public sealed class DomainTests
{
    [Fact]
    public void PresentationChangeDoesNotChangeSemanticIdentity()
    {
        var entity = WorkspaceEntity.CreateApplication("app:microsoft-edge", "Microsoft Edge");
        var moved = entity with
        {
            Presentation = entity.Presentation with
            {
                Position = new Vec3(2, 1, -3),
            },
        };

        Assert.Equal(entity.Id, moved.Id);
        Assert.Equal(entity.HostBinding, moved.HostBinding);
    }

    [Fact]
    public void DisplaySurface_has_spatial_identity_and_surface_capabilities()
    {
        var surface = WorkspaceEntity.CreateDisplaySurface(
            "spatial.surface:desk",
            "Desk",
            PresentationState.Default);

        Assert.Equal(EntityKinds.Surface, surface.Kind);
        Assert.Equal(["bindWindow", "setPresentation", "select"], surface.Capabilities);
        Assert.Empty(surface.Relationships);
        Assert.Null(surface.HostBinding);
    }
}
