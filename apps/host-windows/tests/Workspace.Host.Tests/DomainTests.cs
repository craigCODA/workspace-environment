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
}
