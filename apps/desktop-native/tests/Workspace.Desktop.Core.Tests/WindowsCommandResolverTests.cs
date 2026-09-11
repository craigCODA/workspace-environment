using Workspace.Desktop.Windows.Agent;

namespace Workspace.Desktop.Core.Tests;

public sealed class WindowsCommandResolverTests
{
    [Fact]
    public void Resolves_cmd_shim_when_no_direct_executable_exists()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-codex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var shim = Path.Combine(root, "codex.cmd");
            File.WriteAllText(shim, "@echo off\r\n");

            var resolved = WindowsCommandResolver.Resolve("codex", root);

            Assert.Equal(Path.GetFullPath(Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe"), resolved.FileName);
            Assert.Equal(new[] { "/d", "/c", Path.GetFullPath(shim) }, resolved.PrefixArguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Prefers_direct_executable_over_command_shim()
    {
        var root = Path.Combine(Path.GetTempPath(), $"workspace-codex-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "codex.exe");
            File.WriteAllBytes(executable, []);
            File.WriteAllText(Path.Combine(root, "codex.cmd"), "@echo off\r\n");

            var resolved = WindowsCommandResolver.Resolve("codex", root);

            Assert.Equal(Path.GetFullPath(executable), resolved.FileName);
            Assert.Empty(resolved.PrefixArguments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
