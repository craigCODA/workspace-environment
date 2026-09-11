using System.Diagnostics;
using System.IO.Pipes;
using Workspace.Desktop.Core.Launcher;

return await LauncherProgram.RunAsync(args);

internal static class LauncherProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = LauncherOptions.Parse(args);
        Directory.CreateDirectory(options.StateRoot);
        var activation = new ActivationStore(Path.Combine(options.StateRoot, "activation.json"));

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var state = await activation.LoadAsync();
            var versionId = VersionSelector.Select(state);
            if (string.IsNullOrWhiteSpace(versionId)) return 2;
            var versionDirectory = Path.Combine(options.VersionsRoot, versionId);

            VersionManifest manifest;
            try
            {
                manifest = await VersionManifest.LoadAndValidateAsync(versionDirectory);
            }
            catch when (state.PendingVersion is not null && attempt == 0)
            {
                await activation.SaveAsync(VersionSelector.MarkFailed(state, versionId));
                continue;
            }

            var pipeName = $"WorkspaceEnvironment.Health.{Guid.NewGuid():N}";
            var token = Guid.NewGuid().ToString("N");
            await using var pipe = new NamedPipeServerStream(
                pipeName,
                PipeDirection.In,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            using var process = StartDesktop(
                VersionManifest.ResolveContained(versionDirectory, manifest.EntryPoint),
                versionDirectory,
                pipeName,
                token,
                options.StateRoot,
                options.SourceRoot);

            var healthy = await WaitForHealthAsync(
                pipe,
                token,
                process,
                TimeSpan.FromSeconds(options.HealthTimeoutSeconds));
            if (!healthy)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                if (state.PendingVersion is not null && attempt == 0)
                {
                    await activation.SaveAsync(VersionSelector.MarkFailed(state, versionId));
                    continue;
                }
                return 3;
            }

            await activation.SaveAsync(VersionSelector.MarkHealthy(state, versionId));
            if (options.Once) return 0;
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        return 4;
    }

    private static Process StartDesktop(
        string executable,
        string workingDirectory,
        string pipeName,
        string token,
        string stateRoot,
        string? sourceRoot)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--health-pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--health-token");
        startInfo.ArgumentList.Add(token);
        startInfo.ArgumentList.Add("--state-root");
        startInfo.ArgumentList.Add(stateRoot);
        if (!string.IsNullOrWhiteSpace(sourceRoot))
        {
            startInfo.ArgumentList.Add("--source-root");
            startInfo.ArgumentList.Add(Path.GetFullPath(sourceRoot));
        }
        return Process.Start(startInfo)
            ?? throw new InvalidOperationException("Workspace Desktop did not start.");
    }

    private static async Task<bool> WaitForHealthAsync(
        NamedPipeServerStream pipe,
        string token,
        Process process,
        TimeSpan timeout)
    {
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        try
        {
            var connection = pipe.WaitForConnectionAsync(timeoutCancellation.Token);
            var exit = process.WaitForExitAsync(timeoutCancellation.Token);
            if (await Task.WhenAny(connection, exit) != connection) return false;
            await connection;
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var received = await reader.ReadLineAsync(timeoutCancellation.Token);
            return string.Equals(received, token, StringComparison.Ordinal);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

internal sealed record LauncherOptions(
    string StateRoot,
    string VersionsRoot,
    string? SourceRoot,
    bool Once,
    int HealthTimeoutSeconds)
{
    public static LauncherOptions Parse(string[] args)
    {
        var stateRoot = Value(args, "--state-root") ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkspaceEnvironment");
        var versionsRoot = Value(args, "--versions-root") ?? Path.Combine(stateRoot, "versions");
        var timeoutText = Value(args, "--health-timeout-seconds");
        var timeout = int.TryParse(timeoutText, out var value) ? Math.Clamp(value, 5, 120) : 45;
        return new(
            Path.GetFullPath(stateRoot),
            Path.GetFullPath(versionsRoot),
            Value(args, "--source-root"),
            args.Contains("--once", StringComparer.OrdinalIgnoreCase),
            timeout);
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.FindIndex(args, argument =>
            argument.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
