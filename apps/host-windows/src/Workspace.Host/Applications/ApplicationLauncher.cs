namespace Workspace.Host.Applications;

public sealed class ApplicationLauncher(IProcessLauncher processLauncher)
{
    public async Task<ApplicationLaunchResult> LaunchAsync(
        ApplicationDescriptor application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var processId = await processLauncher.LaunchAsync(
            application.ExecutablePath,
            application.Arguments,
            cancellationToken);

        return new ApplicationLaunchResult(application.Id, processId);
    }
}
