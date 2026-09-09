using System.Diagnostics;

namespace Workspace.Host.Applications;

public interface IProcessLauncher
{
    Task<int> LaunchAsync(string executablePath, string? arguments, CancellationToken cancellationToken);
}

public sealed class SystemProcessLauncher : IProcessLauncher
{
    public Task<int> LaunchAsync(string executablePath, string? arguments, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Windows did not return a process for '{executablePath}'.");
        return Task.FromResult(process.Id);
    }
}
