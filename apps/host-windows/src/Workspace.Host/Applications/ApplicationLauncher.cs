namespace Workspace.Host.Applications;

public sealed class ApplicationLauncher(IProcessLauncher processLauncher)
{
    public async Task<ApplicationLaunchResult> LaunchAsync(
        ApplicationDescriptor application,
        CancellationToken cancellationToken)
    {
        var arguments = string.IsNullOrWhiteSpace(application.Arguments)
            ? Array.Empty<string>()
            : [application.Arguments];
        return await LaunchAsync(application, arguments, null, cancellationToken);
    }

    public async Task<ApplicationLaunchResult> LaunchAsync(
        ApplicationDescriptor application,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(arguments);

        var processId = await processLauncher.LaunchAsync(
            new ApplicationStartRequest(
                application.LaunchKind,
                application.Locator,
                arguments,
                workingDirectory),
            cancellationToken);

        return new ApplicationLaunchResult(application.Id, processId);
    }
}
