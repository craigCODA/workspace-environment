using System.Text;

namespace Workspace.Host.Applications;

public sealed class ApplicationLauncher(IProcessLauncher processLauncher)
{
    public async Task<ApplicationLaunchResult> LaunchAsync(
        ApplicationDescriptor application,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        var arguments = SplitLegacyArguments(application.Arguments);
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

    private static IReadOnlyList<string> SplitLegacyArguments(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return Array.Empty<string>();
        }

        var arguments = new List<string>();
        var argument = new StringBuilder();
        var inQuotes = false;
        var backslashCount = 0;

        void AddArgument()
        {
            if (argument.Length > 0)
            {
                arguments.Add(argument.ToString());
                argument.Clear();
            }
        }

        foreach (var character in commandLine)
        {
            if (character == '\\')
            {
                backslashCount++;
                continue;
            }

            if (character == '"')
            {
                argument.Append('\\', backslashCount / 2);
                if (backslashCount % 2 == 1)
                {
                    argument.Append('"');
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                backslashCount = 0;
                continue;
            }

            argument.Append('\\', backslashCount);
            backslashCount = 0;
            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                AddArgument();
            }
            else
            {
                argument.Append(character);
            }
        }

        argument.Append('\\', backslashCount);
        AddArgument();
        return arguments;
    }
}
