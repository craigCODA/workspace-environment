using System.Diagnostics;
using Workspace.Host.Runtime;

namespace Workspace.Host.Tests;

public sealed class HostRuntimeTests
{
    [Fact]
    public void ParentPidIsOptionalForDirectHostLaunch()
    {
        Assert.Null(HostRuntimeOptions.Parse([]).ParentProcessId);
    }

    [Fact]
    public void ParentPidParsesAsTransientRuntimeState()
    {
        var options = HostRuntimeOptions.Parse(["--parent-pid", "4321"]);

        Assert.Equal(4321, options.ParentProcessId);
    }

    [Theory]
    [InlineData("--parent-pid")]
    [InlineData("--parent-pid", "0")]
    [InlineData("--parent-pid", "not-a-number")]
    [InlineData("--unknown")]
    public void InvalidRuntimeArgumentsFailExplicitly(params string[] arguments)
    {
        Assert.Throws<ArgumentException>(() => HostRuntimeOptions.Parse(arguments));
    }

    [Fact]
    public async Task ParentExitCancelsTheHostLifetime()
    {
        using var parent = Process.Start(new ProcessStartInfo(
            "powershell.exe",
            "-NoProfile -NonInteractive -Command Start-Sleep -Milliseconds 250")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        using var shutdown = new CancellationTokenSource();

        await ParentProcessMonitor.CancelWhenParentExitsAsync(
            parent.Id,
            shutdown,
            CancellationToken.None);

        Assert.True(shutdown.IsCancellationRequested);
    }
}
