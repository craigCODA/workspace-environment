using System.Text.Json;
using System.Text.Json.Nodes;
using Workspace.Host.Domain;

namespace Workspace.Host.Persistence;

public sealed class WorkspaceDocument : IEquatable<WorkspaceDocument>
{
    public const int CurrentSchemaVersion = 2;

    public WorkspaceDocument(int schemaVersion, List<WorkspaceEntity> entities)
    {
        SchemaVersion = schemaVersion;
        Entities = entities;
    }

    public int SchemaVersion { get; init; }

    public List<WorkspaceEntity> Entities { get; init; }

    public static WorkspaceDocument Empty => new(CurrentSchemaVersion, []);

    public WorkspaceDocument MigrateToCurrent() => WorkspaceMigrator.MigrateToCurrent(this);

    public bool TrySetPresentation(
        string entityId,
        PresentationState presentation,
        out WorkspaceEntity? updated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        ArgumentNullException.ThrowIfNull(presentation);

        var index = Entities.FindIndex(entity =>
            string.Equals(entity.Id, entityId, StringComparison.Ordinal));
        if (index < 0)
        {
            updated = null;
            return false;
        }

        updated = Entities[index] with { Presentation = presentation };
        Entities[index] = updated;
        return true;
    }

    public bool TryBindWindow(
        string surfaceId,
        string windowId,
        out WorkspaceEntity? updated)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(surfaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(windowId);

        var surfaceIndex = Entities.FindIndex(entity =>
            string.Equals(entity.Id, surfaceId, StringComparison.Ordinal));
        var window = Entities.FirstOrDefault(entity =>
            string.Equals(entity.Id, windowId, StringComparison.Ordinal));

        if (surfaceIndex < 0
            || Entities[surfaceIndex].Kind != EntityKinds.Surface
            || window?.Kind != EntityKinds.Window)
        {
            updated = null;
            return false;
        }

        var surface = Entities[surfaceIndex];
        updated = surface with
        {
            Relationships =
            [
                .. surface.Relationships.Where(relationship => relationship.Type != "displays"),
                new Relationship("displays", window.Id),
            ],
        };
        Entities[surfaceIndex] = updated;
        return true;
    }

    public bool Equals(WorkspaceDocument? other)
    {
        if (other is null)
        {
            return false;
        }

        var left = JsonSerializer.SerializeToNode(this);
        var right = JsonSerializer.SerializeToNode(other);
        return JsonNode.DeepEquals(left, right);
    }

    public override bool Equals(object? obj) => obj is WorkspaceDocument other && Equals(other);

    public override int GetHashCode() => JsonSerializer.Serialize(this).GetHashCode(StringComparison.Ordinal);
}
