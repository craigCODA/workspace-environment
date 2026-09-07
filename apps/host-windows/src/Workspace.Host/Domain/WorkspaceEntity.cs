using System.Text.Json;

namespace Workspace.Host.Domain;

public sealed record Relationship(string Type, string TargetId);

public sealed record WorkspaceEntity(
    string Id,
    string Kind,
    string Name,
    Dictionary<string, JsonElement> Properties,
    List<Relationship> Relationships,
    List<string> Capabilities,
    HostBinding? HostBinding,
    PresentationState Presentation)
{
    public static WorkspaceEntity CreateApplication(string id, string name)
    {
        return new WorkspaceEntity(
            id,
            EntityKinds.Application,
            name,
            [],
            [],
            ["open", "focus"],
            new HostBinding("application", name),
            PresentationState.Default);
    }
}
