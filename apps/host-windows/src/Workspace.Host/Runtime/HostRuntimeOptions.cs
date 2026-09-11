namespace Workspace.Host.Runtime;

public sealed record HostRuntimeOptions(int? ParentProcessId)
{
    public static HostRuntimeOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return new HostRuntimeOptions((int?)null);
        }

        if (arguments.Count == 2
            && string.Equals(arguments[0], "--parent-pid", StringComparison.Ordinal)
            && int.TryParse(arguments[1], out var parentProcessId)
            && parentProcessId > 0)
        {
            return new HostRuntimeOptions(parentProcessId);
        }

        throw new ArgumentException(
            "Supported arguments: --parent-pid <positive process id>.",
            nameof(arguments));
    }
}
