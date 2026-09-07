namespace Workspace.Host.Applications;

public sealed record ApplicationDescriptor(
    string Id,
    string DisplayName,
    string ExecutablePath,
    string? Arguments);

public sealed record ApplicationLaunchResult(string ApplicationId, int ProcessId);
