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
    public Task<int?> LaunchAsync(ApplicationStartRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Locator);

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

        var process = Process.Start(startInfo);
        return Task.FromResult<int?>(process?.Id);
    }
}
