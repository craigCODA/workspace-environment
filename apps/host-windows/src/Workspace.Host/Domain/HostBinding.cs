namespace Workspace.Host.Domain;

public sealed record HostBinding(
    string Type,
    string Locator,
    string? LaunchDescriptor = null);
