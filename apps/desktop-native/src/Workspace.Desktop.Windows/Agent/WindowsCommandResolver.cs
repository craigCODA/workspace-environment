namespace Workspace.Desktop.Windows.Agent;

public sealed record ResolvedWindowsCommand(
    string FileName,
    IReadOnlyList<string> PrefixArguments);

public static class WindowsCommandResolver
{
    public static ResolvedWindowsCommand Resolve(string commandName, string? path = null)
    {
        if (string.IsNullOrWhiteSpace(commandName))
        {
            throw new ArgumentException("A command name is required.", nameof(commandName));
        }

        var searchPath = path ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directoryValue in searchPath.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = directoryValue.Trim().Trim('"');
            if (directory.Length == 0)
            {
                continue;
            }

            var executable = Path.Combine(directory, commandName + ".exe");
            if (File.Exists(executable))
            {
                return new ResolvedWindowsCommand(
                    Path.GetFullPath(executable),
                    Array.Empty<string>());
            }

            foreach (var extension in new[] { ".cmd", ".bat" })
            {
                var shim = Path.Combine(directory, commandName + extension);
                if (!File.Exists(shim))
                {
                    continue;
                }

                var commandProcessor = Environment.GetEnvironmentVariable("ComSpec");
                if (string.IsNullOrWhiteSpace(commandProcessor))
                {
                    commandProcessor = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.System),
                        "cmd.exe");
                }

                return new ResolvedWindowsCommand(
                    Path.GetFullPath(commandProcessor),
                    new[] { "/d", "/c", Path.GetFullPath(shim) });
            }
        }

        throw new FileNotFoundException(
            $"{commandName} was not found on PATH. Install Codex and sign in with your ChatGPT subscription.");
    }
}
