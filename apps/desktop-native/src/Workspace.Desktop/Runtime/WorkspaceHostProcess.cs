using System.Diagnostics;
using System.Text;

namespace Workspace.Desktop.Runtime;

public sealed class WorkspaceHostProcess : IAsyncDisposable
{
    public const string ReadinessMarker =
        "Workspace Host listening at ws://127.0.0.1:41771/workspace";

    private readonly CancellationTokenSource _lifetime = new();
    private readonly StringBuilder _standardError = new();
    private Process? _process;
    private Task? _outputTask;
    private Task? _errorTask;
    private bool _disposed;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_process is { HasExited: false })
        {
            return;
        }

        var startInfo = ResolveStartInfo();
        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows Workspace Host did not start.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outputTask = ReadOutputAsync(_process.StandardOutput, ready, _lifetime.Token);
        _errorTask = ReadErrorAsync(_process.StandardError, _lifetime.Token);
        _ = ObserveEarlyExitAsync(_process, ready);

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new InvalidOperationException(
                "Windows Workspace Host did not become ready within 30 seconds.");
        }
    }

    private static ProcessStartInfo ResolveStartInfo()
    {
        var bundledHost = Path.Combine(AppContext.BaseDirectory, "host", "Workspace.Host.exe");
        if (File.Exists(bundledHost))
        {
            return CreateStartInfo(bundledHost, Path.GetDirectoryName(bundledHost)!);
        }

        var repository = FindRepositoryRoot()
            ?? throw new FileNotFoundException(
                "The bundled Workspace Host is missing and a development checkout could not be found.");
        var configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        var builtHost = Path.Combine(
            repository,
            "apps",
            "host-windows",
            "src",
            "Workspace.Host",
            "bin",
            configuration,
            "net8.0-windows10.0.19041.0",
            "Workspace.Host.exe");
        if (File.Exists(builtHost))
        {
            return CreateStartInfo(builtHost, Path.GetDirectoryName(builtHost)!);
        }

        var project = Path.Combine(
            repository,
            "apps",
            "host-windows",
            "src",
            "Workspace.Host",
            "Workspace.Host.csproj");
        var fallback = BaseStartInfo("dotnet", repository);
        fallback.ArgumentList.Add("run");
        fallback.ArgumentList.Add("--project");
        fallback.ArgumentList.Add(project);
        fallback.ArgumentList.Add("--");
        fallback.ArgumentList.Add("--parent-pid");
        fallback.ArgumentList.Add(Environment.ProcessId.ToString());
        return fallback;
    }

    private static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory)
    {
        var startInfo = BaseStartInfo(executable, workingDirectory);
        startInfo.ArgumentList.Add("--parent-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        return startInfo;
    }

    private static ProcessStartInfo BaseStartInfo(string executable, string workingDirectory) => new()
    {
        FileName = executable,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardInput = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden,
    };

    private static string? FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "package.json"))
                && Directory.Exists(Path.Combine(directory.FullName, "apps", "spatial-client")))
            {
                return directory.FullName;
            }
        }
        return null;
    }

    private static async Task ReadOutputAsync(
        StreamReader output,
        TaskCompletionSource ready,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await output.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.Equals(line.Trim(), ReadinessMarker, StringComparison.Ordinal))
                {
                    ready.TrySetResult();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ReadErrorAsync(StreamReader error, CancellationToken cancellationToken)
    {
        try
        {
            while (await error.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (_standardError.Length < 16_384)
                {
                    _standardError.AppendLine(line);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task ObserveEarlyExitAsync(Process process, TaskCompletionSource ready)
    {
        await process.WaitForExitAsync().ConfigureAwait(false);
        if (!ready.Task.IsCompleted)
        {
            var detail = _standardError.ToString().Trim();
            ready.TrySetException(new InvalidOperationException(
                $"Windows Workspace Host exited with code {process.ExitCode}. {detail}".Trim()));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _lifetime.Cancel();
        if (_process is { HasExited: false })
        {
            _process.Kill(entireProcessTree: true);
        }
        if (_process is not null)
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            _process.Dispose();
        }
        _lifetime.Dispose();
    }
}
