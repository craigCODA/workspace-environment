using Workspace.Desktop.Core.Agent;

namespace Workspace.Desktop.Core.Runtime;

public sealed record ApprovalCapability(string Capability, string Scope);

public static class ApprovalCapabilityClassifier
{
    public static ApprovalCapability Classify(AgentApprovalRequested approval, string workspaceRoot)
    {
        var summary = approval.CommandSummary.Trim();
        var normalized = $"{approval.Method} {summary} {approval.Reason}".ToLowerInvariant();
        var scope = string.IsNullOrWhiteSpace(approval.WorkingDirectory)
            ? Path.GetFullPath(workspaceRoot)
            : Path.GetFullPath(approval.WorkingDirectory);

        if (ContainsAny(normalized, "git push", "npm publish", "dotnet nuget push", "gh release"))
            return new("remote.publish", scope);
        if (ContainsAny(normalized, "credential", "password", "secret", "api key", "token"))
            return new("credential.read", scope);
        if (ContainsAny(normalized, "remove-item -recurse", "rm -rf", "del /s", "format ", "diskpart"))
            return new("filesystem.destructive-outside-workspace", scope);
        if (ContainsAny(normalized, "winget install", "choco install", "setx ", "reg add", "system configure"))
            return new("system.configure", scope);
        if (normalized.Contains("file", StringComparison.Ordinal)
            && ContainsAny(normalized, "write", "edit", "patch", "change"))
            return new("agent.file-change", scope);

        var executable = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        executable = Path.GetFileNameWithoutExtension(executable ?? "command").ToLowerInvariant();
        return new($"agent.command.{(executable.Length == 0 ? "command" : executable)}", scope);
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.Ordinal));
}
