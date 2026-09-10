using System.Text.Json;
using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class WorkspaceActionPolicyTests
{
    [Theory]
    [InlineData("application.search", WorkspaceConfirmation.None)]
    [InlineData("application.profile.list", WorkspaceConfirmation.None)]
    [InlineData("application.open", WorkspaceConfirmation.Rememberable)]
    [InlineData("window.focus", WorkspaceConfirmation.Rememberable)]
    [InlineData("application.close", WorkspaceConfirmation.Fresh)]
    [InlineData("application.restart", WorkspaceConfirmation.Fresh)]
    public void Classifies_confirmation(string command, WorkspaceConfirmation expected) =>
        Assert.Equal(expected, WorkspaceActionPolicy.Classify(command).Confirmation);

    [Fact]
    public void Occupied_surface_replacement_requires_fresh_confirmation()
    {
        var action = new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new
        {
            applicationId = "pc.application:notepad",
            targetSurfaceId = "spatial.surface:west",
            replaceOccupied = true,
        }));

        Assert.Equal(WorkspaceConfirmation.Fresh, WorkspaceActionPolicy.Classify(action).Confirmation);
    }

    [Fact]
    public void Remembered_launch_scope_is_specific_to_one_application()
    {
        var first = WorkspaceActionPolicy.ScopeFor(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { applicationId = "pc.application:notepad" })), "workspace:one");
        var second = WorkspaceActionPolicy.ScopeFor(new WorkspaceDirective("application.open",
            JsonSerializer.SerializeToElement(new { applicationId = "pc.application:terminal" })), "workspace:two");

        Assert.Equal("workspace:one|application:pc.application:notepad", first);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Replacement_has_a_separate_fresh_requirement_after_the_launch_requirement()
    {
        var action = new WorkspaceDirective("application.open", JsonSerializer.SerializeToElement(new
        {
            applicationId = "pc.application:notepad",
            targetSurfaceId = "spatial.surface:west",
            replaceOccupied = true,
        }));

        var requirements = WorkspaceActionPolicy.Requirements(action);

        Assert.Collection(requirements,
            launch => Assert.Equal(new WorkspaceActionPolicyDecision("application.launch", WorkspaceConfirmation.Rememberable), launch),
            replace => Assert.Equal(new WorkspaceActionPolicyDecision("surface.replace", WorkspaceConfirmation.Fresh), replace));
    }
}
