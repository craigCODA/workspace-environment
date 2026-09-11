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

    [Theory]
    [InlineData("surface.dock", "docked")]
    [InlineData("surface.collapse", "collapsed")]
    public void Accepts_typed_surface_presentation_actions(string command, string stateProperty)
    {
        var result = SceneDirectiveParser.Parse(
            $"Done. [[scene:{{\"command\":\"{command}\",\"args\":{{\"entityId\":\"spatial.surface:chatgpt\",\"{stateProperty}\":true}}}}]]");

        Assert.Equal("Done.", result.SpokenText);
        Assert.Single(result.Directives);
        Assert.Equal(command, result.Directives[0].Command);
        Assert.Equal("spatial.surface:chatgpt", result.Directives[0].Arguments.GetProperty("entityId").GetString());
        Assert.True(result.Directives[0].Arguments.GetProperty(stateProperty).GetBoolean());
    }

    [Fact]
    public void Agent_prompt_vocabulary_matches_the_parser_allowlist()
    {
        Assert.Contains("camera.focus", SceneDirectiveParser.AgentPromptCommandList);
        Assert.Contains("surface.move", SceneDirectiveParser.AgentPromptCommandList);
        Assert.Contains("surface.dock", SceneDirectiveParser.AgentPromptCommandList);
        Assert.Contains("surface.collapse", SceneDirectiveParser.AgentPromptCommandList);
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
