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

    public static WorkspaceEntity CreateWindow(
        string id,
        string name,
        string applicationId,
        string surfaceState = "available")
    {
        return new WorkspaceEntity(
            id,
            EntityKinds.Window,
            name,
            new Dictionary<string, JsonElement>
            {
                ["applicationId"] = JsonSerializer.SerializeToElement(applicationId),
                ["surfaceState"] = JsonSerializer.SerializeToElement(surfaceState),
            },
            [new Relationship("belongs-to", applicationId)],
            ["focus", "input", "setPresentation", "capture"],
            new HostBinding("window", $"{applicationId}:main"),
            PresentationState.Default with
            {
                Position = new Vec3(0, 1.4, -3),
                Size = new Vec3(3.2, 1.8, 1),
                Representation = "application-surface",
            });
    }
}
