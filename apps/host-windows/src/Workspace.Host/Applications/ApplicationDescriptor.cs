namespace Workspace.Host.Applications;

public enum ApplicationLaunchKind
{
    Executable,
    Shortcut,
    Packaged,
}

public sealed record ApplicationDescriptor(
    string Id,
    string DisplayName,
    ApplicationLaunchKind LaunchKind,
    string Locator,
    IReadOnlyList<string> Aliases)
{
    // Temporary compatibility surface. Task 2 migrates existing callers to Locator and structured launch data.
    public ApplicationDescriptor(
        string id,
        string displayName,
        string executablePath,
        string? arguments)
        : this(id, displayName, ApplicationLaunchKind.Executable, executablePath, Array.Empty<string>())
    {
        Arguments = arguments;
    }

    public string ExecutablePath => Locator;

    public string? Arguments { get; init; }
}

public sealed record ApplicationLaunchResult(string ApplicationId, int? ProcessId);
