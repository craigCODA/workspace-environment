using System.Text.Json;
using System.Text.Json.Nodes;
using Workspace.Host.Domain;

namespace Workspace.Host.Persistence;

public sealed class WorkspaceDocument : IEquatable<WorkspaceDocument>
{
    public WorkspaceDocument(int schemaVersion, List<WorkspaceEntity> entities)
    {
        SchemaVersion = schemaVersion;
        Entities = entities;
    }

    public int SchemaVersion { get; init; }

    public List<WorkspaceEntity> Entities { get; init; }

    public static WorkspaceDocument Empty { get; } = new(1, []);

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
