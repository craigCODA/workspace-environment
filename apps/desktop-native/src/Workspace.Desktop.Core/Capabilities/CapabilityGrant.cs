namespace Workspace.Desktop.Core.Capabilities;

public sealed record CapabilityGrant(
    string Capability,
    string Scope,
    DateTimeOffset? ExpiresAt)
{
    public DateTimeOffset ApprovedAt { get; init; } = DateTimeOffset.UtcNow;
}

internal sealed record CapabilityGrantDocument(
    int SchemaVersion,
    IReadOnlyList<CapabilityGrant> Grants)
{
    public const int CurrentSchemaVersion = 1;

    public static CapabilityGrantDocument Empty { get; } = new(
        CurrentSchemaVersion,
        Array.Empty<CapabilityGrant>());
}
