using Workspace.Desktop.Core.Agent;
using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class ApprovalCapabilityClassifierTests
{
    [Theory]
    [InlineData("git push origin main", "remote.publish")]
    [InlineData("npm test", "agent.command.npm")]
    [InlineData("winget install Example", "system.configure")]
    public void Classifies_exact_approval_boundary(string command, string expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "workspace-classifier");
        var approval = new AgentApprovalRequested(
            "1", "command", "thread", "turn", "item", command, root, null, DateTimeOffset.UtcNow);

        var classified = ApprovalCapabilityClassifier.Classify(approval, root);

        Assert.Equal(expected, classified.Capability);
        Assert.Equal(Path.GetFullPath(root), classified.Scope);
    }
}
