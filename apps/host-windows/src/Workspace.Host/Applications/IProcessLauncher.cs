using System.Diagnostics;

namespace Workspace.Host.Applications;

public interface IProcessLauncher
{
    Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken);
}

public sealed record ApplicationStartRequest(
    ApplicationLaunchKind LaunchKind,
    string Locator,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory);

public sealed class SystemProcessLauncher : IProcessLauncher
{
    private readonly Func<ProcessStartInfo, int?> _startProcess;

    public SystemProcessLauncher(Func<ProcessStartInfo, int?>? startProcess = null)
    {
        _startProcess = startProcess ?? StartProcess;
    }

    public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Locator);
        ArgumentNullException.ThrowIfNull(request.Arguments);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.Locator,
            UseShellExecute = request.LaunchKind is not ApplicationLaunchKind.Executable,
        };
        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return Task.FromResult(_startProcess(startInfo));
    }

    private static int? StartProcess(ProcessStartInfo startInfo) => Process.Start(startInfo)?.Id;
}
