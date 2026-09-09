using Workspace.Desktop.Core.Runtime;

namespace Workspace.Desktop.Core.Tests;

public sealed class SceneDirectiveParserTests
{
    [Fact]
    public void Extracts_typed_scene_actions_without_speaking_protocol_markup()
    {
        var result = SceneDirectiveParser.Parse(
            "I'll take you there. [[scene:{\"command\":\"camera.focus\",\"args\":{\"entityId\":\"terminal\"}}]]");

        Assert.Equal("I'll take you there.", result.SpokenText);
        Assert.Single(result.Directives);
        Assert.Equal("camera.focus", result.Directives[0].Command);
        Assert.Equal("terminal", result.Directives[0].Arguments.GetProperty("entityId").GetString());
    }

    [Fact]
    public void Rejects_unknown_scene_actions()
    {
        var result = SceneDirectiveParser.Parse(
            "No. [[scene:{\"command\":\"process.launch\",\"args\":{}}]]");

        Assert.Equal("No.", result.SpokenText);
        Assert.Empty(result.Directives);
    }
}
